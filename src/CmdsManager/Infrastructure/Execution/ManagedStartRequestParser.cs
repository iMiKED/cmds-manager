using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CmdsManager.Domain;

namespace CmdsManager.Infrastructure.Execution
{
    public sealed class ManagedStartRequest
    {
        public string Title { get; set; }
        public string ScriptPath { get; set; }
        public string ScriptArguments { get; set; }
        public string WorkingDirectory { get; set; }

        public ScriptDefinition ToScriptDefinition()
        {
            return new ScriptDefinition
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(ScriptPath) : Title.Trim(),
                Path = ScriptPath,
                Launch = new LaunchProfile
                {
                    Interpreter = ScriptInterpreter.Cmd,
                    Arguments = ScriptArguments ?? string.Empty,
                    WorkingDirectory = WorkingDirectory,
                    WindowMode = ScriptWindowMode.Hidden,
                    CaptureOutput = true,
                    OutputEncoding = ScriptOutputEncoding.Auto,
                    AllowParallelInstances = true,
                    StopPolicy = ScriptStopPolicy.Kill,
                    StopTimeoutSeconds = 0
                }
            };
        }
    }

    public static class ManagedStartRequestParser
    {
        public static ManagedStartRequest Parse(string parentWorkingDirectory, IEnumerable<string> rawArguments)
        {
            var arguments = (rawArguments ?? Enumerable.Empty<string>()).ToArray();
            if (arguments.Length == 0) throw new ArgumentException("The START command has no arguments.");

            var workingDirectory = ResolveDirectory(parentWorkingDirectory, parentWorkingDirectory);
            var index = 0;
            var title = string.Empty;
            if (!IsStartOption(arguments[index]) && !IsCmd(arguments[index]))
            {
                title = arguments[index] ?? string.Empty;
                index++;
            }

            while (index < arguments.Length && !IsCmd(arguments[index]))
            {
                var option = arguments[index] ?? string.Empty;
                if (option.Equals("/D", StringComparison.OrdinalIgnoreCase))
                {
                    if (++index >= arguments.Length) throw new ArgumentException("START /D requires a working directory.");
                    workingDirectory = ResolveDirectory(arguments[index], parentWorkingDirectory);
                    index++;
                    continue;
                }
                if (option.StartsWith("/D", StringComparison.OrdinalIgnoreCase) && option.Length > 2)
                {
                    workingDirectory = ResolveDirectory(option.Substring(2), parentWorkingDirectory);
                    index++;
                    continue;
                }
                if (IsStartOption(option))
                {
                    index++;
                    continue;
                }
                throw new NotSupportedException("Only START commands that launch cmd.exe /c or /k are managed in tabs.");
            }

            if (index >= arguments.Length || !IsCmd(arguments[index]))
                throw new NotSupportedException("The managed START command does not launch cmd.exe.");
            index++;

            string scriptToken = null;
            while (index < arguments.Length)
            {
                var value = arguments[index] ?? string.Empty;
                if (value.Equals("/c", StringComparison.OrdinalIgnoreCase) || value.Equals("/k", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    break;
                }
                if ((value.StartsWith("/c", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/k", StringComparison.OrdinalIgnoreCase)) && value.Length > 2)
                {
                    scriptToken = value.Substring(2);
                    index++;
                    break;
                }
                if (IsCmdOption(value))
                {
                    index++;
                    continue;
                }
                throw new NotSupportedException("The managed cmd.exe command must use /c or /k.");
            }

            if (scriptToken == null && index < arguments.Length &&
                string.Equals(arguments[index], "call", StringComparison.OrdinalIgnoreCase)) index++;
            if (scriptToken == null && index < arguments.Length) scriptToken = arguments[index++];
            if (string.IsNullOrWhiteSpace(scriptToken)) throw new ArgumentException("The managed START command has no child script path.");

            var expandedScript = Environment.ExpandEnvironmentVariables(scriptToken.Trim().Trim('"'));
            var scriptPath = Path.GetFullPath(Path.IsPathRooted(expandedScript)
                ? expandedScript
                : Path.Combine(workingDirectory, expandedScript));
            var extension = Path.GetExtension(scriptPath);
            if (!extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Only .cmd and .bat child scripts can be opened as managed tabs.");
            if (!File.Exists(scriptPath)) throw new FileNotFoundException("The child script was not found.", scriptPath);

            var remainingArguments = arguments.Skip(index).Select(ScriptCommandBuilder.QuoteWindowsArgument);
            return new ManagedStartRequest
            {
                Title = title,
                ScriptPath = scriptPath,
                ScriptArguments = string.Join(" ", remainingArguments),
                WorkingDirectory = workingDirectory
            };
        }

        private static string ResolveDirectory(string value, string fallback)
        {
            var expanded = Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(expanded)) expanded = Environment.CurrentDirectory;
            var baseDirectory = string.IsNullOrWhiteSpace(fallback)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(fallback.Trim().Trim('"')));
            var path = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDirectory, expanded));
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The START working directory was not found: " + path);
            return path;
        }

        private static bool IsCmd(string value)
        {
            var fileName = Path.GetFileName((value ?? string.Empty).Trim().Trim('"'));
            return fileName.Equals("cmd", StringComparison.OrdinalIgnoreCase) || fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStartOption(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '/') return false;
            return value.Equals("/B", StringComparison.OrdinalIgnoreCase) || value.Equals("/I", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/MIN", StringComparison.OrdinalIgnoreCase) || value.Equals("/MAX", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/WAIT", StringComparison.OrdinalIgnoreCase) || value.Equals("/LOW", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/NORMAL", StringComparison.OrdinalIgnoreCase) || value.Equals("/HIGH", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/REALTIME", StringComparison.OrdinalIgnoreCase) || value.Equals("/ABOVENORMAL", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/BELOWNORMAL", StringComparison.OrdinalIgnoreCase) || value.StartsWith("/D", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCmdOption(string value)
        {
            return value.Equals("/d", StringComparison.OrdinalIgnoreCase) || value.Equals("/q", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/s", StringComparison.OrdinalIgnoreCase) || value.Equals("/a", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/u", StringComparison.OrdinalIgnoreCase) || value.Equals("/e:on", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/e:off", StringComparison.OrdinalIgnoreCase) || value.Equals("/f:on", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/f:off", StringComparison.OrdinalIgnoreCase) || value.Equals("/v:on", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("/v:off", StringComparison.OrdinalIgnoreCase);
        }
    }
}
