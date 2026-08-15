using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Infrastructure.Logging;
using CmdsManager.Infrastructure.Windows;
using CmdsManager.Presentation.Forms;
using CmdsManager.Presentation.Controls;

namespace CmdsManager.Tests
{
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();

        [STAThread]
        private static int Main()
        {
            Run("INI parser", TestIniParser);
            Run("Configuration round-trip and conflict", TestConfigurationStore);
            Run("Script validation and command line", TestCommandBuilder);
            Run("Managed process execution", TestProcessExecution);
            Run("Cyrillic output encodings", TestCyrillicOutput);
            Run("Parallel launch sessions", TestParallelLaunchSessions);
            Run("Compact localized dialogs", TestCompactLocalizedDialogs);
            Run("Batched console output", TestBatchedConsoleOutput);
            Run("Single-instance command IPC", TestSingleInstanceCommandIpc);
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
                configuration.Application.ConsoleFontName = "Consolas";
                configuration.Application.ConsoleFontSize = 11.5f;
                configuration.Localization.Language = "en";
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
                        OutputEncoding = ScriptOutputEncoding.Windows1251,
                        StopTimeoutSeconds = 3
                    }
                });
                store.Save(configuration);

                var reloaded = store.Reload();
                Equal(true, reloaded.Application.StartWithWindows, "application setting");
                Equal(1, reloaded.Scripts.Count, "script count");
                Equal(42, reloaded.Scripts[0].Launch.AutoStartOrder, "auto-start order");
                Equal(ScriptInterpreter.Cmd, reloaded.Scripts[0].Launch.Interpreter, "interpreter");
                Equal(ScriptOutputEncoding.Windows1251, reloaded.Scripts[0].Launch.OutputEncoding, "output encoding");
                Equal("en", reloaded.Localization.Language, "selected language");
                Assert(reloaded.Localization.Languages.ContainsKey("ru") && reloaded.Localization.Languages.ContainsKey("en"), "Russian and English string tables");
                Equal("Start", reloaded.Localization.Languages["en"]["Main.Start"], "English string");
                Equal(0, reloaded.Localization.Languages["ru"].Keys.Except(reloaded.Localization.Languages["en"].Keys, StringComparer.OrdinalIgnoreCase).Count(), "every Russian key has an English value");
                Equal(0, reloaded.Localization.Languages["en"].Keys.Except(reloaded.Localization.Languages["ru"].Keys, StringComparer.OrdinalIgnoreCase).Count(), "every English key has a Russian value");
                Equal(11.5f, reloaded.Application.ConsoleFontSize, "console font size");
                var savedText = File.ReadAllText(configPath, Encoding.UTF8);
                Assert(savedText.Contains("[Strings.ru]") && savedText.Contains("[Strings.en]"), "localization is stored in INI");

                reloaded.Scripts[0].Name = "Second save";
                store.Save(reloaded);
                Assert(File.Exists(configPath + ".bak"), "atomic save backup exists");

                var conflicted = store.Reload();
                File.AppendAllText(configPath, Environment.NewLine + "; external change", Encoding.UTF8);
                Expect<ConfigurationChangedException>(() => store.Save(conflicted), "external change detection");

                var legacyPath = Path.Combine(directory, "Legacy.ini");
                File.WriteAllText(legacyPath, "[Application]\r\nConfigVersion=1\r\n", new UTF8Encoding(false));
                var legacy = new ConfigurationStore(legacyPath).LoadOrCreate();
                Equal(2, legacy.Application.ConfigVersion, "legacy configuration version is upgraded");
                Assert(File.ReadAllText(legacyPath, Encoding.UTF8).Contains("[Strings.ru]"), "legacy configuration receives localization strings");
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

        private static void TestCyrillicOutput()
        {
            WithTemporaryDirectory(directory =>
            {
                const string oemPhrase = "Привет из OEM";
                var oem = Encoding.GetEncoding((int)GetOEMCP());
                if (oem.GetString(oem.GetBytes(oemPhrase)) == oemPhrase)
                {
                    var cmdPath = Path.Combine(directory, "russian-oem.cmd");
                    File.WriteAllText(cmdPath, "@echo off\r\necho " + oemPhrase + "\r\n", oem);
                    var cmdLines = RunAndCapture(directory, new ScriptDefinition
                    {
                        Name = "Russian CMD",
                        Path = cmdPath,
                        Launch = new LaunchProfile
                        {
                            Interpreter = ScriptInterpreter.Cmd,
                            OutputEncoding = ScriptOutputEncoding.Auto,
                            WorkingDirectory = directory,
                            CaptureOutput = true
                        }
                    });
                    Assert(cmdLines.Any(line => line.Contains(oemPhrase)), "Auto decodes Windows OEM Cyrillic");

                    var powerShellPath = Path.Combine(directory, "russian.ps1");
                    File.WriteAllText(powerShellPath,
                        "[Console]::OutputEncoding = [Text.Encoding]::GetEncoding(" + GetOEMCP() + ")\r\nWrite-Output 'Русский PowerShell'\r\n",
                        new UTF8Encoding(true));
                    var interpreters = new List<ScriptInterpreter> { ScriptInterpreter.WindowsPowerShell };
                    var pwsh = FindPowerShell7();
                    if (!string.IsNullOrEmpty(pwsh)) interpreters.Add(ScriptInterpreter.PowerShell7);
                    foreach (var interpreter in interpreters)
                    {
                        var lines = RunAndCapture(directory, new ScriptDefinition
                        {
                            Name = interpreter.ToString(),
                            Path = powerShellPath,
                            Launch = new LaunchProfile
                            {
                                Interpreter = interpreter,
                                OutputEncoding = ScriptOutputEncoding.Auto,
                                WorkingDirectory = directory,
                                CaptureOutput = true
                            }
                        }, pwsh);
                        Assert(lines.Any(line => line.Contains("Русский PowerShell")), interpreter + " decodes Cyrillic");
                    }
                }

                var utf8Path = Path.Combine(directory, "russian-utf8.cmd");
                var prefix = Encoding.ASCII.GetBytes("@echo off\r\nchcp 65001>nul\r\n");
                var suffix = new UTF8Encoding(false).GetBytes("echo Привет UTF-8\r\n");
                File.WriteAllBytes(utf8Path, prefix.Concat(suffix).ToArray());
                var utf8Lines = RunAndCapture(directory, new ScriptDefinition
                {
                    Name = "Russian UTF-8",
                    Path = utf8Path,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        OutputEncoding = ScriptOutputEncoding.Utf8,
                        WorkingDirectory = directory,
                        CaptureOutput = true
                    }
                });
                Assert(utf8Lines.Any(line => line.Contains("Привет UTF-8")), "explicit UTF-8 decoding");
            });
        }

        private static void TestParallelLaunchSessions()
        {
            WithTemporaryDirectory(directory =>
            {
                var scriptPath = Path.Combine(directory, "parallel.cmd");
                File.WriteAllText(scriptPath, "@echo off\r\nping 127.0.0.1 -n 4 >nul\r\n", Encoding.ASCII);
                var script = new ScriptDefinition
                {
                    Name = "Parallel",
                    Path = scriptPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        WorkingDirectory = directory,
                        CaptureOutput = true,
                        AllowParallelInstances = true,
                        StopPolicy = ScriptStopPolicy.Kill,
                        StopTimeoutSeconds = 0
                    }
                };
                var processIds = new List<int>();
                var started = new ManualResetEventSlim(false);
                using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                using (var supervisor = new ProcessSupervisor(new ScriptCommandBuilder(directory), logger, () => false))
                {
                    supervisor.InstanceStarted += (sender, args) =>
                    {
                        lock (processIds)
                        {
                            processIds.Add(args.ProcessId);
                            if (processIds.Count == 2) started.Set();
                        }
                    };
                    supervisor.Start(script, string.Empty);
                    supervisor.Start(script, string.Empty);
                    Assert(started.Wait(TimeSpan.FromSeconds(5)), "two launch-session events are published");
                    lock (processIds)
                    {
                        Equal(2, processIds.Distinct().Count(), "each launch has a unique PID for its console tab");
                    }
                    Assert(supervisor.StopAllAsync().Wait(TimeSpan.FromSeconds(5)), "parallel sessions stop");
                    Assert(SpinWait.SpinUntil(() => !supervisor.HasRunningProcesses, TimeSpan.FromSeconds(5)), "parallel exit observers finish");
                }
            });
        }

        private static void TestCompactLocalizedDialogs()
        {
            WithTemporaryDirectory(directory =>
            {
                var store = new ConfigurationStore(Path.Combine(directory, "CmdsManager.ini"));
                var configuration = store.LoadOrCreate();
                configuration.Localization.Language = "en";
                var state = new ConfigurationState(configuration);
                var text = new LocalizationService(state);
                using (var about = new AboutForm(text))
                using (var settings = new SettingsForm(configuration.Application, configuration.PowerShell7Path, configuration.Localization, text))
                using (var script = new ScriptEditorForm(null, configuration.Defaults, directory, text))
                {
                    Assert(about.ClientSize.Width <= 400 && about.ClientSize.Height <= 180, "About box is compact");
                    Assert(settings.ClientSize.Width <= 600 && settings.ClientSize.Height <= 370, "settings dialog is compact");
                    Assert(script.ClientSize.Width <= 630 && script.ClientSize.Height <= 430, "script editor is compact");
                    Equal("About", about.Text, "English About title comes from INI strings");
                    Equal("CmdsManager settings", settings.Text, "English settings title comes from INI strings");
                }
            });
        }

        private static void TestSingleInstanceCommandIpc()
        {
            var received = string.Empty;
            var arrived = new ManualResetEventSlim(false);
            using (var primary = new SingleInstanceGuard())
            {
                Assert(primary.IsPrimaryInstance, "first guard owns the instance mutex");
                primary.StartListening(() => { }, command =>
                {
                    received = command;
                    arrived.Set();
                });
                using (var secondary = new SingleInstanceGuard())
                {
                    Assert(!secondary.IsPrimaryInstance, "second guard is secondary");
                    Assert(secondary.SendCommand("RUN test"), "secondary sends a pipe command");
                    Assert(arrived.Wait(TimeSpan.FromSeconds(3)), "primary receives the pipe command");
                    Equal("RUN test", received, "pipe command payload");
                }
            }
        }

        private static void TestBatchedConsoleOutput()
        {
            WithTemporaryDirectory(directory =>
            {
                var configuration = new ConfigurationStore(Path.Combine(directory, "CmdsManager.ini")).LoadOrCreate();
                configuration.Application.ConsoleFontName = "Consolas";
                configuration.Application.ConsoleFontSize = 12f;
                var state = new ConfigurationState(configuration);
                var text = new LocalizationService(state);
                using (var console = new ConsoleTabsControl(text, () => state.Current.Application))
                {
                    console.CreateControl();
                    var scriptId = Guid.NewGuid();
                    const int processId = 4242;
                    console.EnqueueStarted(new ScriptInstanceEventArgs(scriptId, "Fast output", processId, DateTime.Now, true, null));
                    for (var index = 0; index < 20000; index++)
                        console.EnqueueOutput(new ScriptOutputEventArgs(scriptId, processId, "строка-" + index, false));

                    var elapsed = Stopwatch.StartNew();
                    RichTextBox output = null;
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        var tabs = console.Controls.OfType<TabControl>().FirstOrDefault();
                        if (tabs != null && tabs.TabPages.Count > 0)
                            output = tabs.TabPages[0].Controls.OfType<RichTextBox>().FirstOrDefault();
                        if (output != null && output.Text.Contains("строка-19999")) break;
                        Thread.Sleep(10);
                    }

                    Assert(output != null && output.Text.Contains("строка-19999"), "20,000 lines reach the console in one bounded batch window");
                    Assert(!output.Text.Contains("[4242 OUT]"), "console text has no PID OUT prefix");
                    Assert(output.TextLength <= 200000, "console history is bounded");
                    Assert(Math.Abs(output.Font.SizeInPoints - 12f) < 0.1f, "configured console font size is applied");
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

        private static List<string> RunAndCapture(string directory, ScriptDefinition script, string powerShell7Path = "")
        {
            var lines = new List<string>();
            var completed = new ManualResetEventSlim(false);
            using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
            using (var supervisor = new ProcessSupervisor(new ScriptCommandBuilder(directory), logger, () => false))
            {
                supervisor.OutputReceived += (sender, args) =>
                {
                    lock (lines) lines.Add(args.Line);
                };
                supervisor.StateChanged += (sender, args) =>
                {
                    if (args.Snapshot.ScriptId == script.Id &&
                        (args.Snapshot.State == ScriptRuntimeState.Exited || args.Snapshot.State == ScriptRuntimeState.Failed))
                        completed.Set();
                };
                supervisor.Start(script, powerShell7Path ?? string.Empty);
                Assert(completed.Wait(TimeSpan.FromSeconds(15)), script.Name + " exits within timeout");
            }
            lock (lines) return lines.ToList();
        }

        private static string FindPowerShell7()
        {
            var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            if (File.Exists(standard)) return standard;
            foreach (var rawDirectory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(rawDirectory.Trim().Trim('"'), "pwsh.exe");
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch (ArgumentException)
                {
                }
            }
            return string.Empty;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetOEMCP();

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
                    var expectedRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var resolvedRoot = Path.GetFullPath(root);
                    if (!resolvedRoot.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Unsafe test cleanup path: " + resolvedRoot);
                    }

                    for (var attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            Directory.Delete(resolvedRoot, true);
                            break;
                        }
                        catch (IOException)
                        {
                            if (attempt == 5) throw;
                            Thread.Sleep(100);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            if (attempt == 5) throw;
                            Thread.Sleep(100);
                        }
                    }
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
