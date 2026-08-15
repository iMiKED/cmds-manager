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
        [STAThread]
        private static void Main(string[] args)
        {
            WinFormsApplication.EnableVisualStyles();
            WinFormsApplication.SetCompatibleTextRenderingDefault(false);
            WinFormsApplication.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            var actualArgs = args ?? new string[0];
            string runRequest;
            try
            {
                runRequest = ResolveRunRequest(actualArgs);
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
                    if (runRequest == null || !instance.SendCommand(BuildRunCommand(runRequest)))
                    {
                        instance.SignalPrimaryInstance();
                    }
                    return;
                }

                try
                {
                    RunPrimaryInstance(actualArgs, instance, runRequest);
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

        private static void RunPrimaryInstance(string[] args, SingleInstanceGuard instance, string initialRunRequest)
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

                var commandBuilder = new ScriptCommandBuilder(configDirectory);
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
    }
}
