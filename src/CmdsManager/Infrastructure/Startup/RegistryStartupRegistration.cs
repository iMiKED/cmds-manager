using System;
using Microsoft.Win32;
using CmdsManager.Application;
using CmdsManager.Infrastructure.Execution;

namespace CmdsManager.Infrastructure.Startup
{
    public sealed class RegistryStartupRegistration : IApplicationStartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CmdsManager";
        private readonly string _executablePath;

        public RegistryStartupRegistration(string executablePath)
        {
            _executablePath = System.IO.Path.GetFullPath(executablePath ?? throw new ArgumentNullException(nameof(executablePath)));
        }

        public string RegisteredCommand
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return Convert.ToString(key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                }
            }
        }

        public void Synchronize(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Unable to open the current-user startup registry key.");
                }

                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    return;
                }

                var command = ScriptCommandBuilder.QuoteWindowsArgument(_executablePath) + " --autostart";
                if (command.Length > 260)
                {
                    throw new InvalidOperationException("The portable path is too long for the Windows Run registry entry. Move CmdsManager to a shorter path.");
                }

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }
    }
}
