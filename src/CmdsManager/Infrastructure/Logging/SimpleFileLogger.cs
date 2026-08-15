using System;
using System.Globalization;
using System.IO;
using System.Text;
using CmdsManager.Application;

namespace CmdsManager.Infrastructure.Logging
{
    public sealed class SimpleFileLogger : IExecutionLog
    {
        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly int _retentionDays;
        private bool _disposed;

        public SimpleFileLogger(string directory, int retentionDays)
        {
            _directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            _retentionDays = Math.Max(1, retentionDays);
            Directory.CreateDirectory(_directory);
            DeleteExpiredLogs();
        }

        public void Information(string message)
        {
            Write("INF", message, null);
        }

        public void Warning(string message)
        {
            Write("WRN", message, null);
        }

        public void Error(string message, Exception exception = null)
        {
            Write("ERR", message, exception);
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private void Write(string level, string message, Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            var safeMessage = Sanitize(message);
            var line = string.Format(CultureInfo.InvariantCulture, "{0:O} [{1}] {2}", DateTimeOffset.Now, level, safeMessage);
            if (exception != null)
            {
                line += " | " + exception.GetType().Name + ": " + Sanitize(exception.Message);
            }

            lock (_sync)
            {
                var path = Path.Combine(_directory, "CmdsManager-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        private void DeleteExpiredLogs()
        {
            var threshold = DateTime.UtcNow.AddDays(-_retentionDays);
            foreach (var path in Directory.GetFiles(_directory, "CmdsManager-*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < threshold)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string Sanitize(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }
    }
}
