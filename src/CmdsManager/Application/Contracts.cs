using System;
using CmdsManager.Domain;

namespace CmdsManager.Application
{
    public interface IExecutionLog : IDisposable
    {
        void Information(string message);
        void Warning(string message);
        void Error(string message, Exception exception = null);
    }

    public interface IScriptEditorLauncher
    {
        void Edit(string scriptPath, ApplicationSettings settings);
        void ShowInFolder(string scriptPath);
    }

    public interface IApplicationStartupRegistration
    {
        string RegisteredCommand { get; }
        void Synchronize(bool enabled);
    }

    public sealed class ScriptStateChangedEventArgs : EventArgs
    {
        public ScriptStateChangedEventArgs(ScriptRuntimeSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public ScriptRuntimeSnapshot Snapshot { get; }
    }

    public sealed class ScriptOutputEventArgs : EventArgs
    {
        public ScriptOutputEventArgs(Guid scriptId, int processId, string line, bool isError)
        {
            ScriptId = scriptId;
            ProcessId = processId;
            Line = line ?? string.Empty;
            IsError = isError;
        }

        public Guid ScriptId { get; }
        public int ProcessId { get; }
        public string Line { get; }
        public bool IsError { get; }
    }

    public sealed class ScriptInstanceEventArgs : EventArgs
    {
        public ScriptInstanceEventArgs(Guid scriptId, string scriptName, int processId, DateTime startedAt, bool capturesOutput, int? exitCode)
        {
            ScriptId = scriptId;
            ScriptName = scriptName ?? string.Empty;
            ProcessId = processId;
            StartedAt = startedAt;
            CapturesOutput = capturesOutput;
            ExitCode = exitCode;
        }

        public Guid ScriptId { get; }
        public string ScriptName { get; }
        public int ProcessId { get; }
        public DateTime StartedAt { get; }
        public bool CapturesOutput { get; }
        public int? ExitCode { get; }
    }
}
