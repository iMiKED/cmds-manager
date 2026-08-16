using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CmdsManager.Presentation
{
    internal static class ApplicationResources
    {
        internal const string DisplayName = "Cmds Manager";
        private const string IconResourceName = "CmdsManager.Assets.CmdsManager.ico";
        private static readonly Lazy<Icon> IconHolder = new Lazy<Icon>(LoadIcon);

        internal static Icon Icon => IconHolder.Value;
        internal static string Version => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "-";
        internal static string WindowTitle => DisplayName + " " + Version;

        internal static Bitmap CreateIconBitmap(int size)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            using (var stream = OpenIconStream())
            {
                if (stream == null)
                {
                    using (var fallback = new Icon(SystemIcons.Application, size, size)) return fallback.ToBitmap();
                }
                using (var icon = new Icon(stream, new Size(size, size))) return icon.ToBitmap();
            }
        }

        private static Icon LoadIcon()
        {
            using (var stream = OpenIconStream())
            {
                if (stream == null) return SystemIcons.Application;
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }

        private static Stream OpenIconStream()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName);
        }
    }
}
