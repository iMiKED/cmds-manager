using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Windows;

namespace CmdsManager.Infrastructure.Configuration
{
    public sealed class ConfigurationValidationException : FormatException
    {
        public ConfigurationValidationException(string section, string key, string message)
            : base(string.Format(CultureInfo.InvariantCulture, "[{0}] {1}: {2}", section, key, message))
        {
            Section = section;
            Key = key;
        }

        public string Section { get; }
        public string Key { get; }
    }

    public sealed class ConfigurationChangedException : IOException
    {
        public ConfigurationChangedException(string path)
            : base("Configuration was changed by another program. Reload it before saving: " + path)
        {
        }
    }

    public sealed class ConfigurationStore
    {
        private const int CurrentVersion = 12;
        private readonly object _sync = new object();
        private readonly UTF8Encoding _utf8 = new UTF8Encoding(false, true);
        private byte[] _loadedHash;

        public ConfigurationStore(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("Configuration path is required.", nameof(configPath));
            }

            ConfigPath = Path.GetFullPath(configPath);
        }

        public string ConfigPath { get; }

        public AppConfiguration LoadOrCreate()
        {
            lock (_sync)
            {
                EnsureConfigExists();
                return LoadInternal();
            }
        }

        public AppConfiguration Reload()
        {
            lock (_sync)
            {
                if (!File.Exists(ConfigPath))
                {
                    throw new FileNotFoundException("Configuration file was not found.", ConfigPath);
                }

                return LoadInternal();
            }
        }

        public void Save(AppConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            lock (_sync)
            {
                ValidateConfiguration(configuration);
                if (_loadedHash != null && File.Exists(ConfigPath) && !HashesEqual(_loadedHash, ComputeHash(ConfigPath)))
                {
                    throw new ConfigurationChangedException(ConfigPath);
                }

                var ini = BuildIni(configuration);
                WriteAtomically(ini.Serialize());
                _loadedHash = ComputeHash(ConfigPath);
            }
        }

        private AppConfiguration LoadInternal()
        {
            var text = File.ReadAllText(ConfigPath, _utf8);
            var ini = IniDocument.Parse(text);
            var configuration = ParseConfiguration(ini);
            var loadedVersion = configuration.Application.ConfigVersion;
            var requiresUpgrade = RequiresUpgrade(ini, loadedVersion);
            UpgradeConfiguration(configuration, loadedVersion);
            configuration.Application.ConfigVersion = CurrentVersion;
            ValidateConfiguration(configuration);
            if (requiresUpgrade)
            {
                WriteAtomically(BuildIni(configuration).Serialize());
            }

            _loadedHash = ComputeHash(ConfigPath);
            return configuration;
        }

        private void EnsureConfigExists()
        {
            if (File.Exists(ConfigPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CmdsManager.ini.example");
            if (File.Exists(examplePath) && !PathsEqual(examplePath, ConfigPath))
            {
                File.Copy(examplePath, ConfigPath, false);
                return;
            }

            var defaults = new AppConfiguration();
            WriteAtomically(BuildIni(defaults).Serialize());
        }

        private AppConfiguration ParseConfiguration(IniDocument ini)
        {
            if (!ini.HasSection("Application"))
            {
                throw new ConfigurationValidationException("Application", "", "required section is missing");
            }

            var result = new AppConfiguration();
            var app = result.Application;
            app.ConfigVersion = ReadInt(ini, "Application", "ConfigVersion", 1, 1, CurrentVersion);
            app.Theme = ReadEnum(ini, "Application", "Theme", ApplicationTheme.System);
            app.CloseToTray = ReadBool(ini, "Application", "CloseToTray", true);
            app.StartMinimized = ReadBool(ini, "Application", "StartMinimized", false);
            app.StartWithWindows = ReadBool(ini, "Application", "StartWithWindows", false);
            app.StartHiddenWhenAutoStarted = ReadBool(ini, "Application", "StartHiddenWhenAutoStarted", true);
            app.AutoStartScripts = ReadBool(ini, "Application", "AutoStartScripts", true);
            app.ConfirmBeforeDelete = ReadBool(ini, "Application", "ConfirmBeforeDelete", true);
            app.ShowAppHotkeyEnabled = ReadBool(ini, "Application", "ShowAppHotkeyEnabled", false);
            app.ShowAppHotkey = ini.Get("Application", "ShowAppHotkey", app.ShowAppHotkey).Trim();
            app.MainWindowPlacementSaved = ReadBool(ini, "Application", "MainWindowPlacementSaved", false);
            app.MainWindowX = ReadInt(ini, "Application", "MainWindowX", app.MainWindowX, -100000, 100000);
            app.MainWindowY = ReadInt(ini, "Application", "MainWindowY", app.MainWindowY, -100000, 100000);
            app.MainWindowWidth = ReadInt(ini, "Application", "MainWindowWidth", app.MainWindowWidth, 880, 20000);
            app.MainWindowHeight = ReadInt(ini, "Application", "MainWindowHeight", app.MainWindowHeight, 520, 20000);
            app.MainWindowMaximized = ReadBool(ini, "Application", "MainWindowMaximized", false);
            app.EditorPath = ini.Get("Application", "EditorPath", app.EditorPath);
            app.EditorArguments = ini.Get("Application", "EditorArguments", app.EditorArguments);
            app.LogLevel = ini.Get("Application", "LogLevel", app.LogLevel);
            app.LogRetentionDays = ReadInt(ini, "Application", "LogRetentionDays", 14, 1, 3650);
            app.LogScriptOutput = ReadBool(ini, "Application", "LogScriptOutput", false);
            app.ConsoleFontName = ini.Get("Application", "ConsoleFontName", app.ConsoleFontName);
            app.ConsoleFontSize = ReadFloat(ini, "Application", "ConsoleFontSize", app.ConsoleFontSize, 6f, 48f);
            app.ConsolePaneHeight = ReadInt(ini, "Application", "ConsolePaneHeight", app.ConsolePaneHeight, 100, 4000);
            app.ConsoleBufferSizeKb = ReadInt(ini, "Application", "ConsoleBufferSizeKb", app.ConsoleBufferSizeKb, 64, 1048576);
            app.ConsoleAutoRecord = ReadBool(ini, "Application", "ConsoleAutoRecord", false);
            app.ConsoleLogMaxSizeMb = ReadInt(ini, "Application", "ConsoleLogMaxSizeMb", app.ConsoleLogMaxSizeMb, 1, 4096);
            app.ConsoleForegroundColor = ini.Get("Application", "ConsoleForegroundColor", app.ConsoleForegroundColor);
            app.ConsoleBackgroundColor = ini.Get("Application", "ConsoleBackgroundColor", app.ConsoleBackgroundColor);
            app.ConsoleBackgroundOpacity = ReadInt(ini, "Application", "ConsoleBackgroundOpacity", app.ConsoleBackgroundOpacity, 0, 100);
            app.ConsoleTabForegroundColor = ini.Get("Application", "ConsoleTabForegroundColor", app.ConsoleTabForegroundColor);
            app.ConsoleActiveTabForegroundColor = ini.Get("Application", "ConsoleActiveTabForegroundColor", app.ConsoleActiveTabForegroundColor);
            app.ConsoleTabBackgroundColor = ini.Get("Application", "ConsoleTabBackgroundColor", app.ConsoleTabBackgroundColor);
            app.ConsoleTabBackgroundOpacity = ReadInt(ini, "Application", "ConsoleTabBackgroundOpacity", app.ConsoleTabBackgroundOpacity, 0, 100);
            app.ConsoleActiveTabBackgroundColor = ini.Get("Application", "ConsoleActiveTabBackgroundColor", app.ConsoleActiveTabBackgroundColor);
            app.ConsoleActiveTabBackgroundOpacity = ReadInt(ini, "Application", "ConsoleActiveTabBackgroundOpacity", app.ConsoleActiveTabBackgroundOpacity, 0, 100);
            ReadHotkeys(ini, app);

            result.Defaults = ReadLaunchProfile(ini, "Defaults", new LaunchProfile(), false);
            result.PowerShell7Path = ini.Get("PowerShell", "PowerShell7Path", string.Empty);
            result.Localization = ReadLocalization(ini);

            var identifiers = new HashSet<Guid>();
            foreach (var section in ini.SectionNames.Where(name => name.StartsWith("Script:", StringComparison.OrdinalIgnoreCase)))
            {
                Guid id;
                if (!Guid.TryParse(section.Substring("Script:".Length), out id) || id == Guid.Empty)
                {
                    throw new ConfigurationValidationException(section, "", "section suffix must be a non-empty GUID");
                }

                if (!identifiers.Add(id))
                {
                    throw new ConfigurationValidationException(section, "", "duplicate script identifier");
                }

                var script = new ScriptDefinition
                {
                    Id = id,
                    Name = Required(ini, section, "Name"),
                    Enabled = ReadBool(ini, section, "Enabled", true),
                    Path = Required(ini, section, "Path"),
                    Launch = ReadLaunchProfile(ini, section, result.Defaults, true)
                };

                result.Scripts.Add(script);
            }

            return result;
        }

        private static void ReadHotkeys(IniDocument ini, ApplicationSettings settings)
        {
            if (!ini.HasSection("Hotkeys")) return;
            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var defaults = HotkeySettings.CreateDefaultBinding(action);
                var binding = settings.Hotkeys[action];
                binding.Enabled = ReadBool(ini, "Hotkeys", action + "Enabled", defaults.Enabled);
                binding.Gesture = ini.Get("Hotkeys", action.ToString(), defaults.Gesture).Trim();
            }
        }

        private static LaunchProfile ReadLaunchProfile(IniDocument ini, string section, LaunchProfile fallback, bool includeAutoStart)
        {
            fallback = fallback ?? new LaunchProfile();
            var profile = fallback.Clone();
            profile.Interpreter = ReadEnum(ini, section, "Interpreter", fallback.Interpreter);
            profile.WindowMode = ReadEnum(ini, section, "WindowMode", fallback.WindowMode);
            profile.Arguments = ini.Get(section, "Arguments", fallback.Arguments ?? string.Empty);
            profile.WorkingDirectory = ini.Get(section, "WorkingDirectory", fallback.WorkingDirectory ?? string.Empty);
            profile.CaptureOutput = ReadBool(ini, section, "CaptureOutput", fallback.CaptureOutput);
            profile.OutputEncoding = ReadEnum(ini, section, "OutputEncoding", fallback.OutputEncoding);
            profile.WordWrap = ReadBool(ini, section, "WordWrap", fallback.WordWrap);
            profile.AllowParallelInstances = ReadBool(ini, section, "AllowParallelInstances", fallback.AllowParallelInstances);
            profile.StopPolicy = ReadEnum(ini, section, "StopPolicy", fallback.StopPolicy);
            profile.StopTimeoutSeconds = ReadInt(ini, section, "StopTimeoutSeconds", fallback.StopTimeoutSeconds, 0, 3600);

            if (includeAutoStart)
            {
                profile.AutoStartWithApplication = ReadBool(ini, section, "AutoStartWithApplication", false);
                profile.AutoStartOrder = ReadInt(ini, section, "AutoStartOrder", 100, int.MinValue, int.MaxValue);
                profile.AutoStartDelaySeconds = ReadInt(ini, section, "AutoStartDelaySeconds", 0, 0, 86400);
            }

            return profile;
        }

        private static IniDocument BuildIni(AppConfiguration configuration)
        {
            var ini = new IniDocument();
            var app = configuration.Application;
            ini.Set("Application", "ConfigVersion", CurrentVersion);
            ini.Set("Application", "Theme", app.Theme);
            ini.Set("Application", "CloseToTray", Bool(app.CloseToTray));
            ini.Set("Application", "StartMinimized", Bool(app.StartMinimized));
            ini.Set("Application", "StartWithWindows", Bool(app.StartWithWindows));
            ini.Set("Application", "StartHiddenWhenAutoStarted", Bool(app.StartHiddenWhenAutoStarted));
            ini.Set("Application", "AutoStartScripts", Bool(app.AutoStartScripts));
            ini.Set("Application", "ConfirmBeforeDelete", Bool(app.ConfirmBeforeDelete));
            ini.Set("Application", "MainWindowPlacementSaved", Bool(app.MainWindowPlacementSaved));
            ini.Set("Application", "MainWindowX", app.MainWindowX);
            ini.Set("Application", "MainWindowY", app.MainWindowY);
            ini.Set("Application", "MainWindowWidth", app.MainWindowWidth);
            ini.Set("Application", "MainWindowHeight", app.MainWindowHeight);
            ini.Set("Application", "MainWindowMaximized", Bool(app.MainWindowMaximized));
            ini.Set("Application", "EditorPath", app.EditorPath ?? string.Empty);
            ini.Set("Application", "EditorArguments", app.EditorArguments ?? string.Empty);
            ini.Set("Application", "LogLevel", app.LogLevel ?? "Information");
            ini.Set("Application", "LogRetentionDays", app.LogRetentionDays);
            ini.Set("Application", "LogScriptOutput", Bool(app.LogScriptOutput));
            ini.Set("Application", "ConsoleFontName", app.ConsoleFontName ?? "Consolas");
            ini.Set("Application", "ConsoleFontSize", app.ConsoleFontSize.ToString("0.##", CultureInfo.InvariantCulture));
            ini.Set("Application", "ConsolePaneHeight", app.ConsolePaneHeight);
            ini.Set("Application", "ConsoleBufferSizeKb", app.ConsoleBufferSizeKb);
            ini.Set("Application", "ConsoleAutoRecord", Bool(app.ConsoleAutoRecord));
            ini.Set("Application", "ConsoleLogMaxSizeMb", app.ConsoleLogMaxSizeMb);
            ini.Set("Application", "ConsoleForegroundColor", app.ConsoleForegroundColor ?? "#DCDCDC");
            ini.Set("Application", "ConsoleBackgroundColor", app.ConsoleBackgroundColor ?? "#1C1C1C");
            ini.Set("Application", "ConsoleBackgroundOpacity", app.ConsoleBackgroundOpacity);
            ini.Set("Application", "ConsoleTabForegroundColor", app.ConsoleTabForegroundColor ?? "#262B32");
            ini.Set("Application", "ConsoleActiveTabForegroundColor", app.ConsoleActiveTabForegroundColor ?? "#F5F7FA");
            ini.Set("Application", "ConsoleTabBackgroundColor", app.ConsoleTabBackgroundColor ?? "#FCFCFD");
            ini.Set("Application", "ConsoleTabBackgroundOpacity", app.ConsoleTabBackgroundOpacity);
            ini.Set("Application", "ConsoleActiveTabBackgroundColor", app.ConsoleActiveTabBackgroundColor ?? "#1C1C1C");
            ini.Set("Application", "ConsoleActiveTabBackgroundOpacity", app.ConsoleActiveTabBackgroundOpacity);

            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var binding = app.Hotkeys[action];
                ini.Set("Hotkeys", action + "Enabled", Bool(binding.Enabled));
                ini.Set("Hotkeys", action.ToString(), binding.Gesture ?? HotkeySettings.DefaultGesture(action));
            }

            WriteLaunchProfile(ini, "Defaults", configuration.Defaults, false);
            ini.Set("PowerShell", "PowerShell7Path", configuration.PowerShell7Path ?? string.Empty);

            var localization = configuration.Localization;
            if (localization == null || localization.Languages == null || localization.Languages.Count == 0)
            {
                localization = LocalizationDefaults.Create();
            }
            ini.Set("Localization", "Language", localization.Language ?? "ru");
            foreach (var language in localization.Languages.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var text in language.Value.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    ini.Set("Strings." + language.Key, text.Key, (text.Value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n"));
                }
            }

            foreach (var script in configuration.Scripts.OrderBy(item => item.Launch.AutoStartOrder).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var section = "Script:" + script.Id.ToString("D");
                ini.Set(section, "Name", script.Name);
                ini.Set(section, "Enabled", Bool(script.Enabled));
                ini.Set(section, "Path", script.Path);
                WriteLaunchProfile(ini, section, script.Launch, true);
            }

            return ini;
        }

        private static void WriteLaunchProfile(IniDocument ini, string section, LaunchProfile profile, bool includeAutoStart)
        {
            profile = profile ?? new LaunchProfile();
            ini.Set(section, "Interpreter", profile.Interpreter);
            ini.Set(section, "Arguments", profile.Arguments ?? string.Empty);
            ini.Set(section, "WorkingDirectory", profile.WorkingDirectory ?? string.Empty);
            ini.Set(section, "WindowMode", profile.WindowMode);
            ini.Set(section, "CaptureOutput", Bool(profile.CaptureOutput));
            ini.Set(section, "OutputEncoding", profile.OutputEncoding);
            ini.Set(section, "WordWrap", Bool(profile.WordWrap));
            ini.Set(section, "AllowParallelInstances", Bool(profile.AllowParallelInstances));
            if (includeAutoStart)
            {
                ini.Set(section, "AutoStartWithApplication", Bool(profile.AutoStartWithApplication));
                ini.Set(section, "AutoStartOrder", profile.AutoStartOrder);
                ini.Set(section, "AutoStartDelaySeconds", profile.AutoStartDelaySeconds);
            }

            ini.Set(section, "StopPolicy", profile.StopPolicy);
            ini.Set(section, "StopTimeoutSeconds", profile.StopTimeoutSeconds);
        }

        private static void ValidateConfiguration(AppConfiguration configuration)
        {
            if (configuration.Application == null || configuration.Application.Hotkeys == null ||
                configuration.Defaults == null || configuration.Localization == null || configuration.Scripts == null)
            {
                throw new ConfigurationValidationException("Application", "", "configuration object is incomplete");
            }

            if (configuration.Application.ConfigVersion != CurrentVersion)
            {
                throw new ConfigurationValidationException("Application", "ConfigVersion", "unsupported version");
            }

            if (!Enum.IsDefined(typeof(ApplicationTheme), configuration.Application.Theme))
            {
                throw new ConfigurationValidationException("Application", "Theme", "unsupported theme");
            }

            if (string.IsNullOrWhiteSpace(configuration.Application.EditorPath))
            {
                throw new ConfigurationValidationException("Application", "EditorPath", "value is required");
            }

            ValidateHotkeys(configuration.Application.Hotkeys);

            if (string.IsNullOrWhiteSpace(configuration.Application.ConsoleFontName) || configuration.Application.ConsoleFontSize < 6f || configuration.Application.ConsoleFontSize > 48f)
            {
                throw new ConfigurationValidationException("Application", "ConsoleFont", "font name and size from 6 to 48 are required");
            }

            if (configuration.Application.ConsolePaneHeight < 100 || configuration.Application.ConsolePaneHeight > 4000)
            {
                throw new ConfigurationValidationException("Application", "ConsolePaneHeight", "value from 100 to 4000 is required");
            }

            if (configuration.Application.ConsoleBufferSizeKb < 64 || configuration.Application.ConsoleBufferSizeKb > 1048576)
            {
                throw new ConfigurationValidationException("Application", "ConsoleBufferSizeKb", "value from 64 to 1048576 is required");
            }

            if (configuration.Application.ConsoleLogMaxSizeMb < 1 || configuration.Application.ConsoleLogMaxSizeMb > 4096)
            {
                throw new ConfigurationValidationException("Application", "ConsoleLogMaxSizeMb", "value from 1 to 4096 is required");
            }

            if (configuration.Application.MainWindowX < -100000 || configuration.Application.MainWindowX > 100000 ||
                configuration.Application.MainWindowY < -100000 || configuration.Application.MainWindowY > 100000)
            {
                throw new ConfigurationValidationException("Application", "MainWindowPosition", "coordinates from -100000 to 100000 are required");
            }

            if (configuration.Application.MainWindowWidth < 880 || configuration.Application.MainWindowWidth > 20000 ||
                configuration.Application.MainWindowHeight < 520 || configuration.Application.MainWindowHeight > 20000)
            {
                throw new ConfigurationValidationException("Application", "MainWindowSize", "width from 880 to 20000 and height from 520 to 20000 are required");
            }

            ValidateColor(configuration.Application.ConsoleForegroundColor, "ConsoleForegroundColor");
            ValidateColor(configuration.Application.ConsoleBackgroundColor, "ConsoleBackgroundColor");
            ValidateColor(configuration.Application.ConsoleTabForegroundColor, "ConsoleTabForegroundColor");
            ValidateColor(configuration.Application.ConsoleActiveTabForegroundColor, "ConsoleActiveTabForegroundColor");
            ValidateColor(configuration.Application.ConsoleTabBackgroundColor, "ConsoleTabBackgroundColor");
            ValidateColor(configuration.Application.ConsoleActiveTabBackgroundColor, "ConsoleActiveTabBackgroundColor");
            ValidateOpacity(configuration.Application.ConsoleBackgroundOpacity, "ConsoleBackgroundOpacity");
            ValidateOpacity(configuration.Application.ConsoleTabBackgroundOpacity, "ConsoleTabBackgroundOpacity");
            ValidateOpacity(configuration.Application.ConsoleActiveTabBackgroundOpacity, "ConsoleActiveTabBackgroundOpacity");

            Dictionary<string, string> selectedLanguage;
            if (string.IsNullOrWhiteSpace(configuration.Localization.Language) ||
                configuration.Localization.Languages == null ||
                !configuration.Localization.Languages.TryGetValue(configuration.Localization.Language, out selectedLanguage) ||
                selectedLanguage.Count == 0)
            {
                throw new ConfigurationValidationException("Localization", "Language", "selected language has no string table");
            }

            var ids = new HashSet<Guid>();
            foreach (var script in configuration.Scripts)
            {
                try
                {
                    ScriptDefinitionValidator.Validate(script, false);
                }
                catch (Exception exception)
                {
                    throw new ConfigurationValidationException("Script:" + script.Id.ToString("D"), "", exception.Message);
                }

                if (!ids.Add(script.Id))
                {
                    throw new ConfigurationValidationException("Script:" + script.Id.ToString("D"), "", "duplicate identifier");
                }
            }
        }

        private static void ValidateHotkeys(HotkeySettings settings)
        {
            var used = new Dictionary<HotkeyScope, Dictionary<ShowAppHotkeyGesture, HotkeyAction>>();
            foreach (HotkeyScope scope in Enum.GetValues(typeof(HotkeyScope)))
                used[scope] = new Dictionary<ShowAppHotkeyGesture, HotkeyAction>();

            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var binding = settings[action];
                if (binding == null || string.IsNullOrWhiteSpace(binding.Gesture))
                    throw new ConfigurationValidationException("Hotkeys", action.ToString(), "value is required");

                var scope = HotkeySettings.GetScope(action);
                ShowAppHotkeyGesture gesture;
                if (!ShowAppHotkeyGesture.TryParse(binding.Gesture, scope == HotkeyScope.Global, out gesture))
                {
                    throw new ConfigurationValidationException("Hotkeys", action.ToString(),
                        scope == HotkeyScope.Global
                            ? "expected a modifier and a supported key, for example Ctrl+Alt+M"
                            : "expected a supported key combination, for example F5 or Ctrl+N");
                }

                if (!binding.Enabled) continue;
                HotkeyAction duplicate;
                if (used[scope].TryGetValue(gesture, out duplicate))
                {
                    throw new ConfigurationValidationException("Hotkeys", action.ToString(),
                        "duplicates enabled hotkey " + duplicate);
                }
                used[scope][gesture] = action;
            }
        }

        private static void ValidateColor(string value, string key)
        {
            int parsed;
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#' ||
                !int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
            {
                throw new ConfigurationValidationException("Application", key, "color in #RRGGBB format is required");
            }
        }

        private static void ValidateOpacity(int value, string key)
        {
            if (value < 0 || value > 100)
            {
                throw new ConfigurationValidationException("Application", key, "opacity from 0 to 100 percent is required");
            }
        }

        private void WriteAtomically(string text)
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = ConfigPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, text, _utf8);
                if (File.Exists(ConfigPath))
                {
                    File.Replace(temporaryPath, ConfigPath, ConfigPath + ".bak", true);
                }
                else
                {
                    File.Move(temporaryPath, ConfigPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string Required(IniDocument ini, string section, string key)
        {
            var value = ini.Get(section, key, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationValidationException(section, key, "value is required");
            }

            return value;
        }

        private static bool ReadBool(IniDocument ini, string section, string key, bool defaultValue)
        {
            string raw;
            if (!ini.TryGet(section, key, out raw))
            {
                return defaultValue;
            }

            bool value;
            if (!bool.TryParse(raw, out value))
            {
                throw new ConfigurationValidationException(section, key, "expected true or false");
            }

            return value;
        }

        private static int ReadInt(IniDocument ini, string section, string key, int defaultValue, int minimum, int maximum)
        {
            string raw;
            if (!ini.TryGet(section, key, out raw))
            {
                return defaultValue;
            }

            int value;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum || value > maximum)
            {
                throw new ConfigurationValidationException(section, key, string.Format(CultureInfo.InvariantCulture, "expected integer from {0} to {1}", minimum, maximum));
            }

            return value;
        }

        private static float ReadFloat(IniDocument ini, string section, string key, float defaultValue, float minimum, float maximum)
        {
            string raw;
            if (!ini.TryGet(section, key, out raw))
            {
                return defaultValue;
            }

            float value;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < minimum || value > maximum)
            {
                throw new ConfigurationValidationException(section, key, string.Format(CultureInfo.InvariantCulture, "expected number from {0} to {1}", minimum, maximum));
            }

            return value;
        }

        private static LocalizationSettings ReadLocalization(IniDocument ini)
        {
            var localization = LocalizationDefaults.Create();
            localization.Language = ini.Get("Localization", "Language", localization.Language).Trim();
            foreach (var section in ini.SectionNames.Where(name => name.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase)))
            {
                var language = section.Substring("Strings.".Length).Trim();
                if (language.Length == 0)
                {
                    continue;
                }

                Dictionary<string, string> values;
                if (!localization.Languages.TryGetValue(language, out values))
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    localization.Languages[language] = values;
                }

                foreach (var pair in ini.GetSection(section))
                {
                    values[pair.Key] = pair.Value;
                }
            }

            return localization;
        }

        private static bool RequiresUpgrade(IniDocument ini, int loadedVersion)
        {
            if (loadedVersion != CurrentVersion || !ini.HasSection("Localization"))
            {
                return true;
            }

            var defaults = LocalizationDefaults.Create();
            foreach (var language in defaults.Languages)
            {
                foreach (var pair in language.Value)
                {
                    string ignored;
                    if (!ini.TryGet("Strings." + language.Key, pair.Key, out ignored))
                    {
                        return true;
                    }
                }
            }

            string deprecated;
            if (ini.TryGet("Strings.ru", "Settings.Warning", out deprecated) ||
                ini.TryGet("Strings.en", "Settings.Warning", out deprecated) ||
                ini.TryGet("Strings.ru", "Settings.ShowAppHotkey", out deprecated) ||
                ini.TryGet("Strings.en", "Settings.ShowAppHotkey", out deprecated))
            {
                return true;
            }

            string previousBuildLabel;
            if ((ini.TryGet("Strings.en", "About.Build", out previousBuildLabel) &&
                    string.Equals(previousBuildLabel, "Build: {0}", StringComparison.Ordinal)) ||
                (ini.TryGet("Strings.ru", "About.Build", out previousBuildLabel) &&
                    string.Equals(previousBuildLabel, "Сборка: {0}", StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
        }

        private static void UpgradeConfiguration(AppConfiguration configuration, int loadedVersion)
        {
            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var binding = configuration.Application.Hotkeys[action];
                if (string.IsNullOrWhiteSpace(binding.Gesture))
                    binding.Gesture = HotkeySettings.DefaultGesture(action);
            }
            if (configuration.Localization?.Languages == null) return;
            foreach (var language in configuration.Localization.Languages.Values)
            {
                language.Remove("Settings.Warning");
                language.Remove("Settings.ShowAppHotkey");
                language.Remove("Settings.ShowAppHotkeyClear");
                language.Remove("Settings.ShowAppHotkeyHint");
                language.Remove("Settings.ShowAppHotkeyRequired");
                language.Remove("Settings.ShowAppHotkeyUnavailable");
                language.Remove("Tray.ShowAppHotkeyFailed");
            }
            ReplaceUnmodifiedString(configuration.Localization, "en", "About.Build",
                "Build: {0}", "Built on: {0}");
            ReplaceUnmodifiedString(configuration.Localization, "ru", "About.Build",
                "Сборка: {0}", "Собрано: {0}");
            if (loadedVersion < 3)
            {
                ReplaceUnmodifiedString(configuration.Localization, "ru", "Script.Encoding.Auto",
                    "Авто (OEM Windows)", "Авто (UTF-8/OEM Windows)");
                ReplaceUnmodifiedString(configuration.Localization, "en", "Script.Encoding.Auto",
                    "Auto (Windows OEM)", "Auto (UTF-8/Windows OEM)");
            }
            if (loadedVersion < 4)
            {
                ReplaceUnmodifiedString(configuration.Localization, "ru", "Script.Encoding.Auto",
                    "Авто (UTF-8/OEM Windows)", "Авто (UTF-8/Windows-1251/OEM)");
                ReplaceUnmodifiedString(configuration.Localization, "en", "Script.Encoding.Auto",
                    "Auto (UTF-8/Windows OEM)", "Auto (UTF-8/Windows-1251/OEM)");
                ReplaceUnmodifiedString(configuration.Localization, "ru", "Script.AutoStartOrder",
                    "Порядок автозапуска", "Порядок");
                ReplaceUnmodifiedString(configuration.Localization, "en", "Script.AutoStartOrder",
                    "Auto-start order", "Order");
            }
            if (loadedVersion < 6)
            {
                ReplaceBrandDefaults(configuration.Localization);
            }
        }

        private static void ReplaceBrandDefaults(LocalizationSettings localization)
        {
            ReplaceUnmodifiedString(localization, "ru", "Console.DetachedTitle",
                "{0} [{1}] — CmdsManager", "{0} [{1}] — {2}");
            ReplaceUnmodifiedString(localization, "ru", "Script.AutoStart",
                "Запускать при старте CmdsManager", "Запускать при старте Cmds Manager");
            ReplaceUnmodifiedString(localization, "ru", "Settings.Title",
                "Настройки CmdsManager", "Настройки Cmds Manager");
            ReplaceUnmodifiedString(localization, "ru", "Settings.StartWithWindows",
                "Запускать CmdsManager при входе в Windows", "Запускать Cmds Manager при входе в Windows");
            ReplaceUnmodifiedString(localization, "ru", "Tray.ExitTitle",
                "Выход из CmdsManager", "Выход из Cmds Manager");
            ReplaceUnmodifiedString(localization, "ru", "Tray.ExitConfirm",
                "Все запущенные через CmdsManager скрипты будут остановлены. Выйти?",
                "Все запущенные через Cmds Manager скрипты будут остановлены. Выйти?");
            ReplaceUnmodifiedString(localization, "ru", "Tray.Exiting",
                "CmdsManager — завершение", "Cmds Manager — завершение");
            ReplaceUnmodifiedString(localization, "ru", "App.UiErrorTitle",
                "Ошибка CmdsManager", "Ошибка Cmds Manager");

            ReplaceUnmodifiedString(localization, "en", "Console.DetachedTitle",
                "{0} [{1}] — CmdsManager", "{0} [{1}] — {2}");
            ReplaceUnmodifiedString(localization, "en", "Script.AutoStart",
                "Start with CmdsManager", "Start with Cmds Manager");
            ReplaceUnmodifiedString(localization, "en", "Settings.Title",
                "CmdsManager settings", "Cmds Manager settings");
            ReplaceUnmodifiedString(localization, "en", "Settings.StartWithWindows",
                "Start CmdsManager when signing in to Windows", "Start Cmds Manager when signing in to Windows");
            ReplaceUnmodifiedString(localization, "en", "Tray.ExitTitle",
                "Exit CmdsManager", "Exit Cmds Manager");
            ReplaceUnmodifiedString(localization, "en", "Tray.ExitConfirm",
                "All scripts started by CmdsManager will be stopped. Exit?",
                "All scripts started by Cmds Manager will be stopped. Exit?");
            ReplaceUnmodifiedString(localization, "en", "Tray.Exiting",
                "CmdsManager — exiting", "Cmds Manager — exiting");
            ReplaceUnmodifiedString(localization, "en", "App.UiErrorTitle",
                "CmdsManager error", "Cmds Manager error");
        }

        private static void ReplaceUnmodifiedString(LocalizationSettings localization, string language, string key,
            string previousDefault, string currentDefault)
        {
            Dictionary<string, string> values;
            string value;
            if (localization.Languages.TryGetValue(language, out values) &&
                values.TryGetValue(key, out value) && string.Equals(value, previousDefault, StringComparison.Ordinal))
            {
                values[key] = currentDefault;
            }
        }

        private static T ReadEnum<T>(IniDocument ini, string section, string key, T defaultValue) where T : struct
        {
            string raw;
            if (!ini.TryGet(section, key, out raw))
            {
                return defaultValue;
            }

            T value;
            if (!Enum.TryParse(raw, true, out value) || !Enum.IsDefined(typeof(T), value))
            {
                throw new ConfigurationValidationException(section, key, "unsupported value: " + raw);
            }

            return value;
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static byte[] ComputeHash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return sha.ComputeHash(stream);
            }
        }

        private static bool HashesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
    }
}
