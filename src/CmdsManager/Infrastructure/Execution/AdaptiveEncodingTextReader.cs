using System;
using System.IO;
using System.Text;

namespace CmdsManager.Infrastructure.Execution
{
    internal sealed class AdaptiveEncodingTextReader : TextReader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly Stream _stream;
        private readonly Encoding _fallbackEncoding;
        private readonly byte[] _readBuffer = new byte[4096];
        private byte[] _lineBuffer = new byte[256];
        private int _readOffset;
        private int _readCount;
        private bool _firstLine = true;
        private bool _disposed;

        internal AdaptiveEncodingTextReader(Stream stream, Encoding fallbackEncoding)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _fallbackEncoding = fallbackEncoding ?? throw new ArgumentNullException(nameof(fallbackEncoding));
        }

        public override string ReadLine()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AdaptiveEncodingTextReader));

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
                EnsureLineCapacity(length + 1);
                _lineBuffer[length++] = (byte)next;
            }

            if (length > 0 && _lineBuffer[length - 1] == '\r') length--;
            var offset = 0;
            if (_firstLine && length >= 3 && _lineBuffer[0] == 0xEF && _lineBuffer[1] == 0xBB && _lineBuffer[2] == 0xBF)
            {
                offset = 3;
                length -= 3;
            }
            _firstLine = false;

            try
            {
                return StrictUtf8.GetString(_lineBuffer, offset, length);
            }
            catch (DecoderFallbackException)
            {
                return _fallbackEncoding.GetString(_lineBuffer, offset, length);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) _stream.Dispose();
            _disposed = true;
            base.Dispose(disposing);
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
}
