using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Infrastructure.Logging;

namespace CmdsManager.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Run("INI parser", TestIniParser);
            Run("Configuration round-trip and conflict", TestConfigurationStore);
            Run("Script validation and command line", TestCommandBuilder);
            Run("Managed process execution", TestProcessExecution);
            Run("Job Object stops child processes", TestProcessTreeStop);

            Console.WriteLine();
            if (Failures.Count == 0)
            {
                Console.WriteLine("All tests passed.");
                return 0;
            }

            Console.Error.WriteLine(Failures.Count + " test(s) failed:");
            foreach (var failure in Failures)
            {
                Console.Error.WriteLine("- " + failure);
            }

            return 1;
        }

        private static void TestIniParser()
        {
            var document = IniDocument.Parse("; comment\n[Application]\nName=Cmds=Manager\nEnabled=true\n");
            Equal("Cmds=Manager", document.Get("application", "name"), "value after first equals sign");
            Equal("true", document.Get("APPLICATION", "ENABLED"), "case-insensitive lookup");
            var serialized = document.Serialize();
            Assert(serialized.Contains("[Application]"), "section is serialized");
            Assert(serialized.Contains("Name=Cmds=Manager"), "value is serialized");

            Expect<IniFormatException>(() => IniDocument.Parse("key=value"), "key outside a section");
        }

        private static void TestConfigurationStore()
        {
            WithTemporaryDirectory(directory =>
            {
                var configPath = Path.Combine(directory, "CmdsManager.ini");
                var scriptPath = Path.Combine(directory, "sample.cmd");
                File.WriteAllText(scriptPath, "@exit /b 0\r\n", Encoding.ASCII);
                var store = new ConfigurationStore(configPath);
                var configuration = store.LoadOrCreate();
                configuration.Application.StartWithWindows = true;
                configuration.Application.LogScriptOutput = false;
                configuration.Scripts.Add(new ScriptDefinition
                {
                    Id = Guid.Parse("69e1f16b-4daa-4334-92c0-95b0a3baee55"),
                    Name = "Round trip",
                    Path = scriptPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        AutoStartWithApplication = true,
                        AutoStartOrder = 42,
                        StopTimeoutSeconds = 3
                    }
                });
                store.Save(configuration);

                var reloaded = store.Reload();
                Equal(true, reloaded.Application.StartWithWindows, "application setting");
                Equal(1, reloaded.Scripts.Count, "script count");
                Equal(42, reloaded.Scripts[0].Launch.AutoStartOrder, "auto-start order");
                Equal(ScriptInterpreter.Cmd, reloaded.Scripts[0].Launch.Interpreter, "interpreter");

                reloaded.Scripts[0].Name = "Second save";
                store.Save(reloaded);
                Assert(File.Exists(configPath + ".bak"), "atomic save backup exists");

                var conflicted = store.Reload();
                File.AppendAllText(configPath, Environment.NewLine + "; external change", Encoding.UTF8);
                Expect<ConfigurationChangedException>(() => store.Save(conflicted), "external change detection");
            });
        }

        private static void TestCommandBuilder()
        {
            WithTemporaryDirectory(directory =>
            {
                var scriptPath = Path.Combine(directory, "script with spaces.cmd");
                File.WriteAllText(scriptPath, "@exit /b 0\r\n", Encoding.ASCII);
                var script = new ScriptDefinition
                {
                    Name = "Command",
                    Path = scriptPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Auto,
                        Arguments = "one two",
                        WorkingDirectory = directory,
                        CaptureOutput = true
                    }
                };

                ScriptDefinitionValidator.Validate(script, true);
                Equal(ScriptInterpreter.Cmd, ScriptDefinitionValidator.ResolveAutoInterpreter(scriptPath), "auto interpreter");
                var builder = new ScriptCommandBuilder(directory);
                var spec = builder.Build(script, string.Empty);
                Equal(ScriptInterpreter.Cmd, spec.Interpreter, "resolved command interpreter");
                Assert(File.Exists(spec.ExecutablePath), "cmd.exe exists");
                Assert(spec.Arguments.Contains("script with spaces.cmd"), "quoted script path is present");
                Equal("\"a b\"", ScriptCommandBuilder.QuoteWindowsArgument("a b"), "Windows argument quoting");
            });
        }

        private static void TestProcessExecution()
        {
            WithTemporaryDirectory(directory =>
            {
                var scriptPath = Path.Combine(directory, "exit-code.cmd");
                File.WriteAllText(scriptPath, "@echo off\r\necho managed-output\r\nexit /b 7\r\n", Encoding.ASCII);
                var script = new ScriptDefinition
                {
                    Name = "Execution",
                    Path = scriptPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        WorkingDirectory = directory,
                        WindowMode = ScriptWindowMode.Hidden,
                        CaptureOutput = true,
                        StopTimeoutSeconds = 1
                    }
                };

                var lines = new List<string>();
                var completed = new ManualResetEventSlim(false);
                ScriptRuntimeSnapshot final = null;
                using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                using (var supervisor = new ProcessSupervisor(new ScriptCommandBuilder(directory), logger, () => false))
                {
                    supervisor.OutputReceived += (sender, args) =>
                    {
                        lock (lines)
                        {
                            lines.Add(args.Line);
                        }
                    };
                    supervisor.StateChanged += (sender, args) =>
                    {
                        if (args.Snapshot.ScriptId == script.Id && (args.Snapshot.State == ScriptRuntimeState.Exited || args.Snapshot.State == ScriptRuntimeState.Failed))
                        {
                            final = args.Snapshot;
                            completed.Set();
                        }
                    };

                    var started = supervisor.Start(script, string.Empty);
                    Assert(started.ProcessId.HasValue, "PID is assigned");
                    Assert(completed.Wait(TimeSpan.FromSeconds(10)), "process exits within timeout");
                    Equal(ScriptRuntimeState.Exited, final.State, "final state");
                    Equal(7, final.LastExitCode.GetValueOrDefault(), "exit code");
                    SpinWait.SpinUntil(() =>
                    {
                        lock (lines)
                        {
                            return lines.Any(line => line.Contains("managed-output"));
                        }
                    }, TimeSpan.FromSeconds(2));
                    lock (lines)
                    {
                        Assert(lines.Any(line => line.Contains("managed-output")), "stdout is captured");
                    }
                }
            });
        }

        private static void TestProcessTreeStop()
        {
            WithTemporaryDirectory(directory =>
            {
                var childPath = Path.Combine(directory, "child.cmd");
                var parentPath = Path.Combine(directory, "parent.cmd");
                var startedFlag = Path.Combine(directory, "child-started.flag");
                var orphanFlag = Path.Combine(directory, "orphan.flag");
                var windowsPowerShell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                File.WriteAllText(
                    childPath,
                    "@echo off\r\n" +
                    "echo started>\"" + startedFlag + "\"\r\n" +
                    "\"" + windowsPowerShell + "\" -NoLogo -NoProfile -Command \"Start-Sleep -Seconds 3\"\r\n" +
                    "echo orphan>\"" + orphanFlag + "\"\r\n",
                    Encoding.ASCII);
                File.WriteAllText(
                    parentPath,
                    "@echo off\r\n" +
                    "start \"\" /b \"%ComSpec%\" /d /s /c \"\"" + childPath + "\"\"\r\n" +
                    "\"" + windowsPowerShell + "\" -NoLogo -NoProfile -Command \"Start-Sleep -Seconds 30\"\r\n",
                    Encoding.ASCII);

                var script = new ScriptDefinition
                {
                    Name = "Tree",
                    Path = parentPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        WorkingDirectory = directory,
                        WindowMode = ScriptWindowMode.Hidden,
                        CaptureOutput = true,
                        StopPolicy = ScriptStopPolicy.Kill,
                        StopTimeoutSeconds = 0
                    }
                };

                using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                using (var supervisor = new ProcessSupervisor(new ScriptCommandBuilder(directory), logger, () => false))
                {
                    supervisor.Start(script, string.Empty);
                    Assert(SpinWait.SpinUntil(() => File.Exists(startedFlag), TimeSpan.FromSeconds(5)), "child process starts");
                    var stop = supervisor.StopAsync(script.Id);
                    Assert(stop.Wait(TimeSpan.FromSeconds(5)), "stop completes");
                    Assert(SpinWait.SpinUntil(() => !supervisor.IsRunning(script.Id), TimeSpan.FromSeconds(5)), "runtime state becomes stopped");
                    Thread.Sleep(TimeSpan.FromSeconds(4));
                    Assert(!File.Exists(orphanFlag), "child process cannot survive the Job Object");
                }
            });
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            var root = Path.Combine(Path.GetTempPath(), "CmdsManagerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                action(root);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                Failures.Add(name + ": " + exception.Message);
                Console.Error.WriteLine("FAIL " + name + ": " + exception);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Assertion failed: " + message);
            }
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException("Assertion failed: " + message + ". Expected " + expected + ", actual " + actual + ".");
            }
        }

        private static void Expect<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException("Assertion failed: expected " + typeof(T).Name + " for " + message + ".");
        }
    }
}
