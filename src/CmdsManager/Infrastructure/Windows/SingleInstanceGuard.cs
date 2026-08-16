using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CmdsManager.Infrastructure.Windows
{
    public sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = @"Local\CmdsManager.Main.Instance";
        private const string ActivationEventName = @"Local\CmdsManager.Main.Activate";
        private readonly string _commandPipeName;
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _activationEvent;
        private Thread _listener;
        private Thread _commandListener;
        private volatile bool _stopping;
        private bool _disposed;

        public SingleInstanceGuard(string scope = null)
        {
            var suffix = ScopeSuffix(scope);
            bool createdNew;
            _mutex = new Mutex(true, MutexName + suffix, out createdNew);
            IsPrimaryInstance = createdNew;
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName + suffix);
            _commandPipeName = "CmdsManager.Main.Commands." + Process.GetCurrentProcess().SessionId + suffix;
        }

        public bool IsPrimaryInstance { get; }

        public void SignalPrimaryInstance()
        {
            if (!IsPrimaryInstance)
            {
                _activationEvent.Set();
            }
        }

        public bool SendCommand(string command, int timeoutMilliseconds = 2000)
        {
            if (IsPrimaryInstance || string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            try
            {
                using (var client = new NamedPipeClientStream(".", _commandPipeName, PipeDirection.Out, PipeOptions.None))
                {
                    client.Connect(timeoutMilliseconds);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                    {
                        writer.WriteLine(command.Replace("\r", string.Empty).Replace("\n", string.Empty));
                    }
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void StartListening(Action activationRequested, Action<string> commandReceived = null)
        {
            if (!IsPrimaryInstance)
            {
                throw new InvalidOperationException("Only the primary instance can listen for activation.");
            }

            if (activationRequested == null)
            {
                throw new ArgumentNullException(nameof(activationRequested));
            }

            if (_listener != null)
            {
                return;
            }

            _listener = new Thread(() =>
            {
                while (!_stopping)
                {
                    _activationEvent.WaitOne();
                    if (!_stopping)
                    {
                        activationRequested();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "CmdsManager activation listener"
            };
            _listener.Start();

            if (commandReceived != null)
            {
                _commandListener = new Thread(() => ListenForCommands(commandReceived))
                {
                    IsBackground = true,
                    Name = "CmdsManager command listener"
                };
                _commandListener.Start();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            _activationEvent.Set();
            WakeCommandListener();
            if (_listener != null && _listener.IsAlive)
            {
                _listener.Join(1000);
            }
            if (_commandListener != null && _commandListener.IsAlive)
            {
                _commandListener.Join(1000);
            }

            _activationEvent.Dispose();
            if (IsPrimaryInstance)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }

            _mutex.Dispose();
        }

        private void ListenForCommands(Action<string> commandReceived)
        {
            while (!_stopping)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(_commandPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server, new UTF8Encoding(false, true), false, 1024, true))
                        {
                            var command = reader.ReadLine();
                            if (!_stopping && !string.IsNullOrWhiteSpace(command) && command.Length <= 8192)
                            {
                                commandReceived(command);
                            }
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void WakeCommandListener()
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", _commandPipeName, PipeDirection.Out, PipeOptions.None))
                {
                    client.Connect(200);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false), 128, true) { AutoFlush = true })
                    {
                        writer.WriteLine(string.Empty);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (TimeoutException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string ScopeSuffix(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope)) return string.Empty;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(scope.Trim()));
                var builder = new StringBuilder(".");
                for (var index = 0; index < 8; index++) builder.Append(hash[index].ToString("X2"));
                return builder.ToString();
            }
        }
    }
}
