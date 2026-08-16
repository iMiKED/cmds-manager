using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
            Run("Running script visual indicator", TestRunningVisualIndicator);
            Run("Managed child CMD tabs", TestManagedChildCmdTabs);
            Run("Transformed START command IPC", TestTransformedStartCommandIpc);
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
                configuration.Application.Theme = ApplicationTheme.Dark;
                configuration.Application.LogScriptOutput = false;
                configuration.Application.ConsoleFontName = "Consolas";
                configuration.Application.ConsoleFontSize = 11.5f;
                configuration.Application.ConsolePaneHeight = 210;
                configuration.Application.ConsoleForegroundColor = "#A1B2C3";
                configuration.Application.ConsoleBackgroundColor = "#102030";
                configuration.Application.ConsoleBackgroundOpacity = 82;
                configuration.Application.ConsoleTabForegroundColor = "#112233";
                configuration.Application.ConsoleActiveTabForegroundColor = "#F1F2F3";
                configuration.Application.ConsoleTabBackgroundColor = "#CCDDEE";
                configuration.Application.ConsoleTabBackgroundOpacity = 65;
                configuration.Application.ConsoleActiveTabBackgroundColor = "#203040";
                configuration.Application.ConsoleActiveTabBackgroundOpacity = 73;
                configuration.Application.MainWindowPlacementSaved = true;
                configuration.Application.MainWindowX = 123;
                configuration.Application.MainWindowY = 234;
                configuration.Application.MainWindowWidth = 1280;
                configuration.Application.MainWindowHeight = 760;
                configuration.Application.MainWindowMaximized = true;
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
                        WordWrap = true,
                        StopTimeoutSeconds = 3
                    }
                });
                store.Save(configuration);

                var reloaded = store.Reload();
                Equal(true, reloaded.Application.StartWithWindows, "application setting");
                Equal(ApplicationTheme.Dark, reloaded.Application.Theme, "application theme");
                Equal(1, reloaded.Scripts.Count, "script count");
                Equal(42, reloaded.Scripts[0].Launch.AutoStartOrder, "auto-start order");
                Equal(ScriptInterpreter.Cmd, reloaded.Scripts[0].Launch.Interpreter, "interpreter");
                Equal(ScriptOutputEncoding.Windows1251, reloaded.Scripts[0].Launch.OutputEncoding, "output encoding");
                Equal(true, reloaded.Scripts[0].Launch.WordWrap, "script word-wrap setting");
                Equal("en", reloaded.Localization.Language, "selected language");
                Assert(reloaded.Localization.Languages.ContainsKey("ru") && reloaded.Localization.Languages.ContainsKey("en"), "Russian and English string tables");
                Equal("Start", reloaded.Localization.Languages["en"]["Main.Start"], "English string");
                Equal("Execution state indicator", reloaded.Localization.Languages["en"]["Main.Column.ActivityHint"], "activity indicator string");
                Equal(0, reloaded.Localization.Languages["ru"].Keys.Except(reloaded.Localization.Languages["en"].Keys, StringComparer.OrdinalIgnoreCase).Count(), "every Russian key has an English value");
                Equal(0, reloaded.Localization.Languages["en"].Keys.Except(reloaded.Localization.Languages["ru"].Keys, StringComparer.OrdinalIgnoreCase).Count(), "every English key has a Russian value");
                Equal(11.5f, reloaded.Application.ConsoleFontSize, "console font size");
                Equal(210, reloaded.Application.ConsolePaneHeight, "console pane height");
                Equal("#A1B2C3", reloaded.Application.ConsoleForegroundColor, "console foreground color");
                Equal("#102030", reloaded.Application.ConsoleBackgroundColor, "console background color");
                Equal(82, reloaded.Application.ConsoleBackgroundOpacity, "console background opacity");
                Equal("#112233", reloaded.Application.ConsoleTabForegroundColor, "tab text color");
                Equal("#F1F2F3", reloaded.Application.ConsoleActiveTabForegroundColor, "active tab text color");
                Equal("#CCDDEE", reloaded.Application.ConsoleTabBackgroundColor, "tab background color");
                Equal(65, reloaded.Application.ConsoleTabBackgroundOpacity, "tab background opacity");
                Equal("#203040", reloaded.Application.ConsoleActiveTabBackgroundColor, "active tab background color");
                Equal(73, reloaded.Application.ConsoleActiveTabBackgroundOpacity, "active tab background opacity");
                Equal(true, reloaded.Application.MainWindowPlacementSaved, "main window placement marker");
                Equal(123, reloaded.Application.MainWindowX, "main window X position");
                Equal(234, reloaded.Application.MainWindowY, "main window Y position");
                Equal(1280, reloaded.Application.MainWindowWidth, "main window width");
                Equal(760, reloaded.Application.MainWindowHeight, "main window height");
                Equal(true, reloaded.Application.MainWindowMaximized, "main window maximized state");
                var savedText = File.ReadAllText(configPath, Encoding.UTF8);
                Assert(savedText.Contains("[Strings.ru]") && savedText.Contains("[Strings.en]"), "localization is stored in INI");
                Assert(savedText.Contains("MainWindowPlacementSaved=true") && savedText.Contains("WordWrap=true"),
                    "window placement and script word wrap are stored in INI");

                reloaded.Scripts[0].Name = "Second save";
                store.Save(reloaded);
                Assert(File.Exists(configPath + ".bak"), "atomic save backup exists");

                var conflicted = store.Reload();
                File.AppendAllText(configPath, Environment.NewLine + "; external change", Encoding.UTF8);
                Expect<ConfigurationChangedException>(() => store.Save(conflicted), "external change detection");

                var legacyPath = Path.Combine(directory, "Legacy.ini");
                File.WriteAllText(legacyPath, "[Application]\r\nConfigVersion=1\r\n", new UTF8Encoding(false));
                var legacy = new ConfigurationStore(legacyPath).LoadOrCreate();
                Equal(8, legacy.Application.ConfigVersion, "legacy configuration version is upgraded");
                Assert(File.ReadAllText(legacyPath, Encoding.UTF8).Contains("[Strings.ru]"), "legacy configuration receives localization strings");

                var version2Path = Path.Combine(directory, "Version2.ini");
                File.WriteAllText(version2Path,
                    "[Application]\r\nConfigVersion=2\r\n" +
                    "[Strings.en]\r\nScript.Encoding.Auto=Auto (Windows OEM)\r\n" +
                    "[Strings.ru]\r\nScript.Encoding.Auto=Авто (OEM Windows)\r\n",
                    new UTF8Encoding(false));
                var version2 = new ConfigurationStore(version2Path).LoadOrCreate();
                Equal(8, version2.Application.ConfigVersion, "version 2 configuration is upgraded");
                Equal("Auto (UTF-8/Windows-1251/OEM)", version2.Localization.Languages["en"]["Script.Encoding.Auto"],
                    "old default English Auto label is migrated");
                Equal("Авто (UTF-8/Windows-1251/OEM)", version2.Localization.Languages["ru"]["Script.Encoding.Auto"],
                    "old default Russian Auto label is migrated");

                var version5Path = Path.Combine(directory, "Version5.ini");
                File.WriteAllText(version5Path,
                    "[Application]\r\nConfigVersion=5\r\n" +
                    "[Localization]\r\nLanguage=en\r\n" +
                    "[Strings.en]\r\nSettings.Title=CmdsManager settings\r\n" +
                    "[Strings.ru]\r\nSettings.Title=Настройки CmdsManager\r\n",
                    new UTF8Encoding(false));
                var version5 = new ConfigurationStore(version5Path).LoadOrCreate();
                Equal(8, version5.Application.ConfigVersion, "version 5 configuration is upgraded");
                Equal("Cmds Manager settings", version5.Localization.Languages["en"]["Settings.Title"],
                    "old default English brand is migrated");
                Equal("Настройки Cmds Manager", version5.Localization.Languages["ru"]["Settings.Title"],
                    "old default Russian brand is migrated");

                var version6Path = Path.Combine(directory, "Version6.ini");
                File.WriteAllText(version6Path, "[Application]\r\nConfigVersion=6\r\n", new UTF8Encoding(false));
                var version6 = new ConfigurationStore(version6Path).LoadOrCreate();
                Equal(8, version6.Application.ConfigVersion, "version 6 configuration is upgraded");
                Equal(ApplicationTheme.System, version6.Application.Theme,
                    "existing installations default to the system application theme");
                Assert(File.ReadAllText(version6Path, Encoding.UTF8).Contains("Theme=System"),
                    "theme selection is persisted during version 6 migration");

                var version7Path = Path.Combine(directory, "Version7.ini");
                File.WriteAllText(version7Path,
                    "[Application]\r\nConfigVersion=7\r\n" +
                    "[Localization]\r\nLanguage=en\r\n" +
                    "[Strings.en]\r\nSettings.Warning=Auto-start is per-user...\r\n" +
                    "[Strings.ru]\r\nSettings.Warning=Автозапуск настраивается...\r\n" +
                    "[Script:69e1f16b-4daa-4334-92c0-95b0a3baee56]\r\n" +
                    "Name=Version 7 script\r\nPath=" + scriptPath + "\r\n",
                    new UTF8Encoding(false));
                var version7 = new ConfigurationStore(version7Path).LoadOrCreate();
                Equal(8, version7.Application.ConfigVersion, "version 7 configuration is upgraded");
                Equal(false, version7.Scripts[0].Launch.WordWrap,
                    "existing scripts receive the default disabled word-wrap setting");
                var version7Text = File.ReadAllText(version7Path, Encoding.UTF8);
                Assert(version7Text.Contains("MainWindowPlacementSaved=false") && version7Text.Contains("WordWrap=false"),
                    "version 7 migration writes window placement and word-wrap keys");
                Assert(!version7Text.Contains("Settings.Warning="),
                    "version 7 migration removes the obsolete auto-start warning string");
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

                var managerPath = Path.Combine(directory, "CmdsManager.exe");
                var childPath = Path.Combine(directory, "start-gplay-bridge.cmd");
                File.WriteAllBytes(managerPath, new byte[] { 0 });
                File.WriteAllText(childPath, "@exit /b 0\r\n", Encoding.ASCII);
                File.WriteAllText(scriptPath,
                    "@echo off\r\nset \"ROOT_DIR=%~dp0\"\r\n" +
                    "start \"RuStore GPlay Bridge\" /D \"%ROOT_DIR%\" cmd /k \"%ROOT_DIR%start-gplay-bridge.cmd\"\r\n",
                    Encoding.ASCII);
                var managedSpec = new ScriptCommandBuilder(directory, managerPath).Build(script, string.Empty);
                try
                {
                    Assert(!string.IsNullOrEmpty(managedSpec.TemporaryScriptPath) && File.Exists(managedSpec.TemporaryScriptPath),
                        "CMD script with START cmd /k receives a managed temporary copy");
                    var transformed = File.ReadAllText(managedSpec.TemporaryScriptPath, Encoding.ASCII);
                    Assert(transformed.Contains("CMDSMANAGER_START_LINE=") && transformed.Contains("--managed-start-env"),
                        "START is redirected to CmdsManager IPC through CMD-safe environment transport");
                    Assert(transformed.Contains(script.Id.ToString("D")),
                        "transformed START identifies its parent script for inherited console settings");
                    Assert(transformed.Contains("%ROOT_DIR%start-gplay-bridge.cmd"), "child path variables are preserved for parent CMD expansion");
                    Equal(managerPath, Environment.GetEnvironmentVariable("CMDSMANAGER_HOST_EXE"),
                        "manager executable is scoped to the CmdsManager process environment");

                    var request = ManagedStartRequestParser.Parse(directory, new[]
                    {
                        "RuStore GPlay Bridge", "/D", directory, "cmd", "/k", childPath
                    });
                    Equal("RuStore GPlay Bridge", request.Title, "START title becomes the console tab name");
                    Equal(childPath, request.ScriptPath, "START child script path");
                    Equal(directory, request.WorkingDirectory, "START /D working directory");
                    Equal(ScriptInterpreter.Cmd, request.ToScriptDefinition().Launch.Interpreter, "managed child uses CMD interpreter");
                }
                finally
                {
                    if (!string.IsNullOrEmpty(managedSpec.TemporaryScriptPath) && File.Exists(managedSpec.TemporaryScriptPath))
                        File.Delete(managedSpec.TemporaryScriptPath);
                }
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
                const string windowsPhrase = "Файл не найден.";
                var oem = Encoding.GetEncoding((int)GetOEMCP());
                Equal(windowsPhrase, OutputEncodingDecoder.Decode(Encoding.GetEncoding(1251).GetBytes(windowsPhrase),
                    ScriptOutputEncoding.Auto), "Auto detects Windows-1251 Cyrillic");
                Equal(windowsPhrase, OutputEncodingDecoder.Decode(oem.GetBytes(windowsPhrase),
                    ScriptOutputEncoding.Auto), "Auto detects OEM Cyrillic");
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

                    const string chineseJson = "{\"groupDesc\":\"应用信息\",\"permissionLabel\":\"获取设备信息\"}";
                    var writerPath = Path.Combine(directory, "write-utf8.ps1");
                    File.WriteAllText(writerPath,
                        "$text = '" + chineseJson + "' + [Environment]::NewLine\r\n" +
                        "$bytes = [Text.Encoding]::UTF8.GetBytes($text)\r\n" +
                        "$stream = [Console]::OpenStandardOutput()\r\n" +
                        "$stream.Write($bytes, 0, $bytes.Length)\r\n$stream.Flush()\r\n",
                        new UTF8Encoding(true));
                    var mixedPath = Path.Combine(directory, "mixed-output.cmd");
                    var windowsPowerShell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                    File.WriteAllText(mixedPath,
                        "@echo off\r\necho " + oemPhrase + "\r\n\"" + windowsPowerShell +
                        "\" -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + writerPath + "\"\r\n", oem);
                    var mixedLines = RunAndCapture(directory, new ScriptDefinition
                    {
                        Name = "Mixed OEM and UTF-8",
                        Path = mixedPath,
                        Launch = new LaunchProfile
                        {
                            Interpreter = ScriptInterpreter.Cmd,
                            OutputEncoding = ScriptOutputEncoding.Auto,
                            WorkingDirectory = directory,
                            CaptureOutput = true
                        }
                    });
                    Assert(mixedLines.Any(line => line.Contains(oemPhrase)), "Auto keeps OEM lines in a mixed stream");
                    Assert(mixedLines.Any(line => line.Contains("应用信息") && line.Contains("获取设备信息")),
                        "Auto detects UTF-8 Chinese JSON lines inside a CMD stream");

                    const string windowsJson = "[HTTP RESPONSE] {\"bodyPreview\":\"Файл не найден.\"}";
                    var windowsWriterPath = Path.Combine(directory, "write-windows1251.ps1");
                    File.WriteAllText(windowsWriterPath,
                        "$text = '" + windowsJson + "' + [Environment]::NewLine\r\n" +
                        "$bytes = [Text.Encoding]::GetEncoding(1251).GetBytes($text)\r\n" +
                        "$stream = [Console]::OpenStandardOutput()\r\n" +
                        "$stream.Write($bytes, 0, $bytes.Length)\r\n$stream.Flush()\r\n",
                        new UTF8Encoding(true));
                    var windowsLines = RunAndCapture(directory, new ScriptDefinition
                    {
                        Name = "Windows-1251 child output",
                        Path = windowsWriterPath,
                        Launch = new LaunchProfile
                        {
                            Interpreter = ScriptInterpreter.WindowsPowerShell,
                            OutputEncoding = ScriptOutputEncoding.Auto,
                            WorkingDirectory = directory,
                            CaptureOutput = true
                        }
                    });
                    Assert(windowsLines.Any(line => line.Contains(windowsJson)),
                        "Auto decodes Windows-1251 output from a child process");
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

                var utf16WriterPath = Path.Combine(directory, "write-utf16.ps1");
                File.WriteAllText(utf16WriterPath,
                    "$encoding = [Text.Encoding]::Unicode\r\n" +
                    "$text = 'Привет UTF-16' + [Environment]::NewLine\r\n" +
                    "$bytes = $encoding.GetPreamble() + $encoding.GetBytes($text)\r\n" +
                    "$stream = [Console]::OpenStandardOutput()\r\n" +
                    "$stream.Write($bytes, 0, $bytes.Length)\r\n$stream.Flush()\r\n",
                    new UTF8Encoding(true));
                var utf16Lines = RunAndCapture(directory, new ScriptDefinition
                {
                    Name = "UTF-16 output",
                    Path = utf16WriterPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.WindowsPowerShell,
                        OutputEncoding = ScriptOutputEncoding.Utf16LittleEndian,
                        WorkingDirectory = directory,
                        CaptureOutput = true
                    }
                });
                Assert(utf16Lines.Any(line => line.Contains("Привет UTF-16")),
                    "explicit UTF-16 LE decoding keeps line framing");
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
                configuration.Application.Theme = ApplicationTheme.Dark;
                var state = new ConfigurationState(configuration);
                var text = new LocalizationService(state);
                using (var about = new AboutForm(text, ApplicationTheme.Dark))
                using (var settings = new SettingsForm(configuration.Application, configuration.PowerShell7Path, configuration.Localization, text))
                using (var script = new ScriptEditorForm(null, configuration.Defaults, directory, text, ApplicationTheme.Dark))
                {
                    var aboutHandle = about.Handle;
                    var settingsHandle = settings.Handle;
                    var scriptHandle = script.Handle;
                    about.PerformLayout();
                    settings.PerformLayout();
                    script.PerformLayout();
                    Assert(about.ClientSize.Width <= 580 && about.ClientSize.Height <= 250,
                        "About box remains compact with a 128 px icon");
                    Assert(settings.ClientSize.Width <= 530 && settings.ClientSize.Height <= 340, "settings dialog is narrower and compact");
                    Assert(script.ClientSize.Width <= 570 && script.ClientSize.Height <= 475,
                        "aligned script editor remains compact");
                    Equal("About", about.Text, "English About title comes from INI strings");
                    Equal("Cmds Manager settings", settings.Text, "English settings title comes from INI strings");
                    Assert(settings.BackColor.GetBrightness() < 0.3f && script.BackColor.GetBrightness() < 0.3f,
                        "dark application theme reaches compact dialogs");
                    Assert(AllControls(settings).OfType<TabControl>().Single().GetType().Name == "FluentTabControl",
                        "settings uses the themed compact tab control");
                    var themeSelector = AllControls(settings).OfType<ComboBox>().Single(control =>
                        control.Items.Cast<object>().Select(Convert.ToString)
                            .SequenceEqual(new[] { "System", "Light", "Dark" }));
                    Equal("Dark", Convert.ToString(themeSelector.SelectedItem),
                        "settings exposes and selects system, light, and dark themes");
                    Assert(!AllControls(settings).OfType<ScrollableControl>().Any(control => control.AutoScroll),
                        "settings dialog has no AutoScroll container");
                    Assert(!configuration.Localization.Languages.Values.Any(values =>
                            values.ContainsKey("Settings.Warning")),
                        "obsolete per-user auto-start warning is removed from the language tables");
                    Assert(!AllControls(script).OfType<ScrollableControl>().Any(control => control.AutoScroll),
                        "script editor has no AutoScroll container");
                    var retention = AllControls(settings).OfType<NumericUpDown>()
                        .Single(control => control.Maximum == 3650);
                    Assert(retention.Width <= 80, "log retention field is sized for its value");
                    var opacityFields = AllControls(settings).OfType<NumericUpDown>()
                        .Where(control => control.Maximum == 100).ToArray();
                    Equal(3, opacityFields.Length, "appearance tab has three compact opacity fields");
                    Assert(opacityFields.All(control => control.Width <= 60),
                        "opacity fields are sized for percentage values");
                    var colorButtons = AllControls(settings).OfType<Button>()
                        .Where(control => control.Text.StartsWith("#", StringComparison.Ordinal)).ToArray();
                    Equal(6, colorButtons.Length, "appearance tab exposes all console and tab colors");
                    Assert(colorButtons.Select(control => AbsoluteLeft(control, settings)).Distinct().Count() == 1,
                        "appearance color controls share one left edge");
                    var settingsTextInputs = AllControls(settings)
                        .Where(control => control.GetType().Name == "FluentTextBox").ToArray();
                    Equal(4, settingsTextInputs.Length,
                        "settings exposes the console font, editor, editor arguments, and pwsh path fields");
                    Assert(settingsTextInputs.Select(control => AbsoluteLeft(control, settings)).Distinct().Count() == 1,
                        "settings text fields share one left edge, including console font and pwsh path");
                    Assert(AllControls(settings).OfType<ComboBox>().All(control => control.GetType().Name == "FluentComboBox") &&
                        AllControls(settings).OfType<CheckBox>().All(control => control.GetType().Name == "FluentCheckBox") &&
                        AllControls(settings).Count(control => control.GetType().Name == "FluentNumericUpDown") == 4 &&
                        AllControls(settings).OfType<Button>().All(control => control.GetType().Name == "FluentButton"),
                        "settings uses Fluent inputs, selectors, checkboxes, numeric fields, and buttons");
                    var settingsNativeTextEditors = AllControls(settings).OfType<TextBox>()
                        .Where(control => control.Parent.GetType().Name == "FluentTextBox").ToArray();
                    Assert(settingsTextInputs.All(control => control.Height >= 28) &&
                        settingsNativeTextEditors.Length == 4 &&
                        settingsNativeTextEditors.All(control => control.BorderStyle == BorderStyle.None),
                        "settings Fluent text fields use a taller custom border around the native editor");
                    Assert(AllControls(settings).Where(control => control.GetType().Name == "FluentNumericUpDown")
                            .All(control => control.Height >= 28) &&
                        AllControls(settings).OfType<NumericUpDown>().All(control =>
                            control.BorderStyle == BorderStyle.None && control.Parent.GetType().Name == "FluentNumericUpDown"),
                        "settings Fluent numeric fields use a taller custom border around the native editor");
                    var numeric = AllControls(script).OfType<NumericUpDown>().ToArray();
                    var fluentNumeric = AllControls(script)
                        .Where(control => control.GetType().Name == "FluentNumericUpDown").ToArray();
                    Equal(3, numeric.Length, "script editor has three compact numeric settings");
                    Equal(3, fluentNumeric.Length, "script editor wraps every numeric setting in Fluent chrome");
                    Assert(numeric.All(control => control.Width <= 65 && control.BorderStyle == BorderStyle.None) &&
                        fluentNumeric.All(control => control.Width <= 65 && control.Height >= 28) &&
                        fluentNumeric.Select(control => control.Parent).Distinct().Count() == 1,
                        "order, delay, and timeout fields share one compact row");
                    Assert(!AllControls(script).OfType<TabControl>().Any(), "launch settings are on the same page instead of a second tab");
                    var encodingLabel = AllControls(script).OfType<Label>().First(control => control.Text == text["Script.Encoding"]);
                    var editorTable = encodingLabel.Parent as TableLayoutPanel;
                    var encodingCombo = editorTable?.Controls.OfType<ComboBox>()
                        .FirstOrDefault(control => editorTable.GetRow(control) == editorTable.GetRow(encodingLabel));
                    var encodingLabelCenter = AbsoluteTop(encodingLabel, script) + encodingLabel.Height / 2;
                    var encodingComboCenter = encodingCombo == null ? -1 : AbsoluteTop(encodingCombo, script) + encodingCombo.Height / 2;
                    Assert(encodingCombo != null && Math.Abs(encodingComboCenter - encodingLabelCenter) <= 2,
                        "output encoding label is vertically centered beside its Fluent selector");

                    var alignedTextInputs = AllControls(script)
                        .Where(control => control.GetType().Name == "FluentTextBox").ToArray();
                    Equal(4, alignedTextInputs.Length, "script editor exposes four Fluent text fields");
                    Assert(alignedTextInputs.Select(control => AbsoluteLeft(control, script)).Distinct().Count() == 1,
                        "script text boxes share one left edge");
                    var alignedCombos = AllControls(script).OfType<ComboBox>().ToArray();
                    Equal(4, alignedCombos.Length, "script editor exposes four launch selectors");
                    Assert(alignedCombos.Select(control => AbsoluteLeft(control, script)).Distinct().Count() == 1,
                        "script drop-downs share one left edge");
                    var alignedChecks = AllControls(script).OfType<CheckBox>()
                        .Where(control => control.Text != text["Script.Enabled"]).ToArray();
                    Assert(alignedChecks.Select(control => AbsoluteLeft(control, script)).Distinct().Count() == 1,
                        "script option checkboxes share one left edge");
                    Assert(alignedChecks.Any(control => control.Text == text["Console.WordWrap"]),
                        "script editor exposes the persistent console word-wrap option");
                    Assert(alignedTextInputs.All(control => control.Height >= 28) &&
                        AllControls(script).OfType<TextBox>()
                            .Where(control => control.Parent.GetType().Name == "FluentTextBox")
                            .All(control => control.BorderStyle == BorderStyle.None) &&
                        alignedCombos.All(control => control.GetType().Name == "FluentComboBox") &&
                        AllControls(script).OfType<CheckBox>().All(control => control.GetType().Name == "FluentCheckBox") &&
                        AllControls(script).OfType<Button>().All(control => control.GetType().Name == "FluentButton"),
                        "script editor uses Fluent text fields, selectors, checkboxes, and buttons");
                    Equal(AbsoluteLeft(alignedCombos[0], script), AbsoluteLeft(fluentNumeric[0].Parent, script),
                        "compact numeric row starts at the common control edge");
                    Assert(fluentNumeric.All(control => AbsoluteLeft(control, script) + control.Width <= script.ClientSize.Width),
                        "compact Fluent numeric fields remain inside the dialog client area");
                    var scriptNote = AllControls(script).OfType<Label>().Single(control => control.Text == text["Script.Note"]);
                    var scriptFooter = AllControls(script).OfType<Button>()
                        .Single(control => control.Text == text["Common.Save"]).Parent;
                    Assert(AbsoluteTop(scriptNote, script) + scriptNote.Height <= AbsoluteTop(scriptFooter, script),
                        "script note and Fluent footer do not overlap");

                    var author = AllControls(about).OfType<LinkLabel>()
                        .Single(control => control.Text.StartsWith("Author: iMiKED from 4PDA", StringComparison.Ordinal));
                    Equal("https://github.com/iMiKED", Convert.ToString(author.Links[0].LinkData), "hard-coded author link");
                    var aboutIcon = AllControls(about).OfType<PictureBox>().Single();
                    Assert(aboutIcon.Image != null && aboutIcon.Image.Width == 128 && aboutIcon.Image.Height == 128,
                        "About uses the sharp 128 by 128 application icon frame");
                    var aboutTitle = AllControls(about).OfType<Label>().First(control => control.Text == "Cmds Manager");
                    var aboutVersion = AllControls(about).OfType<Label>().First(control => control.Text.StartsWith("Version ", StringComparison.Ordinal));
                    var aboutDescription = AllControls(about).OfType<Label>().First(control => control.Text == text["About.Description"]);
                    var aboutLefts = new Control[] { aboutTitle, aboutVersion, aboutDescription, author }
                        .Select(control => AbsoluteLeft(control, about)).ToArray();
                    Assert(aboutLefts.Distinct().Count() == 1, "About information lines share one left edge");
                    Assert(aboutVersion.Top > aboutTitle.Bottom, "version is directly below the application title");
                    var aboutGaps = new[]
                    {
                        aboutVersion.Top - aboutTitle.Bottom,
                        aboutDescription.Top - aboutVersion.Bottom,
                        author.Top - aboutDescription.Bottom
                    };
                    Assert(aboutGaps.Max() - aboutGaps.Min() <= 1, "About information rows have equal spacing");
                    Assert(AllControls(about).Any(control => control.GetType().Name.Contains("FadeGradientPanel")),
                        "About contains the fade-out gradient background");
                    var aboutClose = AllControls(about).OfType<Button>().Single();
                    Assert(aboutClose.GetType().Name == "FluentButton" && aboutClose.Height >= 28,
                        "About uses a compact Fluent close button");
                    var settingsSave = AllControls(settings).OfType<Button>()
                        .Single(control => control.Text == text["Common.Save"]);
                    var primaryProperty = aboutClose.GetType().GetProperty("Primary",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(settingsSave.GetType() == aboutClose.GetType() &&
                        settingsSave.Padding == aboutClose.Padding &&
                        settingsSave.MinimumSize == aboutClose.MinimumSize &&
                        settingsSave.FlatStyle == aboutClose.FlatStyle &&
                        settingsSave.FlatAppearance.BorderSize == aboutClose.FlatAppearance.BorderSize &&
                        settingsSave.Parent.Padding == aboutClose.Parent.Padding &&
                        primaryProperty != null && (bool)primaryProperty.GetValue(settingsSave) &&
                        (bool)primaryProperty.GetValue(aboutClose),
                        "About Close and Settings Save use the same primary Fluent button template");
                    Assert(typeof(MainForm).Assembly.GetManifestResourceNames().Contains("CmdsManager.Assets.CmdsManager.ico"),
                        "application icon is embedded in the executable");
                    Assert(!configuration.Localization.Languages.Values.Any(values => values.Values.Any(value =>
                            value.IndexOf("iMiKED", StringComparison.OrdinalIgnoreCase) >= 0)),
                        "author line is not configurable through INI");
                }
            });
        }

        private static void TestSingleInstanceCommandIpc()
        {
            var received = string.Empty;
            var arrived = new ManualResetEventSlim(false);
            var scope = "CmdsManager.Tests." + Guid.NewGuid().ToString("N");
            using (var primary = new SingleInstanceGuard(scope))
            {
                Assert(primary.IsPrimaryInstance, "first guard owns the instance mutex");
                primary.StartListening(() => { }, command =>
                {
                    received = command;
                    arrived.Set();
                });
                using (var secondary = new SingleInstanceGuard(scope))
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
                configuration.Application.ConsoleForegroundColor = "#A1B2C3";
                configuration.Application.ConsoleBackgroundColor = "#102030";
                configuration.Application.ConsoleBackgroundOpacity = 80;
                configuration.Application.ConsoleTabForegroundColor = "#223344";
                configuration.Application.ConsoleActiveTabForegroundColor = "#F1F2F3";
                configuration.Application.ConsoleTabBackgroundColor = "#CCDDEE";
                configuration.Application.ConsoleTabBackgroundOpacity = 25;
                configuration.Application.ConsoleActiveTabBackgroundColor = "#203040";
                configuration.Application.ConsoleActiveTabBackgroundOpacity = 72;
                var state = new ConfigurationState(configuration);
                var text = new LocalizationService(state);
                var initiallyWrappedScriptId = Guid.NewGuid();
                using (var console = new ConsoleTabsControl(text, () => state.Current.Application,
                    scriptId => scriptId == initiallyWrappedScriptId))
                {
                    console.CreateControl();
                    typeof(ConsoleTabsControl).GetMethod("ApplyApplicationTheme",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(console, new object[] { ApplicationTheme.Dark });
                    var scriptId = Guid.NewGuid();
                    const int processId = 4242;
                    console.EnqueueStarted(new ScriptInstanceEventArgs(scriptId, "Fast output", processId, DateTime.Now, true, null));
                    for (var index = 0; index < 20000; index++)
                        console.EnqueueOutput(new ScriptOutputEventArgs(scriptId, processId, "строка-" + index, false));

                    var elapsed = Stopwatch.StartNew();
                    RichTextBox output = null;
                    TerminalTabStrip tabs = null;
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        tabs = FindControl<TerminalTabStrip>(console);
                        output = AllControls(console).OfType<RichTextBox>()
                            .FirstOrDefault(control => processId.Equals(control.Tag));
                        if (output != null && output.Text.Contains("строка-19999")) break;
                        Thread.Sleep(10);
                    }

                    Assert(output != null && output.Text.Contains("строка-19999"), "20,000 lines reach the console in one bounded batch window");
                    Assert(!output.Text.Contains("[4242 OUT]"), "console text has no PID OUT prefix");
                    Assert(output.TextLength <= 200000, "console history is bounded");
                    Assert(Math.Abs(output.Font.SizeInPoints - 12f) < 0.1f, "configured console font size is applied");
                    Equal(Color.FromArgb(0xA1, 0xB2, 0xC3), output.ForeColor,
                        "configured console text color is applied");
                    Assert(tabs.IsTabRunning(0), "running console tab has a filled activity marker");
                    Assert(tabs.GetTabText(0).Contains(text["Console.Running"]),
                        "running console tab has localized status text");
                    Assert(tabs.Height >= 40 && tabs.Font.Name == "Segoe UI",
                        "console tabs use the taller Segoe UI terminal strip");
                    Assert(tabs.BackColor.GetBrightness() < 0.25f,
                        "dark application theme paints the complete console tab strip dark");
                    Assert(!AllControls(console).OfType<TabControl>().Any(),
                        "console host has no native TabControl button surface");
                    Assert(tabs.Parent == output.Parent.Parent && tabs.Bottom == output.Parent.Top,
                        "custom tab strip joins the content host without a border gap");
                    var tabBounds = tabs.GetTabBounds(0);
                    var closeBounds = tabs.GetCloseBounds(0);
                    Assert(tabBounds.Top <= 4,
                        "console tabs use only a small amount of space above their labels");
                    Assert(tabBounds.Contains(closeBounds) && tabBounds.Right - closeBounds.Right >= 8,
                        "close glyph remains inside the visible tab body");
                    Equal(64, tabs.InactiveTabColor.A, "inactive tab opacity is applied to the tab surface");
                    Equal(184, tabs.ActiveTabColor.A, "active tab opacity is applied to the active tab surface");
                    Equal(Color.FromArgb(0x22, 0x33, 0x44), tabs.InactiveTextColor,
                        "inactive tab text color is configurable");
                    Equal(Color.FromArgb(0xF1, 0xF2, 0xF3), tabs.ActiveTextColor,
                        "active tab text color is configurable");
                    var tabPathFactory = typeof(TerminalTabStrip).GetMethod("CreateTabPath",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    Assert(tabPathFactory != null, "terminal tab shape renderer exists");
                    using (var tabPath = (System.Drawing.Drawing2D.GraphicsPath)tabPathFactory.Invoke(null,
                        new object[] { new Rectangle(0, 8, 180, 35) }))
                    {
                        Assert(tabPath.PathTypes.Any(type => (type & 3) ==
                            (byte)System.Drawing.Drawing2D.PathPointType.Bezier3),
                            "terminal tab silhouette uses curved corners instead of a rectangle");
                        Assert(tabPath.PathPoints.Any(point => Math.Abs(point.X) < 0.1f &&
                            Math.Abs(point.Y - 43f) < 0.1f),
                            "terminal tab silhouette has a flared lower shoulder");
                    }
                    using (var standaloneTabs = new TerminalTabStrip { Size = new Size(600, 44) })
                    {
                        standaloneTabs.CreateControl();
                        standaloneTabs.AddTab(7001, "Close test", "Close test", true);
                        var closedKey = -1;
                        standaloneTabs.CloseRequested += (sender, args) => closedKey = args.Key;
                        var standaloneClose = standaloneTabs.GetCloseBounds(0);
                        var mouseDown = typeof(TerminalTabStrip).GetMethod("OnMouseDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        mouseDown.Invoke(standaloneTabs, new object[]
                        {
                            new MouseEventArgs(MouseButtons.Left, 1,
                                standaloneClose.Left + standaloneClose.Width / 2,
                                standaloneClose.Top + standaloneClose.Height / 2, 0)
                        });
                        Equal(7001, closedKey, "close glyph hit-test belongs to its tab");
                    }

                    var menu = tabs.ContextMenuStrip;
                    Assert(menu != null && output.ContextMenuStrip == menu, "console tabs and output share the active-tab menu");
                    var menuTexts = menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
                    Assert(menuTexts.Contains(text["Console.CopySelection"]), "console menu can copy selected text");
                    Assert(menuTexts.Contains(text["Console.SaveSelection"]), "console menu can save selected text");
                    Assert(menuTexts.Contains(text["Console.SaveAll"]), "console menu can save all text");
                    Assert(menuTexts.Contains(text["Console.SelectFont"]), "console menu can choose an active-tab font");
                    Assert(menuTexts.Contains(text["Console.Detach"]), "console menu can detach the active tab");
                    Assert(menuTexts.Contains(text["Console.FullScreen"]), "console menu can show the active tab full screen");
                    Assert(menuTexts.Contains(text["Console.MaximizePane"]), "console menu can maximize the console area");

                    var windowsScriptId = initiallyWrappedScriptId;
                    const int windowsProcessId = 4343;
                    const string windowsPhrase = "Файл не найден.";
                    var windowsBytes = Encoding.GetEncoding(1251).GetBytes(windowsPhrase);
                    console.EnqueueStarted(new ScriptInstanceEventArgs(windowsScriptId, "Windows-1251 output",
                        windowsProcessId, DateTime.Now, true, null, ScriptOutputEncoding.Oem));
                    console.EnqueueOutput(new ScriptOutputEventArgs(windowsScriptId, windowsProcessId,
                        OutputEncodingDecoder.Decode(windowsBytes, ScriptOutputEncoding.Oem), false, windowsBytes));
                    RichTextBox windowsOutput = null;
                    Assert(WaitWithUi(() =>
                    {
                        windowsOutput = AllControls(console).OfType<RichTextBox>()
                            .FirstOrDefault(control => windowsProcessId.Equals(control.Tag));
                        return tabs.TabCount == 2 && tabs.SelectedKey == windowsProcessId &&
                            windowsOutput != null && windowsOutput.TextLength > 0;
                    }, TimeSpan.FromSeconds(2)),
                        "raw output appears in a second console tab");
                    Assert(!windowsOutput.Text.Contains(windowsPhrase), "forced OEM initially shows Windows-1251 bytes incorrectly");
                    var encodingMenu = menu.Items.OfType<ToolStripMenuItem>()
                        .Single(item => item.Text == text["Console.Encoding"]);
                    encodingMenu.DropDownItems.OfType<ToolStripMenuItem>()
                        .Single(item => item.Text == text["Script.Encoding.Windows1251"]).PerformClick();
                    Equal(windowsPhrase, windowsOutput.Text.TrimEnd('\r', '\n'),
                        "changing active-tab encoding re-decodes existing raw history");
                    Assert(windowsOutput.WordWrap && windowsOutput.ScrollBars == RichTextBoxScrollBars.Vertical,
                        "new console tabs apply their script's saved word-wrap setting");
                    ConsoleWordWrapChangedEventArgs wrapChanged = null;
                    console.WordWrapChanged += (sender, args) => wrapChanged = args;
                    var wrapItem = menu.Items.OfType<ToolStripMenuItem>()
                        .Single(item => item.Text == text["Console.WordWrap"]);
                    wrapItem.PerformClick();
                    Assert(!windowsOutput.WordWrap && windowsOutput.ScrollBars == RichTextBoxScrollBars.Both,
                        "active console tab can disable word wrap");
                    Assert(wrapChanged != null && wrapChanged.ScriptId == windowsScriptId && !wrapChanged.WordWrap,
                        "word-wrap changes identify the script whose setting must be persisted");

                    Assert(console.DetachSelectedTab(), "selected console tab detaches without restarting its process");
                    Assert(WaitWithUi(() => console.DetachedTabCount == 1 && windowsOutput.FindForm() != null,
                        TimeSpan.FromSeconds(2)), "detached tab is hosted by a separate form");
                    var detached = windowsOutput.FindForm();
                    Assert(detached != null && detached.GetType().Name == "DetachedConsoleForm",
                        "detached console uses the dedicated window host");
                    console.EnqueueOutput(new ScriptOutputEventArgs(windowsScriptId, windowsProcessId,
                        "detached-output", false));
                    Assert(WaitWithUi(() => windowsOutput.Text.Contains("detached-output"), TimeSpan.FromSeconds(2)),
                        "the same output view keeps receiving text while detached");
                    var setFullScreen = detached.GetType().GetMethod("SetFullScreen",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(setFullScreen != null, "detached console exposes a full-screen transition");
                    setFullScreen.Invoke(detached, new object[] { true });
                    Equal(FormBorderStyle.None, detached.FormBorderStyle,
                        "detached console enters borderless full screen without process restart");
                    setFullScreen.Invoke(detached, new object[] { false });
                    detached.Close();
                    Assert(WaitWithUi(() => console.DetachedTabCount == 0 && windowsOutput.FindForm() == null,
                        TimeSpan.FromSeconds(2)), "closing the detached window attaches the same view back as a tab");

                    tabs.SelectTab(windowsProcessId);
                    Assert(console.ToggleSelectedTabFullScreen(), "active embedded tab can open directly full screen");
                    Assert(WaitWithUi(() => console.DetachedTabCount == 1 && windowsOutput.FindForm() != null,
                        TimeSpan.FromSeconds(2)), "full-screen tab is moved to a separate host");
                    detached = windowsOutput.FindForm();
                    Equal(FormBorderStyle.None, detached.FormBorderStyle, "direct full-screen mode is borderless");
                    setFullScreen = detached.GetType().GetMethod("SetFullScreen",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    setFullScreen.Invoke(detached, new object[] { false });
                    Assert(WaitWithUi(() => console.DetachedTabCount == 0 && windowsOutput.FindForm() == null,
                        TimeSpan.FromSeconds(2)), "leaving direct full-screen mode restores the embedded tab");

                    console.EnqueueExited(new ScriptInstanceEventArgs(scriptId, "Fast output", processId, DateTime.Now, true, 0));
                    elapsed.Restart();
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(2) &&
                        tabs.IsTabRunning(0))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    Assert(!tabs.IsTabRunning(0), "exited console tab has an inactive marker");
                    Assert(tabs.GetTabText(0).Contains(text.Get("Console.Exited", 0)),
                        "exited console tab has the exit code");
                }
            });
        }

        private static void TestRunningVisualIndicator()
        {
            WithTemporaryDirectory(directory =>
            {
                var scriptPath = Path.Combine(directory, "indicator.ps1");
                File.WriteAllText(scriptPath, "Start-Sleep -Seconds 20\r\n", Encoding.ASCII);
                var store = new ConfigurationStore(Path.Combine(directory, "CmdsManager.ini"));
                var configuration = store.LoadOrCreate();
                configuration.Localization.Language = "en";
                configuration.Application.Theme = ApplicationTheme.Light;
                configuration.Application.ConsolePaneHeight = 180;
                var workingArea = Screen.PrimaryScreen.WorkingArea;
                var initialBounds = new Rectangle(
                    workingArea.Left + 24,
                    workingArea.Top + 24,
                    Math.Max(880, Math.Min(1000, workingArea.Width - 72)),
                    Math.Max(520, Math.Min(650, workingArea.Height - 72)));
                configuration.Application.MainWindowPlacementSaved = true;
                configuration.Application.MainWindowX = initialBounds.X;
                configuration.Application.MainWindowY = initialBounds.Y;
                configuration.Application.MainWindowWidth = initialBounds.Width;
                configuration.Application.MainWindowHeight = initialBounds.Height;
                configuration.Application.MainWindowMaximized = false;
                var script = new ScriptDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Indicator",
                    Path = scriptPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.WindowsPowerShell,
                        CaptureOutput = false,
                        WindowMode = ScriptWindowMode.Hidden,
                        StopPolicy = ScriptStopPolicy.Kill,
                        StopTimeoutSeconds = 0
                    }
                };
                configuration.Scripts.Add(script);
                configuration.Scripts.Add(new ScriptDefinition
                {
                    Id = Guid.NewGuid(), Name = "Second row", Path = scriptPath,
                    Launch = script.Launch.Clone()
                });
                configuration.Scripts.Add(new ScriptDefinition
                {
                    Id = Guid.NewGuid(), Name = "Third row", Path = scriptPath,
                    Launch = script.Launch.Clone()
                });
                var state = new ConfigurationState(configuration);
                var text = new LocalizationService(state);
                var commandBuilder = new ScriptCommandBuilder(directory);

                using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                using (var supervisor = new ProcessSupervisor(commandBuilder, logger, () => false))
                using (var form = new MainForm(state, store, supervisor,
                    new WindowsScriptEditorLauncher(commandBuilder), new NoOpStartupRegistration(), logger, text))
                {
                    var formHandle = form.Handle;
                    Assert(formHandle != IntPtr.Zero, "main form handle is created for queued UI updates");
                    Equal("Cmds Manager 0.6.6", form.Text,
                        "main window title contains the spaced product name and version");
                    var grid = FindControl<DataGridView>(form);
                    Assert(grid != null && grid.Columns.Contains("Activity"), "main grid has an activity indicator column");
                    var toolbar = AllControls(form).OfType<ToolStrip>().Single();
                    var toolbarButtons = toolbar.Items.OfType<ToolStripButton>().ToArray();
                    var expectedToolbarButtons = new[]
                    {
                        text["Main.Add"], text["Main.Edit"], text["Main.Delete"], text["Main.Start"],
                        text["Main.Stop"], text["Main.StartAll"], text["Main.StopAll"], text["Main.Reload"],
                        text["Main.Settings"], text["Main.About"], text["Main.Exit"]
                    };
                    Assert(toolbarButtons.Select(button => button.Text).SequenceEqual(expectedToolbarButtons),
                        "Fluent toolbar preserves every existing command and its order");
                    Assert(toolbarButtons.All(button => button.Image != null) && toolbar.Height == 42 &&
                        toolbar.Renderer.GetType().Name == "FluentToolStripRenderer",
                        "toolbar uses compact icon buttons and the Fluent renderer");
                    var renderButton = toolbar.Renderer.GetType().GetMethod("OnRenderButtonBackground",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(renderButton != null, "Fluent toolbar exposes its button background renderer");
                    using (var normalPreview = new Bitmap(Math.Max(1, toolbarButtons[0].Width),
                        Math.Max(1, toolbarButtons[0].Height)))
                    using (var primaryPreview = new Bitmap(Math.Max(1, toolbarButtons[3].Width), Math.Max(1, toolbarButtons[3].Height)))
                    {
                        var markerColor = Color.Magenta;
                        using (var graphics = Graphics.FromImage(normalPreview))
                        {
                            graphics.Clear(markerColor);
                            renderButton.Invoke(toolbar.Renderer,
                                new object[] { new ToolStripItemRenderEventArgs(graphics, toolbarButtons[0]) });
                        }
                        using (var graphics = Graphics.FromImage(primaryPreview))
                        {
                            graphics.Clear(markerColor);
                            renderButton.Invoke(toolbar.Renderer,
                                new object[] { new ToolStripItemRenderEventArgs(graphics, toolbarButtons[3]) });
                        }
                        Equal(markerColor.ToArgb(), normalPreview.GetPixel(normalPreview.Width / 2,
                            normalPreview.Height / 2).ToArgb(),
                            "normal toolbar buttons have no persistent surface or border at rest");
                        Assert(primaryPreview.GetPixel(primaryPreview.Width / 2, primaryPreview.Height / 2).ToArgb() !=
                            markerColor.ToArgb(), "Start keeps its persistent rounded accent surface");
                    }
                    var themeManager = typeof(MainForm).Assembly.GetType("CmdsManager.Presentation.Theming.AppThemeManager");
                    Assert(themeManager.GetMethod("ApplyWindowCorners",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) != null,
                        "main window requests native DWM rounded corners when supported");
                    var expectedColumns = new[]
                    {
                        "Activity", "Name", "Type", "Interpreter", "AutoStart", "State", "Pid", "Started", "ExitCode", "Path"
                    };
                    Assert(grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Name).SequenceEqual(expectedColumns),
                        "Fluent table preserves every existing column and its order");
                    Assert(grid.CellBorderStyle == DataGridViewCellBorderStyle.SingleHorizontal &&
                        grid.RowTemplate.Height == 38 && grid.ColumnHeadersHeight == 34 && !grid.EnableHeadersVisualStyles,
                        "table uses compact Fluent rows, quiet horizontal separators, and custom headers");
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Assert(Math.Abs(form.Left - initialBounds.Left) <= 2 &&
                        Math.Abs(form.Top - initialBounds.Top) <= 2 &&
                        Math.Abs(form.Width - initialBounds.Width) <= 2 &&
                        Math.Abs(form.Height - initialBounds.Height) <= 2,
                        "main window position and size are restored from INI");
                    var movedBounds = new Rectangle(initialBounds.X + 18, initialBounds.Y + 16,
                        initialBounds.Width + 12, initialBounds.Height + 10);
                    form.Bounds = movedBounds;
                    typeof(MainForm).GetMethod("HandleWindowResizeEnd",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(form, new object[] { form, EventArgs.Empty });
                    Assert(WaitWithUi(() =>
                    {
                        var saved = store.Reload().Application;
                        return saved.MainWindowPlacementSaved && saved.MainWindowX == movedBounds.X &&
                            saved.MainWindowY == movedBounds.Y && saved.MainWindowWidth == movedBounds.Width &&
                            saved.MainWindowHeight == movedBounds.Height && !saved.MainWindowMaximized;
                    }, TimeSpan.FromSeconds(2)),
                        "moved and resized main window placement is persisted to INI");
                    form.WindowState = FormWindowState.Maximized;
                    System.Windows.Forms.Application.DoEvents();
                    typeof(MainForm).GetMethod("HandleWindowResizeEnd",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(form, new object[] { form, EventArgs.Empty });
                    Assert(WaitWithUi(() => store.Reload().Application.MainWindowMaximized,
                        TimeSpan.FromSeconds(2)),
                        "maximized main window state is persisted to INI");
                    form.WindowState = FormWindowState.Normal;
                    System.Windows.Forms.Application.DoEvents();
                    typeof(MainForm).GetMethod("HandleWindowResizeEnd",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(form, new object[] { form, EventArgs.Empty });
                    Assert(WaitWithUi(() => !store.Reload().Application.MainWindowMaximized,
                        TimeSpan.FromSeconds(2)) && Math.Abs(form.Left - movedBounds.Left) <= 2 &&
                        Math.Abs(form.Top - movedBounds.Top) <= 2,
                        "restoring the main window preserves its normal bounds and state");
                    var split = FindControl<SplitContainer>(form);
                    Assert(split != null && split.FixedPanel == FixedPanel.Panel2,
                        "console pane keeps its configured height when the main window is resized");
                    Assert(Math.Abs(split.Panel2.Height - 180) <= 2,
                        "configured console pane height is restored from INI");
                    Assert(split.Panel1MinSize >= grid.ColumnHeadersHeight + grid.RowTemplate.Height * 3,
                        "normal splitter expansion stops at the last script row");
                    var maximizePane = typeof(MainForm).GetMethod("ToggleConsolePaneMaximized",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    maximizePane.Invoke(form, null);
                    System.Windows.Forms.Application.DoEvents();
                    Assert(split.Panel1MinSize <= grid.ColumnHeadersHeight + grid.RowTemplate.Height + 5 &&
                        split.Panel1.Height <= split.Panel1MinSize + 1 && grid.DisplayedRowCount(false) <= 1 &&
                        grid.Controls.OfType<VScrollBar>().Any(control => control.Visible),
                        "maximized console area leaves one visible script row and the grid scrolls");
                    form.Height += 80;
                    System.Windows.Forms.Application.DoEvents();
                    Assert(split.Panel1.Height <= split.Panel1MinSize + 1,
                        "maximized console area keeps one row while the main window is resized");
                    maximizePane.Invoke(form, null);
                    System.Windows.Forms.Application.DoEvents();
                    Assert(Math.Abs(split.Panel2.Height - 180) <= 2,
                        "restoring the console area returns to its remembered height");
                    split.SplitterDistance = split.Height - split.SplitterWidth - 205;
                    var splitterMoved = typeof(MainForm).GetMethod("HandleSplitterMoved",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    splitterMoved.Invoke(form, new object[]
                    {
                        split, new SplitterEventArgs(0, split.SplitterDistance, 0, split.SplitterDistance)
                    });
                    Assert(WaitWithUi(() => configuration.Application.ConsolePaneHeight == 205,
                        TimeSpan.FromSeconds(1)), "dragged console pane height updates the live INI model");
                    Assert(WaitWithUi(() =>
                    {
                        try { return store.Reload().Application.ConsolePaneHeight == 205; }
                        catch (ConfigurationChangedException) { return false; }
                    }, TimeSpan.FromSeconds(2)), "dragged console pane height is persisted to INI");

                    supervisor.Start(script, string.Empty);
                    var elapsed = Stopwatch.StartNew();
                    DataGridViewRow row = null;
                    var runningVisible = false;
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => script.Id.Equals(item.Tag));
                        if (row != null && string.Equals(Convert.ToString(row.Cells["State"].Value),
                            text["Main.State.Running"], StringComparison.Ordinal))
                        {
                            runningVisible = true;
                            break;
                        }
                        Thread.Sleep(10);
                    }

                    Assert(runningVisible, "main grid receives the running state");
                    row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => script.Id.Equals(item.Tag));
                    Assert(row != null, "running script remains visible in the main grid");
                    var marker = Convert.ToString(row.Cells["Activity"].Value);
                    var runtime = supervisor.GetSnapshot(script.Id);
                    var cells = string.Join(", ", grid.Columns.Cast<DataGridViewColumn>()
                        .Select(column => column.Name + "='" + Convert.ToString(row.Cells[column.Name].Value) + "'"));
                    Assert(marker == "●", "running script has a static filled activity marker; actual marker is '" +
                        marker + "', runtime is " + runtime.State + ", cells: " + cells);
                    Equal(Color.FromArgb(234, 248, 239), row.DefaultCellStyle.BackColor, "running row has a pale green background");
                    Assert(row.Cells["Activity"].Style.ForeColor.G > row.Cells["Activity"].Style.ForeColor.R,
                        "running activity marker is green");
                    var stableUntil = Stopwatch.StartNew();
                    while (stableUntil.Elapsed < TimeSpan.FromMilliseconds(1100))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    row = grid.Rows.Cast<DataGridViewRow>().First(item => script.Id.Equals(item.Tag));
                    Equal("●", Convert.ToString(row.Cells["Activity"].Value), "running marker does not blink");

                    supervisor.StopAsync(script.Id).GetAwaiter().GetResult();
                    elapsed.Restart();
                    while (elapsed.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        System.Windows.Forms.Application.DoEvents();
                        row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => script.Id.Equals(item.Tag));
                        if (row == null) continue;
                        marker = Convert.ToString(row.Cells["Activity"].Value);
                        if (marker == "○") break;
                        Thread.Sleep(10);
                    }
                    Equal("○", marker, "stopped script has an inactive marker");
                }
            });
        }

        private static void TestManagedChildCmdTabs()
        {
            WithTemporaryDirectory(directory =>
            {
                var parentPath = Path.Combine(directory, "parent.cmd");
                var childPath = Path.Combine(directory, "start-gplay-bridge.cmd");
                File.WriteAllText(parentPath, "@echo off\r\necho parent-output\r\nping.exe -n 20 127.0.0.1 >nul\r\n", Encoding.ASCII);
                File.WriteAllText(childPath, "@echo off\r\necho managed-child-output\r\nping.exe -n 20 127.0.0.1 >nul\r\n", Encoding.ASCII);
                var store = new ConfigurationStore(Path.Combine(directory, "CmdsManager.ini"));
                var configuration = store.LoadOrCreate();
                configuration.Localization.Language = "en";
                configuration.Application.Theme = ApplicationTheme.Dark;
                configuration.Application.ConsoleBackgroundColor = "#102030";
                var parent = new ScriptDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Parent script",
                    Path = parentPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        WorkingDirectory = directory,
                        CaptureOutput = true,
                        WordWrap = true,
                        StopPolicy = ScriptStopPolicy.Kill,
                        StopTimeoutSeconds = 0
                    }
                };
                configuration.Scripts.Add(parent);
                var state = new ConfigurationState(configuration);
                var text = new LocalizationService(state);
                var commandBuilder = new ScriptCommandBuilder(directory);

                using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                using (var supervisor = new ProcessSupervisor(commandBuilder, logger, () => false))
                using (var form = new MainForm(state, store, supervisor,
                    new WindowsScriptEditorLauncher(commandBuilder), new NoOpStartupRegistration(), logger, text))
                {
                    var formHandle = form.Handle;
                    var console = FindControl<ConsoleTabsControl>(form);
                    var tabs = FindControl<TerminalTabStrip>(console);
                    var grid = FindControl<DataGridView>(form);
                    supervisor.Start(parent, string.Empty);
                    Assert(WaitWithUi(() => tabs.TabCount == 1, TimeSpan.FromSeconds(4)), "parent console tab appears");

                    form.RunManagedChild(parent.Id, directory, new[]
                    {
                        "RuStore GPlay Bridge", "/D", directory, "cmd", "/k", childPath
                    });
                    Assert(WaitWithUi(() => tabs.TabCount == 2 &&
                        Enumerable.Range(0, tabs.TabCount).Any(index =>
                            tabs.GetTabText(index).Contains("RuStore GPlay Bridge")), TimeSpan.FromSeconds(4)),
                        "START cmd /k child appears in a neighboring tab");
                    var childIndex = Enumerable.Range(0, tabs.TabCount).First(index =>
                        tabs.GetTabText(index).Contains("RuStore GPlay Bridge"));
                    var childProcessId = tabs.GetTabKey(childIndex);
                    var childOutput = AllControls(console).OfType<RichTextBox>()
                        .Single(output => childProcessId.Equals(output.Tag));
                    Assert(WaitWithUi(() => childOutput.Text.Contains("managed-child-output"),
                        TimeSpan.FromSeconds(4)), "managed child output is captured in its own tab");
                    var parentIndex = Enumerable.Range(0, tabs.TabCount).First(index =>
                        tabs.GetTabText(index).Contains("Parent script"));
                    var parentProcessId = tabs.GetTabKey(parentIndex);
                    var parentOutput = AllControls(console).OfType<RichTextBox>()
                        .Single(output => parentProcessId.Equals(output.Tag));
                    Assert(parentOutput.WordWrap && childOutput.WordWrap &&
                        parentOutput.ScrollBars == RichTextBoxScrollBars.Vertical &&
                        childOutput.ScrollBars == RichTextBoxScrollBars.Vertical,
                        "managed child console inherits its parent script's saved word-wrap setting");
                    Equal(Color.FromArgb(0x10, 0x20, 0x30), childOutput.BackColor,
                        "dark application theme does not overwrite the configured console background");
                    Assert(grid.BackgroundColor.GetBrightness() < 0.25f,
                        "dark application theme reaches the main script table");
                    Assert(!AllControls(console).OfType<TabControl>().Any(),
                        "managed child tabs use the custom borderless terminal host");

                    grid.ClearSelection();
                    var parentRow = grid.Rows.Cast<DataGridViewRow>().First(row => parent.Id.Equals(row.Tag));
                    parentRow.Selected = true;
                    grid.CurrentCell = parentRow.Cells["Name"];
                    System.Windows.Forms.Application.DoEvents();
                    Assert(tabs.GetTabText(tabs.SelectedIndex).Contains("Parent script"),
                        "selecting a grid row selects its console tab");

                    tabs.SelectTab(childProcessId);
                    var wrapItem = tabs.ContextMenuStrip.Items.OfType<ToolStripMenuItem>()
                        .Single(item => item.Text == text["Console.WordWrap"]);
                    wrapItem.PerformClick();
                    Assert(!parentOutput.WordWrap && !childOutput.WordWrap,
                        "changing Word Wrap in a child tab updates the whole active script family");
                    Equal(false, store.Reload().Scripts.Single(item => item.Id == parent.Id).Launch.WordWrap,
                        "changing Word Wrap in a generated child tab persists the parent script setting to INI");

                    ConsoleTabCloseRequestedEventArgs closeRequest = null;
                    console.CloseRequested += (sender, args) => closeRequest = args;
                    tabs.SelectTab(childProcessId);
                    var closeItem = tabs.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().Last();
                    closeItem.PerformClick();
                    Assert(closeRequest != null && closeRequest.IsRunning, "closing a running tab requests process stop");
                    Assert(WaitWithUi(() => !supervisor.IsRunning(closeRequest.ScriptId), TimeSpan.FromSeconds(5)),
                        "closing the child tab stops its exact managed process");
                    Assert(!Enumerable.Range(0, tabs.TabCount).Any(index => tabs.GetTabKey(index) == childProcessId),
                        "closed child tab is removed");

                    supervisor.StopAsync(parent.Id).GetAwaiter().GetResult();
                    Assert(SpinWait.SpinUntil(() => !supervisor.IsRunning(parent.Id), TimeSpan.FromSeconds(5)), "parent script stops after tab test");
                }
            });
        }

        private static void TestTransformedStartCommandIpc()
        {
            WithTemporaryDirectory(directory =>
            {
                var parentPath = Path.Combine(directory, "start-all.cmd");
                var childPath = Path.Combine(directory, "start-gplay-bridge.cmd");
                File.WriteAllText(childPath, "@exit /b 0\r\n", Encoding.ASCII);
                File.WriteAllText(parentPath,
                    "@echo off\r\nset \"ROOT_DIR=%~dp0\"\r\n" +
                    "start \"RuStore GPlay Bridge\" /D \"%ROOT_DIR%\" cmd /k \"%ROOT_DIR%start-gplay-bridge.cmd\"\r\n",
                    Encoding.ASCII);
                var parent = new ScriptDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "START IPC parent",
                    Path = parentPath,
                    Launch = new LaunchProfile
                    {
                        Interpreter = ScriptInterpreter.Cmd,
                        WorkingDirectory = directory,
                        CaptureOutput = true
                    }
                };
                var commandReceived = new ManualResetEventSlim(false);
                var parentExited = new ManualResetEventSlim(false);
                var command = string.Empty;
                var previousScope = Environment.GetEnvironmentVariable("CMDSMANAGER_INSTANCE_SCOPE");
                var testScope = "CmdsManager.Tests." + Guid.NewGuid().ToString("N");
                try
                {
                    Environment.SetEnvironmentVariable("CMDSMANAGER_INSTANCE_SCOPE", testScope,
                        EnvironmentVariableTarget.Process);
                    using (var primary = new SingleInstanceGuard(testScope))
                    {
                        Assert(primary.IsPrimaryInstance, "test process owns an isolated CmdsManager instance guard");
                        primary.StartListening(() => { }, value =>
                        {
                            command = value;
                            commandReceived.Set();
                        });
                        using (var logger = new SimpleFileLogger(Path.Combine(directory, "logs"), 1))
                        using (var supervisor = new ProcessSupervisor(
                            new ScriptCommandBuilder(directory, typeof(MainForm).Assembly.Location), logger, () => false))
                        {
                            supervisor.StateChanged += (sender, args) =>
                            {
                                if (args.Snapshot.ScriptId == parent.Id && args.Snapshot.State == ScriptRuntimeState.Exited)
                                    parentExited.Set();
                            };
                            supervisor.Start(parent, string.Empty);
                            Assert(commandReceived.Wait(TimeSpan.FromSeconds(8)), "transformed START invokes the primary command pipe");
                            Assert(parentExited.Wait(TimeSpan.FromSeconds(8)), "parent script waits for the IPC helper and exits");
                        }
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("CMDSMANAGER_INSTANCE_SCOPE", previousScope,
                        EnvironmentVariableTarget.Process);
                }

                Assert(command.StartsWith("START ", StringComparison.Ordinal), "managed START uses a dedicated IPC command");
                var values = Encoding.UTF8.GetString(Convert.FromBase64String(command.Substring(6))).Split('\0');
                var parentScriptId = Guid.Empty;
                Assert(values.Length >= 3 && Guid.TryParse(values[0], out parentScriptId),
                    "managed START IPC includes its parent script identifier");
                Equal(parent.Id, parentScriptId,
                    "managed START IPC preserves the parent identifier for inherited console settings");
                ManagedStartRequest request;
                try { request = ManagedStartRequestParser.Parse(values[1], values.Skip(2)); }
                catch (Exception exception)
                {
                    throw new InvalidOperationException("Managed START IPC arguments: " +
                        string.Join(" | ", values.Select(value => "[" + value + "]")), exception);
                }
                Equal("RuStore GPlay Bridge", request.Title, "IPC preserves the START window title");
                Equal(childPath, request.ScriptPath, "IPC preserves the expanded child path");
                Assert(!Directory.GetFiles(directory, ".start-all.cmdsmanager-*.cmd").Any(),
                    "temporary transformed parent script is removed after execution");
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

        private static T FindControl<T>(Control root) where T : Control
        {
            var match = root as T;
            if (match != null) return match;
            foreach (Control child in root.Controls)
            {
                match = FindControl<T>(child);
                if (match != null) return match;
            }
            return null;
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (var descendant in AllControls(child)) yield return descendant;
        }

        private static int AbsoluteLeft(Control control, Control ancestor)
        {
            var result = 0;
            while (control != null && control != ancestor)
            {
                result += control.Left;
                control = control.Parent;
            }
            return result;
        }

        private static int AbsoluteTop(Control control, Control ancestor)
        {
            var result = 0;
            while (control != null && control != ancestor)
            {
                result += control.Top;
                control = control.Parent;
            }
            return result;
        }

        private static bool WaitWithUi(Func<bool> condition, TimeSpan timeout)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < timeout)
            {
                System.Windows.Forms.Application.DoEvents();
                if (condition()) return true;
                Thread.Sleep(10);
            }
            System.Windows.Forms.Application.DoEvents();
            return condition();
        }

        private sealed class NoOpStartupRegistration : IApplicationStartupRegistration
        {
            public string RegisteredCommand => string.Empty;
            public void Synchronize(bool enabled) { }
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
