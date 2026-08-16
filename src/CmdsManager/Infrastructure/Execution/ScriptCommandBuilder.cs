using System;
using System.IO;
using System.Text;
using CmdsManager.Domain;

namespace CmdsManager.Infrastructure.Execution
{
    public sealed class ProcessLaunchSpec
    {
        public string ExecutablePath { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public bool CaptureOutput { get; set; }
        public ScriptWindowMode WindowMode { get; set; }
        public ScriptInterpreter Interpreter { get; set; }
        public ScriptOutputEncoding OutputEncoding { get; set; }
        public string TemporaryScriptPath { get; set; }
    }

    public sealed class ScriptCommandBuilder
    {
        private readonly string _configurationDirectory;
        private readonly string _managerExecutablePath;

        public ScriptCommandBuilder(string configurationDirectory, string managerExecutablePath = null)
        {
            _configurationDirectory = Path.GetFullPath(configurationDirectory ?? throw new ArgumentNullException(nameof(configurationDirectory)));
            _managerExecutablePath = string.IsNullOrWhiteSpace(managerExecutablePath)
                ? null
                : Path.GetFullPath(managerExecutablePath);
        }

        public ProcessLaunchSpec Build(ScriptDefinition script, string configuredPowerShell7Path)
        {
            ScriptDefinitionValidator.Validate(script, false);
            var scriptPath = ResolvePath(script.Path);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("Script file was not found.", scriptPath);
            }

            var interpreter = script.Launch.Interpreter == ScriptInterpreter.Auto
                ? ScriptDefinitionValidator.ResolveAutoInterpreter(scriptPath)
                : script.Launch.Interpreter;

            var workingDirectory = string.IsNullOrWhiteSpace(script.Launch.WorkingDirectory)
                ? Path.GetDirectoryName(scriptPath)
                : ResolvePath(script.Launch.WorkingDirectory);

            if (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException("Working directory was not found: " + workingDirectory);
            }

            var userArguments = script.Launch.Arguments?.Trim() ?? string.Empty;
            var spec = new ProcessLaunchSpec
            {
                WorkingDirectory = workingDirectory,
                CaptureOutput = script.Launch.CaptureOutput,
                WindowMode = script.Launch.WindowMode,
                Interpreter = interpreter,
                OutputEncoding = script.Launch.OutputEncoding
            };

            switch (interpreter)
            {
                case ScriptInterpreter.Cmd:
                    spec.ExecutablePath = ResolveCmd();
                    var executableScriptPath = PrepareCmdScript(scriptPath, script.Id, spec);
                    spec.Arguments = BuildCmdArguments(executableScriptPath, userArguments);
                    break;
                case ScriptInterpreter.WindowsPowerShell:
                    spec.ExecutablePath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                    EnsureExecutable(spec.ExecutablePath, "Windows PowerShell 5.1");
                    spec.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + QuoteWindowsArgument(scriptPath) + AppendArguments(userArguments);
                    break;
                case ScriptInterpreter.PowerShell7:
                    spec.ExecutablePath = ResolvePowerShell7(configuredPowerShell7Path);
                    spec.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + QuoteWindowsArgument(scriptPath) + AppendArguments(userArguments);
                    break;
                case ScriptInterpreter.CScript:
                    spec.ExecutablePath = Path.Combine(Environment.SystemDirectory, "cscript.exe");
                    EnsureExecutable(spec.ExecutablePath, "Windows Script Host (console)");
                    spec.Arguments = "//Nologo " + QuoteWindowsArgument(scriptPath) + AppendArguments(userArguments);
                    break;
                case ScriptInterpreter.WScript:
                    spec.ExecutablePath = Path.Combine(Environment.SystemDirectory, "wscript.exe");
                    EnsureExecutable(spec.ExecutablePath, "Windows Script Host");
                    spec.Arguments = QuoteWindowsArgument(scriptPath) + AppendArguments(userArguments);
                    spec.CaptureOutput = false;
                    break;
                default:
                    throw new NotSupportedException("Unsupported interpreter: " + interpreter);
            }

            return spec;
        }

        private string PrepareCmdScript(string scriptPath, Guid scriptId, ProcessLaunchSpec spec)
        {
            if (!spec.CaptureOutput || string.IsNullOrEmpty(_managerExecutablePath) || !File.Exists(_managerExecutablePath))
                return scriptPath;

            try
            {
                var transformed = CmdScriptTransformer.TryCreateManagedCopy(scriptPath, scriptId);
                if (string.IsNullOrEmpty(transformed)) return scriptPath;
                spec.TemporaryScriptPath = transformed;
                Environment.SetEnvironmentVariable("CMDSMANAGER_HOST_EXE", _managerExecutablePath, EnvironmentVariableTarget.Process);
                return transformed;
            }
            catch (IOException)
            {
                return scriptPath;
            }
            catch (UnauthorizedAccessException)
            {
                return scriptPath;
            }
        }

        public string ResolvePath(string value)
        {
            var expanded = Environment.ExpandEnvironmentVariables((value ?? string.Empty).Trim());
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(_configurationDirectory, expanded));
        }

        public static string QuoteWindowsArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return argument;
            }

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }

                builder.Append('\\', backslashes);
                backslashes = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private string ResolvePowerShell7(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var expanded = ResolvePath(configuredPath);
                if (Directory.Exists(expanded))
                {
                    expanded = Path.Combine(expanded, "pwsh.exe");
                }

                EnsureExecutable(expanded, "PowerShell 7");
                return expanded;
            }

            var fromPath = FindOnPath("pwsh.exe");
            if (!string.IsNullOrEmpty(fromPath))
            {
                return fromPath;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var standardPath = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(standardPath))
            {
                return standardPath;
            }

            throw new FileNotFoundException("PowerShell 7 (pwsh.exe) is not installed or could not be found. Select Windows PowerShell 5.1 or configure PowerShell7Path.");
        }

        private static string ResolveCmd()
        {
            var path = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            }

            path = Environment.ExpandEnvironmentVariables(path.Trim());
            EnsureExecutable(path, "Command Prompt");
            return Path.GetFullPath(path);
        }

        private static string BuildCmdArguments(string scriptPath, string userArguments)
        {
            if (scriptPath.IndexOf('"') >= 0)
            {
                throw new ArgumentException("Script path cannot contain a quotation mark.", nameof(scriptPath));
            }

            return "/d /s /c \"\"" + scriptPath + "\"" + AppendArguments(userArguments) + "\"";
        }

        private static string AppendArguments(string arguments)
        {
            return string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments.Trim();
        }

        private static string FindOnPath(string fileName)
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var rawDirectory in pathValue.Split(Path.PathSeparator))
            {
                var directory = rawDirectory.Trim().Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                try
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }

            return null;
        }

        private static void EnsureExecutable(string path, string displayName)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException(displayName + " executable was not found.", path);
            }
        }
    }
}
