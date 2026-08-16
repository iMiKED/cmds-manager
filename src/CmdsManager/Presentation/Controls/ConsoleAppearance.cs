using System;
using System.Drawing;
using System.Globalization;

namespace CmdsManager.Presentation.Controls
{
    internal static class ConsoleAppearance
    {
        internal static Color ParseColor(string value, Color fallback)
        {
            int rgb;
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[0] != '#' ||
                !int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
            {
                return fallback;
            }

            return Color.FromArgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        }

        internal static string ToHex(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        internal static Color WithOpacity(Color color, int opacityPercent)
        {
            var alpha = checked((int)Math.Round(Math.Max(0, Math.Min(100, opacityPercent)) * 2.55));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        internal static Color Composite(Color foreground, Color background, int opacityPercent)
        {
            var opacity = Math.Max(0, Math.Min(100, opacityPercent)) / 100d;
            return Color.FromArgb(
                Blend(foreground.R, background.R, opacity),
                Blend(foreground.G, background.G, opacity),
                Blend(foreground.B, background.B, opacity));
        }

        private static int Blend(byte foreground, byte background, double opacity)
        {
            return checked((int)Math.Round(foreground * opacity + background * (1d - opacity)));
        }
    }
}
