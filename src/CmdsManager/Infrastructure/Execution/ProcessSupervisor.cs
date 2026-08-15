using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Windows;

namespace CmdsManager.Infrastructure.Execution
{
    public sealed class ProcessSupervisor : IDisposable
    {
        private sealed class RunningProcess
        {
            internal Guid InstanceId { get; } = Guid.NewGuid();
            internal ScriptDefinition Script { get; set; }
            internal NativeProcess Native { get; set; }
            internal DateTime StartedAt { get; set; }
            internal bool StopRequested { get; set; }
            internal bool CapturesOutput { get; set; }
            internal Task OutputTask { get; set; }
            internal Task ErrorTask { get; set; }
        }

        private readonly object _sync = new object();
        private readonly Dictionary<Guid, List<RunningProcess>> _running = new Dictionary<Guid, List<RunningProcess>>();
        private readonly Dictionary<Guid, ScriptRuntimeSnapshot> _snapshots = new Dictionary<Guid, ScriptRuntimeSnapshot>();
        private readonly ScriptCommandBuilder _commandBuilder;
        private readonly IExecutionLog _log;
        private readonly Func<bool> _logScriptOutput;
        private bool _disposed;

        public ProcessSupervisor(ScriptCommandBuilder commandBuilder, IExecutionLog log, Func<bool> logScriptOutput)
        {
            _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _logScriptOutput = logScriptOutput ?? (() => false);
        }

        public event EventHandler<ScriptStateChangedEventArgs> StateChanged;
        public event EventHandler<ScriptOutputEventArgs> OutputReceived;
        public event EventHandler<ScriptInstanceEventArgs> InstanceStarted;
        public event EventHandler<ScriptInstanceEventArgs> InstanceExited;

        public bool HasRunningProcesses
        {
            get
            {
                lock (_sync)
                {
                    return _running.Values.Any(list => list.Count > 0);
                }
            }
        }

        public ScriptRuntimeSnapshot Start(ScriptDefinition script, string powerShell7Path)
        {
            ThrowIfDisposed();
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            lock (_sync)
            {
                List<RunningProcess> existing;
                if (_running.TryGetValue(script.Id, out existing) && existing.Count > 0 && !script.Launch.AllowParallelInstances)
                {
                    throw new InvalidOperationException("This script is already running and parallel instances are disabled.");
                }
            }

            Publish(new ScriptRuntimeSnapshot
            {
                ScriptId = script.Id,
                State = ScriptRuntimeState.Starting
            });

            try
            {
                var spec = _commandBuilder.Build(script, powerShell7Path);
                var native = NativeProcessLauncher.Start(spec);
                var session = new RunningProcess
                {
                    Script = script.Clone(),
                    Native = native,
                    StartedAt = DateTime.Now,
                    CapturesOutput = spec.CaptureOutput
                };

                lock (_sync)
                {
                    List<RunningProcess> list;
                    if (!_running.TryGetValue(script.Id, out list))
                    {
                        list = new List<RunningProcess>();
                        _running.Add(script.Id, list);
                    }

                    list.Add(session);
                }

                InstanceStarted?.Invoke(this, new ScriptInstanceEventArgs(script.Id, script.Name, native.ProcessId, session.StartedAt, spec.CaptureOutput, null));
                session.OutputTask = StartReader(session, native.StandardOutput, false);
                session.ErrorTask = StartReader(session, native.StandardError, true);
                Task.Factory.StartNew(
                    () => WaitForExit(session),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                _log.Information("Started script '" + SafeName(script.Name) + "' with PID " + native.ProcessId + ".");
                return PublishCurrent(script.Id, ScriptRuntimeState.Running, native.ProcessId, session.StartedAt, null, string.Empty);
            }
            catch (Exception exception)
            {
                _log.Error("Failed to start script '" + SafeName(script.Name) + "'.", exception);
                Publish(new ScriptRuntimeSnapshot
                {
                    ScriptId = script.Id,
                    State = ScriptRuntimeState.Failed,
                    Error = exception.Message
                });
                throw;
            }
        }

        public Task StopAsync(Guid scriptId)
        {
            ThrowIfDisposed();
            List<RunningProcess> sessions;
            lock (_sync)
            {
                List<RunningProcess> current;
                if (!_running.TryGetValue(scriptId, out current) || current.Count == 0)
                {
                    return Task.FromResult(0);
                }

                sessions = current.ToList();
                foreach (var session in sessions)
                {
                    session.StopRequested = true;
                }
            }

            PublishCurrent(scriptId, ScriptRuntimeState.Stopping, sessions[0].Native.ProcessId, sessions[0].StartedAt, null, string.Empty);
            return Task.Factory.StartNew(() =>
            {
                foreach (var session in sessions)
                {
                    StopOne(session);
                }
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }

        public Task StopAllAsync()
        {
            ThrowIfDisposed();
            Guid[] identifiers;
            lock (_sync)
            {
                identifiers = _running.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key).ToArray();
            }

            return Task.WhenAll(identifiers.Select(StopAsync));
        }

        public ScriptRuntimeSnapshot GetSnapshot(Guid scriptId)
        {
            lock (_sync)
            {
                ScriptRuntimeSnapshot snapshot;
                if (_snapshots.TryGetValue(scriptId, out snapshot))
                {
                    return CloneSnapshot(snapshot);
                }

                return new ScriptRuntimeSnapshot
                {
                    ScriptId = scriptId,
                    State = ScriptRuntimeState.Stopped
                };
            }
        }

        public bool IsRunning(Guid scriptId)
        {
            lock (_sync)
            {
                List<RunningProcess> sessions;
                return _running.TryGetValue(scriptId, out sessions) && sessions.Count > 0;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                StopAllAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _log.Error("Unable to stop every script during shutdown.", exception);
            }

            _disposed = true;
            RunningProcess[] remaining;
            lock (_sync)
            {
                remaining = _running.Values.SelectMany(value => value).ToArray();
                _running.Clear();
            }

            foreach (var session in remaining)
            {
                session.Native.Dispose();
            }
        }

        private Task StartReader(RunningProcess session, System.IO.StreamReader reader, bool isError)
        {
            if (reader == null)
            {
                return Task.FromResult(0);
            }

            return Task.Factory.StartNew(() =>
            {
                try
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        OutputReceived?.Invoke(this, new ScriptOutputEventArgs(session.Script.Id, session.Native.ProcessId, line, isError));
                        if (_logScriptOutput())
                        {
                            _log.Information("Script '" + SafeName(session.Script.Name) + "' " + (isError ? "stderr" : "stdout") + ": " + line);
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void WaitForExit(RunningProcess session)
        {
            var waitResult = NativeMethods.WaitForSingleObject(session.Native.ProcessHandle, NativeMethods.Infinite);
            uint rawExitCode = 1;
            if (waitResult == NativeMethods.WaitObject0)
            {
                NativeMethods.GetExitCodeProcess(session.Native.ProcessHandle, out rawExitCode);
            }

            var exitCode = unchecked((int)rawExitCode);
            var outputTasks = new[] { session.OutputTask, session.ErrorTask }.Where(task => task != null).ToArray();

            if (session.Native.JobHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(session.Native.JobHandle);
                session.Native.JobHandle = IntPtr.Zero;
            }

            if (outputTasks.Length > 0)
            {
                try
                {
                    Task.WaitAll(outputTasks, TimeSpan.FromSeconds(2));
                }
                catch (AggregateException)
                {
                }
            }

            session.Native.Dispose();

            ScriptRuntimeSnapshot snapshot;
            lock (_sync)
            {
                List<RunningProcess> list;
                if (_running.TryGetValue(session.Script.Id, out list))
                {
                    list.RemoveAll(item => item.InstanceId == session.InstanceId);
                    if (list.Count == 0)
                    {
                        _running.Remove(session.Script.Id);
                        snapshot = new ScriptRuntimeSnapshot
                        {
                            ScriptId = session.Script.Id,
                            State = ScriptRuntimeState.Exited,
                            ActiveCount = 0,
                            LastExitCode = exitCode
                        };
                    }
                    else
                    {
                        var newest = list.OrderByDescending(item => item.StartedAt).First();
                        snapshot = new ScriptRuntimeSnapshot
                        {
                            ScriptId = session.Script.Id,
                            State = ScriptRuntimeState.Running,
                            ActiveCount = list.Count,
                            ProcessId = newest.Native.ProcessId,
                            StartedAt = newest.StartedAt,
                            LastExitCode = exitCode
                        };
                    }
                }
                else
                {
                    snapshot = new ScriptRuntimeSnapshot
                    {
                        ScriptId = session.Script.Id,
                        State = ScriptRuntimeState.Exited,
                        ActiveCount = 0,
                        LastExitCode = exitCode
                    };
                }

                _snapshots[session.Script.Id] = CloneSnapshot(snapshot);
            }

            _log.Information("Script '" + SafeName(session.Script.Name) + "' exited with code " + exitCode + ".");
            InstanceExited?.Invoke(this, new ScriptInstanceEventArgs(session.Script.Id, session.Script.Name, session.Native.ProcessId, session.StartedAt, session.CapturesOutput, exitCode));
            StateChanged?.Invoke(this, new ScriptStateChangedEventArgs(CloneSnapshot(snapshot)));
        }

        private void StopOne(RunningProcess session)
        {
            if (NativeMethods.WaitForSingleObject(session.Native.ProcessHandle, 0) == NativeMethods.WaitObject0)
            {
                return;
            }

            var closedWindow = false;
            if (session.Script.Launch.StopPolicy == ScriptStopPolicy.GracefulThenKill)
            {
                try
                {
                    using (var process = Process.GetProcessById(session.Native.ProcessId))
                    {
                        closedWindow = process.CloseMainWindow();
                    }
                }
                catch (ArgumentException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            if (closedWindow)
            {
                var timeout = checked((uint)Math.Min(int.MaxValue, session.Script.Launch.StopTimeoutSeconds * 1000L));
                if (NativeMethods.WaitForSingleObject(session.Native.ProcessHandle, timeout) == NativeMethods.WaitObject0)
                {
                    return;
                }
            }

            if (session.Native.JobHandle != IntPtr.Zero && !NativeMethods.TerminateJobObject(session.Native.JobHandle, 1))
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (error != 5 && NativeMethods.WaitForSingleObject(session.Native.ProcessHandle, 0) != NativeMethods.WaitObject0)
                {
                    _log.Warning("Windows could not terminate a Job Object for script '" + SafeName(session.Script.Name) + "'. Error " + error + ".");
                }
            }

            NativeMethods.WaitForSingleObject(session.Native.ProcessHandle, 2000);
        }

        private ScriptRuntimeSnapshot PublishCurrent(Guid scriptId, ScriptRuntimeState state, int? processId, DateTime? startedAt, int? exitCode, string error)
        {
            int activeCount;
            lock (_sync)
            {
                List<RunningProcess> list;
                activeCount = _running.TryGetValue(scriptId, out list) ? list.Count : 0;
            }

            var snapshot = new ScriptRuntimeSnapshot
            {
                ScriptId = scriptId,
                State = state,
                ActiveCount = activeCount,
                ProcessId = processId,
                StartedAt = startedAt,
                LastExitCode = exitCode,
                Error = error ?? string.Empty
            };
            Publish(snapshot);
            return snapshot;
        }

        private void Publish(ScriptRuntimeSnapshot snapshot)
        {
            lock (_sync)
            {
                _snapshots[snapshot.ScriptId] = CloneSnapshot(snapshot);
            }

            StateChanged?.Invoke(this, new ScriptStateChangedEventArgs(CloneSnapshot(snapshot)));
        }

        private static ScriptRuntimeSnapshot CloneSnapshot(ScriptRuntimeSnapshot snapshot)
        {
            return new ScriptRuntimeSnapshot
            {
                ScriptId = snapshot.ScriptId,
                State = snapshot.State,
                ActiveCount = snapshot.ActiveCount,
                ProcessId = snapshot.ProcessId,
                StartedAt = snapshot.StartedAt,
                LastExitCode = snapshot.LastExitCode,
                Error = snapshot.Error
            };
        }

        private static string SafeName(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProcessSupervisor));
            }
        }
    }
}
