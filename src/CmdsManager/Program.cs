using System;
using System.IO;
using System.Linq;
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

            using (var instance = new SingleInstanceGuard())
            {
                if (!instance.IsPrimaryInstance)
                {
                    instance.SignalPrimaryInstance();
                    return;
                }

                try
                {
                    RunPrimaryInstance(args ?? new string[0], instance);
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

        private static void RunPrimaryInstance(string[] args, SingleInstanceGuard instance)
        {
            var automaticallyStarted = args.Any(value => value.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
            var configPath = ResolveConfigurationPath(args);
            var store = new ConfigurationStore(configPath);
            var configuration = store.LoadOrCreate();
            var state = new ConfigurationState(configuration);
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
                using (var mainForm = new MainForm(state, store, supervisor, editor, startup, log))
                using (var context = new CmdsApplicationContext(mainForm, supervisor, state, log, automaticallyStarted))
                {
                    WinFormsApplication.ThreadException += (sender, eventArgs) =>
                    {
                        log.Error("Unhandled UI exception.", eventArgs.Exception);
                        MessageBox.Show(mainForm, eventArgs.Exception.Message, "Ошибка CmdsManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };
                    AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
                    {
                        log.Error("Unhandled application exception.", eventArgs.ExceptionObject as Exception);
                    };

                    instance.StartListening(context.ActivateFromAnotherInstance);
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
    }
}
