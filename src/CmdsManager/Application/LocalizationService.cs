using System;
using System.Collections.Generic;
using System.Globalization;

namespace CmdsManager.Application
{
    public sealed class LocalizationService
    {
        private readonly ConfigurationState _state;

        public LocalizationService(ConfigurationState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _state.Changed += (sender, args) => Changed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Changed;

        public string CurrentLanguage => _state.Current.Localization?.Language ?? "ru";

        public IEnumerable<string> LanguageCodes
        {
            get
            {
                var localization = _state.Current.Localization;
                if (localization == null || localization.Languages == null)
                {
                    return new string[0];
                }

                return localization.Languages.Keys;
            }
        }

        public string this[string key] => Get(key);

        public string Get(string key, params object[] arguments)
        {
            return GetForLanguage(CurrentLanguage, key, arguments);
        }

        public string GetForLanguage(string language, string key, params object[] arguments)
        {
            var value = Find(language, key) ?? Find("en", key) ?? Find("ru", key) ?? key ?? string.Empty;
            value = value.Replace("\\n", Environment.NewLine);
            if (arguments == null || arguments.Length == 0)
            {
                return value;
            }

            try
            {
                var culture = language != null && language.Equals("ru", StringComparison.OrdinalIgnoreCase)
                    ? CultureInfo.GetCultureInfo("ru-RU")
                    : CultureInfo.GetCultureInfo("en-US");
                return string.Format(culture, value, arguments);
            }
            catch (FormatException)
            {
                return value;
            }
        }

        private string Find(string language, string key)
        {
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            Dictionary<string, string> values;
            string value;
            var languages = _state.Current.Localization?.Languages;
            return languages != null && languages.TryGetValue(language, out values) && values.TryGetValue(key, out value)
                ? value
                : null;
        }
    }
}
