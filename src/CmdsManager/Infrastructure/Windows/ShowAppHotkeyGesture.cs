using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CmdsManager.Infrastructure.Windows
{
    [Flags]
    public enum ShowAppHotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008
    }

    public sealed class ShowAppHotkeyGesture : IEquatable<ShowAppHotkeyGesture>
    {
        private static readonly Dictionary<string, Keys> NamesToKeys = BuildNamesToKeys();
        private static readonly Dictionary<Keys, string> KeysToNames =
            NamesToKeys.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => PreferredName(group));

        private ShowAppHotkeyGesture(ShowAppHotkeyModifiers modifiers, Keys keyCode)
        {
            Modifiers = modifiers;
            KeyCode = keyCode;
        }

        public ShowAppHotkeyModifiers Modifiers { get; }
        public Keys KeyCode { get; }
        public uint VirtualKey => (uint)KeyCode;

        public static bool TryCreate(Keys keyCode, ShowAppHotkeyModifiers modifiers, out ShowAppHotkeyGesture gesture)
        {
            return TryCreate(keyCode, modifiers, true, out gesture);
        }

        public static bool TryCreate(Keys keyCode, ShowAppHotkeyModifiers modifiers, bool requireModifier,
            out ShowAppHotkeyGesture gesture)
        {
            gesture = null;
            keyCode &= Keys.KeyCode;
            if ((requireModifier && modifiers == ShowAppHotkeyModifiers.None) ||
                (modifiers & ~(ShowAppHotkeyModifiers.Alt | ShowAppHotkeyModifiers.Control |
                    ShowAppHotkeyModifiers.Shift | ShowAppHotkeyModifiers.Win)) != 0 ||
                !KeysToNames.ContainsKey(keyCode))
            {
                return false;
            }

            gesture = new ShowAppHotkeyGesture(modifiers, keyCode);
            return true;
        }

        public static bool TryParse(string value, out ShowAppHotkeyGesture gesture)
        {
            return TryParse(value, true, out gesture);
        }

        public static bool TryParse(string value, bool requireModifier, out ShowAppHotkeyGesture gesture)
        {
            gesture = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;

            var modifiers = ShowAppHotkeyModifiers.None;
            Keys keyCode = Keys.None;
            foreach (var rawToken in value.Split(new[] { '+' }, StringSplitOptions.None))
            {
                var token = rawToken.Trim();
                if (token.Length == 0) return false;
                ShowAppHotkeyModifiers modifier;
                if (TryParseModifier(token, out modifier))
                {
                    if ((modifiers & modifier) != 0) return false;
                    modifiers |= modifier;
                    continue;
                }

                Keys parsedKey;
                if (keyCode != Keys.None || !NamesToKeys.TryGetValue(token, out parsedKey)) return false;
                keyCode = parsedKey;
            }

            return TryCreate(keyCode, modifiers, requireModifier, out gesture);
        }

        public bool Matches(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) != KeyCode) return false;
            var actual = ShowAppHotkeyModifiers.None;
            if ((keyData & Keys.Control) == Keys.Control) actual |= ShowAppHotkeyModifiers.Control;
            if ((keyData & Keys.Alt) == Keys.Alt) actual |= ShowAppHotkeyModifiers.Alt;
            if ((keyData & Keys.Shift) == Keys.Shift) actual |= ShowAppHotkeyModifiers.Shift;
            if ((NativeMethods.GetKeyState((int)Keys.LWin) & 0x8000) != 0 ||
                (NativeMethods.GetKeyState((int)Keys.RWin) & 0x8000) != 0)
                actual |= ShowAppHotkeyModifiers.Win;
            return actual == Modifiers;
        }

        public bool Equals(ShowAppHotkeyGesture other)
        {
            return other != null && other.Modifiers == Modifiers && other.KeyCode == KeyCode;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ShowAppHotkeyGesture);
        }

        public override int GetHashCode()
        {
            return ((int)Modifiers * 397) ^ (int)KeyCode;
        }

        public override string ToString()
        {
            var parts = new List<string>(5);
            if ((Modifiers & ShowAppHotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((Modifiers & ShowAppHotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & ShowAppHotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((Modifiers & ShowAppHotkeyModifiers.Win) != 0) parts.Add("Win");
            parts.Add(KeysToNames[KeyCode]);
            return string.Join("+", parts);
        }

        private static bool TryParseModifier(string value, out ShowAppHotkeyModifiers modifier)
        {
            if (value.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ShowAppHotkeyModifiers.Control;
                return true;
            }
            if (value.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ShowAppHotkeyModifiers.Alt;
                return true;
            }
            if (value.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ShowAppHotkeyModifiers.Shift;
                return true;
            }
            if (value.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ShowAppHotkeyModifiers.Win;
                return true;
            }

            modifier = ShowAppHotkeyModifiers.None;
            return false;
        }

        private static Dictionary<string, Keys> BuildNamesToKeys()
        {
            var result = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase);
            for (var character = 'A'; character <= 'Z'; character++)
                result[character.ToString()] = (Keys)character;
            for (var digit = 0; digit <= 9; digit++)
                result[digit.ToString()] = (Keys)((int)Keys.D0 + digit);
            for (var number = 1; number <= 24; number++)
                result["F" + number] = (Keys)((int)Keys.F1 + number - 1);

            Add(result, "Space", Keys.Space);
            Add(result, "Enter", Keys.Enter);
            Add(result, "Tab", Keys.Tab);
            Add(result, "Backspace", Keys.Back);
            Add(result, "Delete", Keys.Delete);
            Add(result, "Escape", Keys.Escape);
            Add(result, "Insert", Keys.Insert);
            Add(result, "Home", Keys.Home);
            Add(result, "End", Keys.End);
            Add(result, "PageUp", Keys.PageUp);
            Add(result, "PageDown", Keys.PageDown);
            Add(result, "Up", Keys.Up);
            Add(result, "Down", Keys.Down);
            Add(result, "Left", Keys.Left);
            Add(result, "Right", Keys.Right);
            Add(result, "Pause", Keys.Pause);
            Add(result, "PrintScreen", Keys.PrintScreen);
            Add(result, "ScrollLock", Keys.Scroll);
            Add(result, "Comma", Keys.Oemcomma);
            Add(result, "Period", Keys.OemPeriod);
            Add(result, "Minus", Keys.OemMinus);
            Add(result, "Plus", Keys.Oemplus);
            Add(result, "Num0", Keys.NumPad0);
            Add(result, "Num1", Keys.NumPad1);
            Add(result, "Num2", Keys.NumPad2);
            Add(result, "Num3", Keys.NumPad3);
            Add(result, "Num4", Keys.NumPad4);
            Add(result, "Num5", Keys.NumPad5);
            Add(result, "Num6", Keys.NumPad6);
            Add(result, "Num7", Keys.NumPad7);
            Add(result, "Num8", Keys.NumPad8);
            Add(result, "Num9", Keys.NumPad9);
            Add(result, "Multiply", Keys.Multiply);
            Add(result, "Add", Keys.Add);
            Add(result, "Subtract", Keys.Subtract);
            Add(result, "Decimal", Keys.Decimal);
            Add(result, "Divide", Keys.Divide);
            return result;
        }

        private static void Add(IDictionary<string, Keys> target, string name, Keys key)
        {
            target[name] = key;
        }

        private static string PreferredName(IGrouping<Keys, KeyValuePair<string, Keys>> group)
        {
            return group.Select(pair => pair.Key)
                .OrderBy(name => name.Length)
                .ThenBy(name => name, StringComparer.Ordinal)
                .First();
        }
    }
}
