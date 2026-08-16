using System;
using System.IO;
using System.Text;

namespace CmdsManager.Infrastructure.Execution
{
    internal static class CmdScriptTransformer
    {
        private static readonly byte[] ManagedStartPrefix = Encoding.ASCII.GetBytes(
            "set \"CMDSMANAGER_START_CWD=%CD%\" & set \"CMDSMANAGER_START_LINE=");
        private static readonly byte[] ManagedStartSuffix = Encoding.ASCII.GetBytes(
            "\" & \"%CMDSMANAGER_HOST_EXE%\" --managed-start-env");

        internal static string TryCreateManagedCopy(string scriptPath)
        {
            var source = File.ReadAllBytes(scriptPath);
            using (var output = new MemoryStream(source.Length + 256))
            {
                var replacements = 0;
                var lineStart = 0;
                while (lineStart < source.Length)
                {
                    var lineEnd = lineStart;
                    while (lineEnd < source.Length && source[lineEnd] != '\r' && source[lineEnd] != '\n') lineEnd++;

                    var startToken = FindStartToken(source, lineStart, lineEnd);
                    if (startToken >= 0 && IsManagedCmdLaunch(source, startToken + 5, lineEnd))
                    {
                        output.Write(source, lineStart, startToken - lineStart);
                        output.Write(ManagedStartPrefix, 0, ManagedStartPrefix.Length);
                        output.Write(source, startToken + 5, lineEnd - startToken - 5);
                        output.Write(ManagedStartSuffix, 0, ManagedStartSuffix.Length);
                        replacements++;
                    }
                    else
                    {
                        output.Write(source, lineStart, lineEnd - lineStart);
                    }

                    while (lineEnd < source.Length && (source[lineEnd] == '\r' || source[lineEnd] == '\n'))
                    {
                        output.WriteByte(source[lineEnd]);
                        lineEnd++;
                    }
                    lineStart = lineEnd;
                }

                if (replacements == 0) return null;

                var directory = Path.GetDirectoryName(scriptPath);
                var extension = Path.GetExtension(scriptPath);
                var temporaryPath = Path.Combine(directory,
                    "." + Path.GetFileNameWithoutExtension(scriptPath) + ".cmdsmanager-" +
                    Guid.NewGuid().ToString("N") + extension);
                File.WriteAllBytes(temporaryPath, output.ToArray());
                return temporaryPath;
            }
        }

        private static int FindStartToken(byte[] source, int lineStart, int lineEnd)
        {
            var index = lineStart;
            if (index == 0 && lineEnd >= 3 && source[0] == 0xEF && source[1] == 0xBB && source[2] == 0xBF)
                index = 3;
            while (index < lineEnd && IsWhitespace(source[index])) index++;
            if (index < lineEnd && source[index] == '@')
            {
                index++;
                while (index < lineEnd && IsWhitespace(source[index])) index++;
            }

            if (!Matches(source, index, lineEnd, "start")) return -1;
            var after = index + 5;
            return after < lineEnd && IsWhitespace(source[after]) ? index : -1;
        }

        private static bool IsManagedCmdLaunch(byte[] source, int start, int end)
        {
            return (ContainsToken(source, start, end, "cmd") || ContainsToken(source, start, end, "cmd.exe")) &&
                (ContainsToken(source, start, end, "/k") || ContainsToken(source, start, end, "/c")) &&
                (Contains(source, start, end, ".cmd") || Contains(source, start, end, ".bat"));
        }

        private static bool ContainsToken(byte[] source, int start, int end, string value)
        {
            for (var index = start; index + value.Length <= end; index++)
            {
                if (!Matches(source, index, end, value)) continue;
                var before = index == start || IsBoundary(source[index - 1]);
                var afterIndex = index + value.Length;
                var after = afterIndex == end || IsBoundary(source[afterIndex]);
                if (before && after) return true;
            }
            return false;
        }

        private static bool Contains(byte[] source, int start, int end, string value)
        {
            for (var index = start; index + value.Length <= end; index++)
                if (Matches(source, index, end, value)) return true;
            return false;
        }

        private static bool Matches(byte[] source, int index, int end, string value)
        {
            if (index < 0 || index + value.Length > end) return false;
            for (var offset = 0; offset < value.Length; offset++)
            {
                var actual = source[index + offset];
                var expected = (byte)value[offset];
                if (actual >= 'A' && actual <= 'Z') actual = (byte)(actual + ('a' - 'A'));
                if (expected >= 'A' && expected <= 'Z') expected = (byte)(expected + ('a' - 'A'));
                if (actual != expected) return false;
            }
            return true;
        }

        private static bool IsWhitespace(byte value) { return value == ' ' || value == '\t'; }
        private static bool IsBoundary(byte value)
        {
            return IsWhitespace(value) || value == '"' || value == '\'' || value == '(' || value == ')' || value == '&';
        }
    }
}
