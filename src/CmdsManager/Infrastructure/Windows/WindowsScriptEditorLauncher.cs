using System;
using System.Diagnostics;
using System.IO;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Execution;

namespace CmdsManager.Infrastructure.Windows
{
    public sealed class WindowsScriptEditorLauncher : IScriptEditorLauncher
    {
        private readonly ScriptCommandBuilder _paths;

        public WindowsScriptEditorLauncher(ScriptCommandBuilder paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public void Edit(string scriptPath, ApplicationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var resolvedScript = _paths.ResolvePath(scriptPath);
            if (!File.Exists(resolvedScript))
            {
                throw new FileNotFoundException("Script file was not found.", resolvedScript);
            }

            var editorPath = _paths.ResolvePath(settings.EditorPath);
            if (!File.Exists(editorPath))
            {
                throw new FileNotFoundException("Configured editor was not found.", editorPath);
            }

            var quotedFile = ScriptCommandBuilder.QuoteWindowsArgument(resolvedScript);
            var argumentsTemplate = settings.EditorArguments ?? string.Empty;
            var arguments = argumentsTemplate.IndexOf("{file}", StringComparison.OrdinalIgnoreCase) >= 0
                ? ReplaceOrdinalIgnoreCase(argumentsTemplate, "{file}", quotedFile)
                : (argumentsTemplate.Trim() + " " + quotedFile).Trim();

            Process.Start(new ProcessStartInfo
            {
                FileName = editorPath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(resolvedScript),
                UseShellExecute = false
            });
        }

        public void ShowInFolder(string scriptPath)
        {
            var resolvedScript = _paths.ResolvePath(scriptPath);
            var windowsDirectory = Environment.GetEnvironmentVariable("WINDIR") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(windowsDirectory, "explorer.exe"),
                Arguments = "/select," + ScriptCommandBuilder.QuoteWindowsArgument(resolvedScript),
                UseShellExecute = false
            });
        }

        private static string ReplaceOrdinalIgnoreCase(string value, string search, string replacement)
        {
            var index = value.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            return index < 0 ? value : value.Substring(0, index) + replacement + value.Substring(index + search.Length);
        }
    }
}
