using System;
using System.Collections.Generic;

namespace CmdsManager.Domain
{
    public enum ScriptInterpreter
    {
        Auto,
        Cmd,
        WindowsPowerShell,
        PowerShell7,
        CScript,
        WScript
    }

    public enum ScriptWindowMode
    {
        Hidden,
        Normal,
        Minimized
    }

    public enum ScriptStopPolicy
    {
        GracefulThenKill,
        Kill
    }

    public enum ScriptRuntimeState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Exited,
        Failed
    }

    public sealed class LaunchProfile
    {
        public ScriptInterpreter Interpreter { get; set; } = ScriptInterpreter.Auto;
        public ScriptWindowMode WindowMode { get; set; } = ScriptWindowMode.Hidden;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public bool CaptureOutput { get; set; } = true;
        public bool AllowParallelInstances { get; set; }
        public bool AutoStartWithApplication { get; set; }
        public int AutoStartOrder { get; set; } = 100;
        public int AutoStartDelaySeconds { get; set; }
        public ScriptStopPolicy StopPolicy { get; set; } = ScriptStopPolicy.GracefulThenKill;
        public int StopTimeoutSeconds { get; set; } = 5;

        public LaunchProfile Clone()
        {
            return (LaunchProfile)MemberwiseClone();
        }
    }

    public sealed class ScriptDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Path { get; set; } = string.Empty;
        public LaunchProfile Launch { get; set; } = new LaunchProfile();

        public ScriptDefinition Clone()
        {
            var clone = (ScriptDefinition)MemberwiseClone();
            clone.Launch = Launch?.Clone() ?? new LaunchProfile();
            return clone;
        }
    }

    public sealed class ApplicationSettings
    {
        public int ConfigVersion { get; set; } = 1;
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimized { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartHiddenWhenAutoStarted { get; set; } = true;
        public bool AutoStartScripts { get; set; } = true;
        public bool ConfirmBeforeDelete { get; set; } = true;
        public string EditorPath { get; set; } = @"%SystemRoot%\System32\notepad.exe";
        public string EditorArguments { get; set; } = "\"{file}\"";
        public string LogLevel { get; set; } = "Information";
        public int LogRetentionDays { get; set; } = 14;
        public bool LogScriptOutput { get; set; }

        public ApplicationSettings Clone()
        {
            return (ApplicationSettings)MemberwiseClone();
        }
    }

    public sealed class AppConfiguration
    {
        public ApplicationSettings Application { get; set; } = new ApplicationSettings();
        public LaunchProfile Defaults { get; set; } = new LaunchProfile();
        public string PowerShell7Path { get; set; } = string.Empty;
        public List<ScriptDefinition> Scripts { get; set; } = new List<ScriptDefinition>();

        public AppConfiguration Clone()
        {
            var clone = new AppConfiguration
            {
                Application = Application?.Clone() ?? new ApplicationSettings(),
                Defaults = Defaults?.Clone() ?? new LaunchProfile(),
                PowerShell7Path = PowerShell7Path ?? string.Empty
            };

            foreach (var script in Scripts)
            {
                clone.Scripts.Add(script.Clone());
            }

            return clone;
        }
    }

    public sealed class ScriptRuntimeSnapshot
    {
        public Guid ScriptId { get; set; }
        public ScriptRuntimeState State { get; set; }
        public int ActiveCount { get; set; }
        public int? ProcessId { get; set; }
        public DateTime? StartedAt { get; set; }
        public int? LastExitCode { get; set; }
        public string Error { get; set; } = string.Empty;
    }
}
