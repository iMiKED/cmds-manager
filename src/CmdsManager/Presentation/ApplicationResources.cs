using System;
using System.Drawing;
using System.Reflection;

namespace CmdsManager.Presentation
{
    internal static class ApplicationResources
    {
        private const string IconResourceName = "CmdsManager.Assets.CmdsManager.ico";
        private static readonly Lazy<Icon> IconHolder = new Lazy<Icon>(LoadIcon);

        internal static Icon Icon => IconHolder.Value;

        private static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
            {
                if (stream == null) return SystemIcons.Application;
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }
    }
}
