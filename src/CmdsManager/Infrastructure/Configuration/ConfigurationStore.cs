using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CmdsManager.Domain;

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
        private const int CurrentVersion = 2;
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
            var requiresUpgrade = RequiresUpgrade(ini, configuration.Application.ConfigVersion);
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
            app.CloseToTray = ReadBool(ini, "Application", "CloseToTray", true);
            app.StartMinimized = ReadBool(ini, "Application", "StartMinimized", false);
            app.StartWithWindows = ReadBool(ini, "Application", "StartWithWindows", false);
            app.StartHiddenWhenAutoStarted = ReadBool(ini, "Application", "StartHiddenWhenAutoStarted", true);
            app.AutoStartScripts = ReadBool(ini, "Application", "AutoStartScripts", true);
            app.ConfirmBeforeDelete = ReadBool(ini, "Application", "ConfirmBeforeDelete", true);
            app.EditorPath = ini.Get("Application", "EditorPath", app.EditorPath);
            app.EditorArguments = ini.Get("Application", "EditorArguments", app.EditorArguments);
            app.LogLevel = ini.Get("Application", "LogLevel", app.LogLevel);
            app.LogRetentionDays = ReadInt(ini, "Application", "LogRetentionDays", 14, 1, 3650);
            app.LogScriptOutput = ReadBool(ini, "Application", "LogScriptOutput", false);
            app.ConsoleFontName = ini.Get("Application", "ConsoleFontName", app.ConsoleFontName);
            app.ConsoleFontSize = ReadFloat(ini, "Application", "ConsoleFontSize", app.ConsoleFontSize, 6f, 48f);

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
            ini.Set("Application", "CloseToTray", Bool(app.CloseToTray));
            ini.Set("Application", "StartMinimized", Bool(app.StartMinimized));
            ini.Set("Application", "StartWithWindows", Bool(app.StartWithWindows));
            ini.Set("Application", "StartHiddenWhenAutoStarted", Bool(app.StartHiddenWhenAutoStarted));
            ini.Set("Application", "AutoStartScripts", Bool(app.AutoStartScripts));
            ini.Set("Application", "ConfirmBeforeDelete", Bool(app.ConfirmBeforeDelete));
            ini.Set("Application", "EditorPath", app.EditorPath ?? string.Empty);
            ini.Set("Application", "EditorArguments", app.EditorArguments ?? string.Empty);
            ini.Set("Application", "LogLevel", app.LogLevel ?? "Information");
            ini.Set("Application", "LogRetentionDays", app.LogRetentionDays);
            ini.Set("Application", "LogScriptOutput", Bool(app.LogScriptOutput));
            ini.Set("Application", "ConsoleFontName", app.ConsoleFontName ?? "Consolas");
            ini.Set("Application", "ConsoleFontSize", app.ConsoleFontSize.ToString("0.##", CultureInfo.InvariantCulture));

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
            if (configuration.Application == null || configuration.Defaults == null || configuration.Localization == null || configuration.Scripts == null)
            {
                throw new ConfigurationValidationException("Application", "", "configuration object is incomplete");
            }

            if (configuration.Application.ConfigVersion != CurrentVersion)
            {
                throw new ConfigurationValidationException("Application", "ConfigVersion", "unsupported version");
            }

            if (string.IsNullOrWhiteSpace(configuration.Application.EditorPath))
            {
                throw new ConfigurationValidationException("Application", "EditorPath", "value is required");
            }

            if (string.IsNullOrWhiteSpace(configuration.Application.ConsoleFontName) || configuration.Application.ConsoleFontSize < 6f || configuration.Application.ConsoleFontSize > 48f)
            {
                throw new ConfigurationValidationException("Application", "ConsoleFont", "font name and size from 6 to 48 are required");
            }

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

            return false;
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
