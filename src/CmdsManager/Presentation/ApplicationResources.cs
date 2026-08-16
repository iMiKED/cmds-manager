using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

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
        internal static string BuildTimestamp => Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "BuildDateTime", StringComparison.Ordinal))?.Value
            ?? "-";
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
                var pngFrame = TryLoadPngIconFrame(stream, size);
                if (pngFrame != null) return pngFrame;
                stream.Position = 0;
                using (var icon = new Icon(stream, new Size(size, size))) return icon.ToBitmap();
            }
        }

        private static Bitmap TryLoadPngIconFrame(Stream stream, int requestedSize)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek) return null;
            stream.Position = 0;
            uint bestLength = 0;
            uint bestOffset = 0;
            var bestDistance = int.MaxValue;
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (stream.Length < 6 || reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) return null;
                var count = reader.ReadUInt16();
                if (count == 0 || stream.Length < 6L + count * 16L) return null;
                for (var index = 0; index < count; index++)
                {
                    var widthByte = reader.ReadByte();
                    var heightByte = reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadByte();
                    reader.ReadUInt16();
                    reader.ReadUInt16();
                    var length = reader.ReadUInt32();
                    var offset = reader.ReadUInt32();
                    var width = widthByte == 0 ? 256 : widthByte;
                    var height = heightByte == 0 ? 256 : heightByte;
                    var distance = Math.Abs(width - requestedSize) + Math.Abs(height - requestedSize);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestLength = length;
                    bestOffset = offset;
                }

                if (bestLength < 8 || bestLength > int.MaxValue ||
                    bestOffset > stream.Length || bestLength > stream.Length - bestOffset) return null;
                stream.Position = bestOffset;
                var imageBytes = reader.ReadBytes((int)bestLength);
                if (imageBytes.Length != (int)bestLength ||
                    imageBytes[0] != 0x89 || imageBytes[1] != 0x50 || imageBytes[2] != 0x4E ||
                    imageBytes[3] != 0x47 || imageBytes[4] != 0x0D || imageBytes[5] != 0x0A ||
                    imageBytes[6] != 0x1A || imageBytes[7] != 0x0A) return null;
                using (var png = new MemoryStream(imageBytes, false))
                using (var image = Image.FromStream(png, true, true))
                    return new Bitmap(image);
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
