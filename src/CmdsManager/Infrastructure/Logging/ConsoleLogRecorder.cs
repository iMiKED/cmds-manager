using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CmdsManager.Infrastructure.Logging
{
    public enum ConsoleRecordingState
    {
        Recording,
        Paused,
        Stopped,
        LimitReached
    }

    public sealed class ConsoleLogRecorder : IDisposable
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly byte[] LimitMarker = Utf8.GetBytes(
            Environment.NewLine + "[Cmds Manager] Console log size limit reached." + Environment.NewLine);
        private readonly object _sync = new object();
        private readonly long _maximumBytes;
        private FileStream _stream;

        public ConsoleLogRecorder(string directory, string scriptName, int processId,
            DateTime startedAt, long maximumBytes)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Console log directory is required.", nameof(directory));
            if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));

            var fullDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(fullDirectory);
            _maximumBytes = maximumBytes;
            FilePath = CreateUniquePath(fullDirectory, scriptName, processId, startedAt);
            _stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite,
                65536, FileOptions.SequentialScan);
            State = ConsoleRecordingState.Recording;
        }

        public string FilePath { get; }
        public ConsoleRecordingState State { get; private set; }
        public long BytesWritten { get; private set; }

        public bool Write(string value)
        {
            if (string.IsNullOrEmpty(value)) return State != ConsoleRecordingState.LimitReached;
            lock (_sync)
            {
                if (State == ConsoleRecordingState.Paused) return true;
                if (State != ConsoleRecordingState.Recording || _stream == null) return false;

                var bytes = Utf8.GetBytes(value);
                var remaining = _maximumBytes - BytesWritten;
                if (bytes.LongLength <= remaining)
                {
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                    BytesWritten += bytes.LongLength;
                    return true;
                }

                WriteTruncatedValue(value, remaining);
                CloseStream();
                State = ConsoleRecordingState.LimitReached;
                return false;
            }
        }

        public void Pause()
        {
            lock (_sync)
            {
                if (State == ConsoleRecordingState.Recording) State = ConsoleRecordingState.Paused;
            }
        }

        public void Resume()
        {
            lock (_sync)
            {
                if (State == ConsoleRecordingState.Paused) State = ConsoleRecordingState.Recording;
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                CloseStream();
                State = ConsoleRecordingState.Stopped;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        public static void DeleteExpiredLogs(string directory, int retentionDays)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            var threshold = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
            foreach (var path in Directory.GetFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < threshold) File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void WriteTruncatedValue(string value, long remaining)
        {
            if (remaining <= 0 || _stream == null) return;
            var markerLength = Math.Min(remaining, LimitMarker.LongLength);
            var contentBudget = remaining - markerLength;
            if (contentBudget > 0)
            {
                var characterCount = MaximumPrefixLength(value, contentBudget);
                if (characterCount > 0)
                {
                    var completeLine = value.LastIndexOf('\n', characterCount - 1, characterCount);
                    if (completeLine >= 0) characterCount = completeLine + 1;
                    if (characterCount > 0 && char.IsHighSurrogate(value[characterCount - 1])) characterCount--;
                    var prefix = Utf8.GetBytes(value.Substring(0, characterCount));
                    _stream.Write(prefix, 0, prefix.Length);
                    BytesWritten += prefix.LongLength;
                }
            }

            var markerBytes = (int)Math.Min(_maximumBytes - BytesWritten, LimitMarker.LongLength);
            if (markerBytes > 0)
            {
                _stream.Write(LimitMarker, 0, markerBytes);
                BytesWritten += markerBytes;
            }
            _stream.Flush();
        }

        private static int MaximumPrefixLength(string value, long maximumBytes)
        {
            var low = 0;
            var high = value.Length;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (Utf8.GetByteCount(value.Substring(0, middle)) <= maximumBytes) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        private void CloseStream()
        {
            if (_stream == null) return;
            _stream.Dispose();
            _stream = null;
        }

        private static string CreateUniquePath(string directory, string scriptName, int processId, DateTime startedAt)
        {
            var stem = SafeFileName(scriptName) + "-" + processId.ToString(CultureInfo.InvariantCulture) + "-" +
                startedAt.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            for (var suffix = 0; suffix < 10000; suffix++)
            {
                var name = stem + (suffix == 0 ? string.Empty : "-" + suffix.ToString(CultureInfo.InvariantCulture)) + ".log";
                var path = Path.Combine(directory, name);
                if (!File.Exists(path)) return path;
            }
            throw new IOException("Could not allocate a unique console log file name.");
        }

        private static string SafeFileName(string value)
        {
            var result = string.IsNullOrWhiteSpace(value) ? "console" : value.Trim();
            foreach (var character in Path.GetInvalidFileNameChars()) result = result.Replace(character, '_');
            return result.Length > 60 ? result.Substring(0, 60) : result;
        }
    }
}
