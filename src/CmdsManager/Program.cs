using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Infrastructure.Logging;
using CmdsManager.Infrastructure.Startup;
using CmdsManager.Infrastructure.Windows;
using CmdsManager.Presentation.Forms;
using CmdsManager.Presentation.Tray;
using WinFormsApplication = System.Windows.Forms.Application;

namespace CmdsManager
{
    internal static class Program
    {
        private sealed class ManagedStartInvocation
        {
            internal string ParentWorkingDirectory { get; set; }
            internal string[] Arguments { get; set; }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            WinFormsApplication.EnableVisualStyles();
            WinFormsApplication.SetCompatibleTextRenderingDefault(false);
            WinFormsApplication.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            var actualArgs = args ?? new string[0];
            string runRequest;
            ManagedStartInvocation managedStart;
            try
            {
                runRequest = ResolveRunRequest(actualArgs);
                managedStart = ResolveManagedStart(actualArgs);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "CmdsManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var instance = new SingleInstanceGuard())
            {
                if (!instance.IsPrimaryInstance)
                {
                    var command = managedStart != null
                        ? BuildManagedStartCommand(managedStart)
                        : runRequest != null ? BuildRunCommand(runRequest) : null;
                    if (command == null || !instance.SendCommand(command))
                    {
                        instance.SignalPrimaryInstance();
                    }
                    return;
                }

                try
                {
                    RunPrimaryInstance(actualArgs, instance, runRequest, managedStart);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        "CmdsManager не удалось запустить.\n\n" + exception.Message,
                        "CmdsManager",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private static void RunPrimaryInstance(string[] args, SingleInstanceGuard instance, string initialRunRequest,
            ManagedStartInvocation initialManagedStart)
        {
            var automaticallyStarted = args.Any(value => value.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
            var configPath = ResolveConfigurationPath(args);
            var store = new ConfigurationStore(configPath);
            var configuration = store.LoadOrCreate();
            var state = new ConfigurationState(configuration);
            var text = new LocalizationService(state);
            var configDirectory = Path.GetDirectoryName(store.ConfigPath);
            var logDirectory = Path.Combine(configDirectory, "logs");

            using (var log = new SimpleFileLogger(logDirectory, configuration.Application.LogRetentionDays))
            {
                log.Information("CmdsManager is starting.");
                var startup = new RegistryStartupRegistration(WinFormsApplication.ExecutablePath);
                if (!automaticallyStarted)
                {
                    startup.Synchronize(configuration.Application.StartWithWindows);
                }

                var commandBuilder = new ScriptCommandBuilder(configDirectory, WinFormsApplication.ExecutablePath);
                var editor = new WindowsScriptEditorLauncher(commandBuilder);
                using (var supervisor = new ProcessSupervisor(commandBuilder, log, () => state.Current.Application.LogScriptOutput))
                using (var mainForm = new MainForm(state, store, supervisor, editor, startup, log, text))
                using (var context = new CmdsApplicationContext(mainForm, supervisor, state, log, text, automaticallyStarted))
                {
                    WinFormsApplication.ThreadException += (sender, eventArgs) =>
                    {
                        log.Error("Unhandled UI exception.", eventArgs.Exception);
                        MessageBox.Show(mainForm, eventArgs.Exception.Message, text["App.UiErrorTitle"], MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };
                    AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
                    {
                        log.Error("Unhandled application exception.", eventArgs.ExceptionObject as Exception);
                    };

                    instance.StartListening(context.ActivateFromAnotherInstance, context.HandleExternalCommand);
                    if (!string.IsNullOrWhiteSpace(initialRunRequest))
                    {
                        context.HandleExternalCommand(BuildRunCommand(initialRunRequest));
                    }
                    if (initialManagedStart != null)
                    {
                        context.HandleExternalCommand(BuildManagedStartCommand(initialManagedStart));
                    }
                    WinFormsApplication.Run(context);
                }

                log.Information("CmdsManager stopped.");
            }
        }

        private static string ResolveConfigurationPath(string[] args)
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].Equals("--config", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("После --config необходимо указать путь к INI-файлу.");
                }

                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(args[index + 1]));
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CmdsManager.ini");
        }

        private static string ResolveRunRequest(string[] args)
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].Equals("--run", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("После --run необходимо указать имя или GUID скрипта.");
                }

                return args[index + 1].Trim();
            }

            return null;
        }

        private static string BuildRunCommand(string selector)
        {
            return "RUN " + Convert.ToBase64String(Encoding.UTF8.GetBytes(selector ?? string.Empty));
        }

        private static ManagedStartInvocation ResolveManagedStart(string[] args)
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index].Equals("--managed-start-env", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Environment.GetEnvironmentVariable("CMDSMANAGER_START_CWD");
                    var line = Environment.GetEnvironmentVariable("CMDSMANAGER_START_LINE");
                    if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(line))
                        throw new ArgumentException("Для --managed-start-env не заданы параметры дочернего START.");
                    return new ManagedStartInvocation
                    {
                        ParentWorkingDirectory = parent,
                        Arguments = SplitCmdArguments(line)
                    };
                }
                if (!args[index].Equals("--managed-start-from", StringComparison.OrdinalIgnoreCase)) continue;
                if (index + 2 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    throw new ArgumentException("После --managed-start-from необходимо указать рабочую папку и аргументы START.");
                return new ManagedStartInvocation
                {
                    ParentWorkingDirectory = args[index + 1],
                    Arguments = args.Skip(index + 2).ToArray()
                };
            }
            return null;
        }

        private static string[] SplitCmdArguments(string commandLine)
        {
            var result = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
            var quoted = false;
            var started = false;
            foreach (var character in commandLine ?? string.Empty)
            {
                if (character == '"')
                {
                    quoted = !quoted;
                    started = true;
                    continue;
                }
                if (!quoted && char.IsWhiteSpace(character))
                {
                    if (started)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        started = false;
                    }
                    continue;
                }
                current.Append(character);
                started = true;
            }
            if (started) result.Add(current.ToString());
            return result.ToArray();
        }

        private static string BuildManagedStartCommand(ManagedStartInvocation invocation)
        {
            var values = new[] { invocation.ParentWorkingDirectory ?? string.Empty }
                .Concat(invocation.Arguments ?? new string[0]);
            return "START " + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", values)));
        }
    }
}
