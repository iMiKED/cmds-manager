using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CmdsManager.Infrastructure.Configuration
{
    public sealed class IniFormatException : FormatException
    {
        public IniFormatException(string message, int lineNumber)
            : base(string.Format(CultureInfo.InvariantCulture, "INI line {0}: {1}", lineNumber, message))
        {
            LineNumber = lineNumber;
        }

        public int LineNumber { get; }
    }

    public sealed class IniDocument
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> SectionNames => _sections.Keys;

        public static IniDocument Parse(string text)
        {
            var document = new IniDocument();
            var currentSection = string.Empty;
            using (var reader = new StringReader(text ?? string.Empty))
            {
                string rawLine;
                var lineNumber = 0;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line[0] == '[')
                    {
                        if (line[line.Length - 1] != ']')
                        {
                            throw new IniFormatException("section is missing closing bracket", lineNumber);
                        }

                        currentSection = line.Substring(1, line.Length - 2).Trim();
                        if (currentSection.Length == 0)
                        {
                            throw new IniFormatException("section name is empty", lineNumber);
                        }

                        document.EnsureSection(currentSection);
                        continue;
                    }

                    var equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        throw new IniFormatException("expected key=value", lineNumber);
                    }

                    if (currentSection.Length == 0)
                    {
                        throw new IniFormatException("key is outside a section", lineNumber);
                    }

                    var key = line.Substring(0, equalsIndex).Trim();
                    var value = line.Substring(equalsIndex + 1).Trim();
                    if (key.Length == 0)
                    {
                        throw new IniFormatException("key is empty", lineNumber);
                    }

                    document.Set(currentSection, key, value);
                }
            }

            return document;
        }

        public bool HasSection(string section)
        {
            return _sections.ContainsKey(section);
        }

        public string Get(string section, string key, string defaultValue = "")
        {
            Dictionary<string, string> values;
            string value;
            return _sections.TryGetValue(section, out values) && values.TryGetValue(key, out value)
                ? value
                : defaultValue;
        }

        public bool TryGet(string section, string key, out string value)
        {
            Dictionary<string, string> values;
            if (_sections.TryGetValue(section, out values) && values.TryGetValue(key, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        public void Set(string section, string key, object value)
        {
            ValidateToken(section, nameof(section));
            ValidateToken(key, nameof(key));
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                throw new ArgumentException("INI values cannot contain new lines.", nameof(value));
            }

            EnsureSection(section)[key] = text;
        }

        public string Serialize()
        {
            var builder = new StringBuilder();
            var first = true;
            foreach (var section in _sections)
            {
                if (!first)
                {
                    builder.AppendLine();
                }

                first = false;
                builder.Append('[').Append(section.Key).AppendLine("]");
                foreach (var pair in section.Value)
                {
                    builder.Append(pair.Key).Append('=').AppendLine(pair.Value ?? string.Empty);
                }
            }

            return builder.ToString();
        }

        private Dictionary<string, string> EnsureSection(string section)
        {
            Dictionary<string, string> values;
            if (!_sections.TryGetValue(section, out values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections.Add(section, values);
            }

            return values;
        }

        private static void ValidateToken(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\r', '\n', '[', ']' }) >= 0)
            {
                throw new ArgumentException("Invalid INI token.", parameterName);
            }
        }
    }
}
