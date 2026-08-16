using System;
using System.Globalization;
using System.IO;
using System.Text;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Windows;

namespace CmdsManager.Infrastructure.Execution
{
    internal sealed class RawOutputLine
    {
        internal RawOutputLine(byte[] bytes, string text)
        {
            Bytes = bytes ?? new byte[0];
            Text = text ?? string.Empty;
        }

        internal byte[] Bytes { get; }
        internal string Text { get; }
    }

    internal sealed class AdaptiveEncodingTextReader : TextReader
    {
        private readonly Stream _stream;
        private readonly ScriptOutputEncoding _outputEncoding;
        private readonly byte[] _readBuffer = new byte[4096];
        private byte[] _lineBuffer = new byte[256];
        private int _readOffset;
        private int _readCount;
        private bool _disposed;

        internal AdaptiveEncodingTextReader(Stream stream, ScriptOutputEncoding outputEncoding)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _outputEncoding = outputEncoding;
        }

        internal RawOutputLine ReadOutputLine()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AdaptiveEncodingTextReader));

            var bytes = _outputEncoding == ScriptOutputEncoding.Utf16LittleEndian
                ? ReadUtf16Line()
                : ReadSingleByteLine();
            return bytes == null ? null : new RawOutputLine(bytes, OutputEncodingDecoder.Decode(bytes, _outputEncoding));
        }

        public override string ReadLine()
        {
            var line = ReadOutputLine();
            return line?.Text;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) _stream.Dispose();
            _disposed = true;
            base.Dispose(disposing);
        }

        private byte[] ReadSingleByteLine()
        {
            var length = 0;
            while (true)
            {
                var next = ReadByte();
                if (next < 0)
                {
                    if (length == 0) return null;
                    break;
                }

                if (next == '\n') break;
                AppendByte(ref length, (byte)next);
            }

            if (length > 0 && _lineBuffer[length - 1] == '\r') length--;
            return CopyLine(length);
        }

        private byte[] ReadUtf16Line()
        {
            var length = 0;
            while (true)
            {
                var first = ReadByte();
                if (first < 0)
                {
                    if (length == 0) return null;
                    break;
                }

                var second = ReadByte();
                if (second < 0)
                {
                    AppendByte(ref length, (byte)first);
                    break;
                }

                if (first == '\n' && second == 0) break;
                AppendByte(ref length, (byte)first);
                AppendByte(ref length, (byte)second);
            }

            if (length >= 2 && _lineBuffer[length - 2] == '\r' && _lineBuffer[length - 1] == 0) length -= 2;
            return CopyLine(length);
        }

        private void AppendByte(ref int length, byte value)
        {
            EnsureLineCapacity(length + 1);
            _lineBuffer[length++] = value;
        }

        private byte[] CopyLine(int length)
        {
            var result = new byte[length];
            if (length > 0) Buffer.BlockCopy(_lineBuffer, 0, result, 0, length);
            return result;
        }

        private int ReadByte()
        {
            if (_readOffset >= _readCount)
            {
                _readCount = _stream.Read(_readBuffer, 0, _readBuffer.Length);
                _readOffset = 0;
                if (_readCount == 0) return -1;
            }

            return _readBuffer[_readOffset++];
        }

        private void EnsureLineCapacity(int required)
        {
            if (required <= _lineBuffer.Length) return;
            var replacement = new byte[Math.Max(required, _lineBuffer.Length * 2)];
            Buffer.BlockCopy(_lineBuffer, 0, replacement, 0, _lineBuffer.Length);
            _lineBuffer = replacement;
        }
    }

    public static class OutputEncodingDecoder
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Encoding Utf8 = new UTF8Encoding(false, false);
        private static readonly Encoding Windows1251 = Encoding.GetEncoding(1251);
        private static readonly Encoding Oem = CreateOemEncoding();
        private static readonly Encoding RussianOem866 = Encoding.GetEncoding(866);

        public static string Decode(byte[] bytes, ScriptOutputEncoding outputEncoding)
        {
            bytes = bytes ?? new byte[0];
            string result;
            switch (outputEncoding)
            {
                case ScriptOutputEncoding.Utf8:
                    result = Utf8.GetString(bytes);
                    break;
                case ScriptOutputEncoding.Oem:
                    result = Oem.GetString(bytes);
                    break;
                case ScriptOutputEncoding.Windows1251:
                    result = Windows1251.GetString(bytes);
                    break;
                case ScriptOutputEncoding.Utf16LittleEndian:
                    result = Encoding.Unicode.GetString(bytes);
                    break;
                default:
                    result = DecodeAutomatically(bytes);
                    break;
            }

            return result.Length > 0 && result[0] == '\uFEFF' ? result.Substring(1) : result;
        }

        private static string DecodeAutomatically(byte[] bytes)
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                var result = Oem.GetString(bytes);
                var resultScore = ScoreCyrillicText(result);
                var russianOem = RussianOem866.GetString(bytes);
                var russianOemScore = ScoreCyrillicText(russianOem);
                if (russianOemScore > resultScore)
                {
                    result = russianOem;
                    resultScore = russianOemScore;
                }

                var windows = Windows1251.GetString(bytes);
                return ScoreCyrillicText(windows) > resultScore ? windows : result;
            }
        }

        private static int ScoreCyrillicText(string value)
        {
            var score = 0;
            foreach (var character in value ?? string.Empty)
            {
                if ((character >= '\u0410' && character <= '\u044F') || character == '\u0401' || character == '\u0451')
                {
                    score += 2;
                    continue;
                }

                if (character >= '\u2500' && character <= '\u259F')
                {
                    score -= 10;
                    continue;
                }

                if ((character >= '\u0400' && character <= '\u052F') ||
                    CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Control)
                {
                    score -= 4;
                    continue;
                }

                if (character == '\uFFFD') score -= 12;
            }

            return score;
        }

        private static Encoding CreateOemEncoding()
        {
            try { return Encoding.GetEncoding((int)NativeMethods.GetOEMCP()); }
            catch (ArgumentException) { return Encoding.Default; }
        }
    }
}
