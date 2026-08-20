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

    public enum ScriptOutputEncoding
    {
        Auto,
        Utf8,
        Oem,
        Windows1251,
        Utf16LittleEndian
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

    public enum ApplicationTheme
    {
        System,
        Light,
        Dark
    }

    public enum HotkeyAction
    {
        ShowApp,
        QuickLaunch,
        EmergencyStopAll,
        StartSelected,
        StopSelected,
        RestartSelected,
        AddScript,
        EditScript,
        DeleteScript,
        OpenSettings,
        NextConsoleTab,
        PreviousConsoleTab,
        CloseConsoleTab,
        ToggleConsoleDetach,
        ToggleConsolePane,
        FindConsole,
        FindNext,
        FindPrevious,
        ToggleScrollLock,
        ToggleConsoleFullScreen,
        ToggleWordWrap,
        ClearConsole,
        CopyConsoleSelection,
        SelectAllConsole,
        SaveConsole,
        IncreaseConsoleFont,
        DecreaseConsoleFont,
        ResetConsoleFont
    }

    public enum HotkeyScope
    {
        Global,
        Application,
        Console
    }

    public sealed class HotkeyBinding
    {
        public bool Enabled { get; set; }
        public string Gesture { get; set; } = string.Empty;

        public HotkeyBinding Clone()
        {
            return (HotkeyBinding)MemberwiseClone();
        }
    }

    public sealed class HotkeySettings
    {
        private readonly Dictionary<HotkeyAction, HotkeyBinding> _bindings =
            new Dictionary<HotkeyAction, HotkeyBinding>();

        public HotkeySettings()
        {
            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
                _bindings[action] = CreateDefaultBinding(action);
        }

        public HotkeyBinding this[HotkeyAction action]
        {
            get { return _bindings[action]; }
            set { _bindings[action] = value ?? CreateDefaultBinding(action); }
        }

        public IEnumerable<KeyValuePair<HotkeyAction, HotkeyBinding>> Bindings => _bindings;

        public HotkeySettings Clone()
        {
            var clone = new HotkeySettings();
            foreach (var pair in _bindings) clone[pair.Key] = pair.Value?.Clone();
            return clone;
        }

        public static HotkeyBinding CreateDefaultBinding(HotkeyAction action)
        {
            return new HotkeyBinding
            {
                Enabled = GetScope(action) != HotkeyScope.Global,
                Gesture = DefaultGesture(action)
            };
        }

        public static HotkeyScope GetScope(HotkeyAction action)
        {
            if (action == HotkeyAction.ShowApp || action == HotkeyAction.QuickLaunch ||
                action == HotkeyAction.EmergencyStopAll)
                return HotkeyScope.Global;
            if (action >= HotkeyAction.FindConsole)
                return HotkeyScope.Console;
            return HotkeyScope.Application;
        }

        public static string DefaultGesture(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.ShowApp: return "Ctrl+Alt+M";
                case HotkeyAction.QuickLaunch: return "Ctrl+Alt+Space";
                case HotkeyAction.EmergencyStopAll: return "Ctrl+Alt+Shift+F12";
                case HotkeyAction.StartSelected: return "F5";
                case HotkeyAction.StopSelected: return "Shift+F5";
                case HotkeyAction.RestartSelected: return "Ctrl+Shift+F5";
                case HotkeyAction.AddScript: return "Ctrl+N";
                case HotkeyAction.EditScript: return "Ctrl+E";
                case HotkeyAction.DeleteScript: return "Delete";
                case HotkeyAction.OpenSettings: return "Ctrl+Comma";
                case HotkeyAction.NextConsoleTab: return "Ctrl+Tab";
                case HotkeyAction.PreviousConsoleTab: return "Ctrl+Shift+Tab";
                case HotkeyAction.CloseConsoleTab: return "Ctrl+W";
                case HotkeyAction.ToggleConsoleDetach: return "Ctrl+Shift+D";
                case HotkeyAction.ToggleConsolePane: return "Ctrl+Shift+M";
                case HotkeyAction.FindConsole: return "Ctrl+F";
                case HotkeyAction.FindNext: return "F3";
                case HotkeyAction.FindPrevious: return "Shift+F3";
                case HotkeyAction.ToggleScrollLock: return "ScrollLock";
                case HotkeyAction.ToggleConsoleFullScreen: return "F11";
                case HotkeyAction.ToggleWordWrap: return "Alt+Z";
                case HotkeyAction.ClearConsole: return "Ctrl+L";
                case HotkeyAction.CopyConsoleSelection: return "Ctrl+C";
                case HotkeyAction.SelectAllConsole: return "Ctrl+A";
                case HotkeyAction.SaveConsole: return "Ctrl+S";
                case HotkeyAction.IncreaseConsoleFont: return "Ctrl+Shift+Plus";
                case HotkeyAction.DecreaseConsoleFont: return "Ctrl+Minus";
                case HotkeyAction.ResetConsoleFont: return "Ctrl+0";
                default: return string.Empty;
            }
        }
    }

    public sealed class LaunchProfile
    {
        public ScriptInterpreter Interpreter { get; set; } = ScriptInterpreter.Auto;
        public ScriptWindowMode WindowMode { get; set; } = ScriptWindowMode.Hidden;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public bool CaptureOutput { get; set; } = true;
        public ScriptOutputEncoding OutputEncoding { get; set; } = ScriptOutputEncoding.Auto;
        public bool WordWrap { get; set; }
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
        public int ConfigVersion { get; set; } = 12;
        public ApplicationTheme Theme { get; set; } = ApplicationTheme.System;
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimized { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartHiddenWhenAutoStarted { get; set; } = true;
        public bool AutoStartScripts { get; set; } = true;
        public bool ConfirmBeforeDelete { get; set; } = true;
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
        public bool ShowAppHotkeyEnabled
        {
            get { return Hotkeys[HotkeyAction.ShowApp].Enabled; }
            set { Hotkeys[HotkeyAction.ShowApp].Enabled = value; }
        }
        public string ShowAppHotkey
        {
            get { return Hotkeys[HotkeyAction.ShowApp].Gesture; }
            set { Hotkeys[HotkeyAction.ShowApp].Gesture = value ?? string.Empty; }
        }
        public bool MainWindowPlacementSaved { get; set; }
        public int MainWindowX { get; set; }
        public int MainWindowY { get; set; }
        public int MainWindowWidth { get; set; } = 1120;
        public int MainWindowHeight { get; set; } = 680;
        public bool MainWindowMaximized { get; set; }
        public string EditorPath { get; set; } = @"%SystemRoot%\System32\notepad.exe";
        public string EditorArguments { get; set; } = "\"{file}\"";
        public string LogLevel { get; set; } = "Information";
        public int LogRetentionDays { get; set; } = 14;
        public bool LogScriptOutput { get; set; }
        public string ConsoleFontName { get; set; } = "Consolas";
        public float ConsoleFontSize { get; set; } = 10f;
        public int ConsolePaneHeight { get; set; } = 235;
        public int ConsoleBufferSizeKb { get; set; } = 256;
        public bool ConsoleAutoRecord { get; set; }
        public int ConsoleLogMaxSizeMb { get; set; } = 50;
        public string ConsoleForegroundColor { get; set; } = "#DCDCDC";
        public string ConsoleBackgroundColor { get; set; } = "#1C1C1C";
        public int ConsoleBackgroundOpacity { get; set; } = 100;
        public string ConsoleTabForegroundColor { get; set; } = "#262B32";
        public string ConsoleActiveTabForegroundColor { get; set; } = "#F5F7FA";
        public string ConsoleTabBackgroundColor { get; set; } = "#FCFCFD";
        public int ConsoleTabBackgroundOpacity { get; set; } = 100;
        public string ConsoleActiveTabBackgroundColor { get; set; } = "#1C1C1C";
        public int ConsoleActiveTabBackgroundOpacity { get; set; } = 100;

        public ApplicationSettings Clone()
        {
            var clone = (ApplicationSettings)MemberwiseClone();
            clone.Hotkeys = Hotkeys?.Clone() ?? new HotkeySettings();
            return clone;
        }
    }

    public sealed class LocalizationSettings
    {
        public string Language { get; set; } = "ru";
        public Dictionary<string, Dictionary<string, string>> Languages { get; set; } =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public LocalizationSettings Clone()
        {
            var clone = new LocalizationSettings { Language = Language ?? "ru" };
            foreach (var language in Languages)
            {
                clone.Languages[language.Key] = new Dictionary<string, string>(language.Value, StringComparer.OrdinalIgnoreCase);
            }

            return clone;
        }
    }

    public sealed class AppConfiguration
    {
        public ApplicationSettings Application { get; set; } = new ApplicationSettings();
        public LaunchProfile Defaults { get; set; } = new LaunchProfile();
        public string PowerShell7Path { get; set; } = string.Empty;
        public LocalizationSettings Localization { get; set; } = new LocalizationSettings();
        public List<ScriptDefinition> Scripts { get; set; } = new List<ScriptDefinition>();

        public AppConfiguration Clone()
        {
            var clone = new AppConfiguration
            {
                Application = Application?.Clone() ?? new ApplicationSettings(),
                Defaults = Defaults?.Clone() ?? new LaunchProfile(),
                PowerShell7Path = PowerShell7Path ?? string.Empty,
                Localization = Localization?.Clone() ?? new LocalizationSettings()
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
