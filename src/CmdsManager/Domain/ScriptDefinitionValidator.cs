using System;
using System.Collections.Generic;
using System.IO;

namespace CmdsManager.Domain
{
    public static class ScriptDefinitionValidator
    {
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cmd",
            ".bat",
            ".ps1",
            ".vbs"
        };

        public static void Validate(ScriptDefinition script, bool requireExistingFile)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            if (script.Id == Guid.Empty)
            {
                throw new ArgumentException("Script identifier cannot be empty.", nameof(script));
            }

            if (string.IsNullOrWhiteSpace(script.Name))
            {
                throw new ArgumentException("Script name is required.", nameof(script));
            }

            if (script.Name.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                throw new ArgumentException("Script name cannot contain new lines.", nameof(script));
            }

            if (string.IsNullOrWhiteSpace(script.Path))
            {
                throw new ArgumentException("Script path is required.", nameof(script));
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(script.Path.Trim());
            var extension = Path.GetExtension(expandedPath);
            if (!SupportedExtensions.Contains(extension))
            {
                throw new ArgumentException("Supported extensions are .cmd, .bat, .ps1 and .vbs.", nameof(script));
            }

            if (requireExistingFile && !File.Exists(expandedPath))
            {
                throw new FileNotFoundException("Script file was not found.", expandedPath);
            }

            if (script.Launch == null)
            {
                throw new ArgumentException("Launch profile is required.", nameof(script));
            }

            if (script.Launch.StopTimeoutSeconds < 0 || script.Launch.StopTimeoutSeconds > 3600)
            {
                throw new ArgumentOutOfRangeException(nameof(script), "Stop timeout must be between 0 and 3600 seconds.");
            }

            if (script.Launch.AutoStartDelaySeconds < 0 || script.Launch.AutoStartDelaySeconds > 86400)
            {
                throw new ArgumentOutOfRangeException(nameof(script), "Auto-start delay must be between 0 and 86400 seconds.");
            }

            ValidateInterpreter(extension, script.Launch.Interpreter);

            if (script.Launch.Interpreter == ScriptInterpreter.WScript && script.Launch.CaptureOutput)
            {
                throw new ArgumentException("WScript does not support output capture. Use CScript or disable capture.", nameof(script));
            }
        }

        public static ScriptInterpreter ResolveAutoInterpreter(string scriptPath)
        {
            var extension = Path.GetExtension(scriptPath ?? string.Empty);
            if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptInterpreter.Cmd;
            }

            if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptInterpreter.WindowsPowerShell;
            }

            if (extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase))
            {
                return ScriptInterpreter.CScript;
            }

            throw new NotSupportedException("Unsupported script extension: " + extension);
        }

        private static void ValidateInterpreter(string extension, ScriptInterpreter interpreter)
        {
            if (interpreter == ScriptInterpreter.Auto)
            {
                return;
            }

            var valid =
                ((extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)) && interpreter == ScriptInterpreter.Cmd) ||
                (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && (interpreter == ScriptInterpreter.WindowsPowerShell || interpreter == ScriptInterpreter.PowerShell7)) ||
                (extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase) && (interpreter == ScriptInterpreter.CScript || interpreter == ScriptInterpreter.WScript));

            if (!valid)
            {
                throw new ArgumentException("Selected interpreter does not match the script extension.", nameof(interpreter));
            }
        }
    }
}
