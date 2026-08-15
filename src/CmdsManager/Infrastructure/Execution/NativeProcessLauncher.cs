using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Windows;

namespace CmdsManager.Infrastructure.Execution
{
    internal sealed class NativeProcess : IDisposable
    {
        private bool _disposed;

        internal IntPtr ProcessHandle { get; set; }
        internal IntPtr JobHandle { get; set; }
        internal StreamReader StandardOutput { get; set; }
        internal StreamReader StandardError { get; set; }
        internal int ProcessId { get; set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StandardOutput?.Dispose();
            StandardError?.Dispose();
            if (JobHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(JobHandle);
                JobHandle = IntPtr.Zero;
            }

            if (ProcessHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(ProcessHandle);
                ProcessHandle = IntPtr.Zero;
            }
        }
    }

    internal static class NativeProcessLauncher
    {
        internal static NativeProcess Start(ProcessLaunchSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            IntPtr job = IntPtr.Zero;
            IntPtr stdoutRead = IntPtr.Zero;
            IntPtr stdoutWrite = IntPtr.Zero;
            IntPtr stderrRead = IntPtr.Zero;
            IntPtr stderrWrite = IntPtr.Zero;
            IntPtr nullInput = IntPtr.Zero;
            var processInfo = new NativeMethods.ProcessInformation();
            var processCreated = false;

            try
            {
                job = CreateKillOnCloseJob();

                var startup = new NativeMethods.StartupInfo
                {
                    Size = Marshal.SizeOf(typeof(NativeMethods.StartupInfo)),
                    Flags = NativeMethods.StartfUseShowWindow,
                    ShowWindow = ToShowWindow(spec.WindowMode)
                };

                if (spec.CaptureOutput)
                {
                    CreateOutputPipe(out stdoutRead, out stdoutWrite);
                    CreateOutputPipe(out stderrRead, out stderrWrite);
                    nullInput = CreateNullInput();
                    startup.Flags |= NativeMethods.StartfUseStdHandles;
                    startup.StandardInput = nullInput;
                    startup.StandardOutput = stdoutWrite;
                    startup.StandardError = stderrWrite;
                }

                var flags = NativeMethods.CreateSuspended | NativeMethods.CreateNewProcessGroup | NativeMethods.CreateUnicodeEnvironment;
                if (spec.WindowMode == ScriptWindowMode.Hidden)
                {
                    flags |= NativeMethods.CreateNoWindow;
                }

                var commandLine = new StringBuilder(ScriptCommandBuilder.QuoteWindowsArgument(spec.ExecutablePath));
                if (!string.IsNullOrWhiteSpace(spec.Arguments))
                {
                    commandLine.Append(' ').Append(spec.Arguments);
                }

                if (!NativeMethods.CreateProcessW(
                    spec.ExecutablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    spec.CaptureOutput,
                    flags,
                    IntPtr.Zero,
                    spec.WorkingDirectory,
                    ref startup,
                    out processInfo))
                {
                    throw LastWin32("Unable to create the script process.");
                }

                processCreated = true;
                Close(ref stdoutWrite);
                Close(ref stderrWrite);
                Close(ref nullInput);

                if (!NativeMethods.AssignProcessToJobObject(job, processInfo.Process))
                {
                    throw LastWin32("Unable to assign the process to a Windows Job Object.");
                }

                if (NativeMethods.ResumeThread(processInfo.Thread) == uint.MaxValue)
                {
                    throw LastWin32("Unable to resume the script process.");
                }

                Close(ref processInfo.Thread);

                var process = new NativeProcess
                {
                    ProcessHandle = processInfo.Process,
                    JobHandle = job,
                    ProcessId = processInfo.ProcessId
                };
                processInfo.Process = IntPtr.Zero;
                job = IntPtr.Zero;

                if (spec.CaptureOutput)
                {
                    var encoding = GetOutputEncoding(spec.OutputEncoding);
                    process.StandardOutput = CreateReader(ref stdoutRead, encoding);
                    process.StandardError = CreateReader(ref stderrRead, encoding);
                }

                return process;
            }
            catch
            {
                if (processCreated && processInfo.Process != IntPtr.Zero)
                {
                    NativeMethods.TerminateProcess(processInfo.Process, 1);
                }

                throw;
            }
            finally
            {
                Close(ref processInfo.Thread);
                Close(ref processInfo.Process);
                Close(ref stdoutRead);
                Close(ref stdoutWrite);
                Close(ref stderrRead);
                Close(ref stderrWrite);
                Close(ref nullInput);
                Close(ref job);
            }
        }

        private static IntPtr CreateKillOnCloseJob()
        {
            var job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                throw LastWin32("Unable to create a Windows Job Object.");
            }

            var information = new NativeMethods.JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;
            var size = Marshal.SizeOf(typeof(NativeMethods.JobObjectExtendedLimitInformation));
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, pointer, false);
                if (!NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectExtendedLimitInformationClass, pointer, (uint)size))
                {
                    var exception = LastWin32("Unable to configure a Windows Job Object.");
                    NativeMethods.CloseHandle(job);
                    throw exception;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            return job;
        }

        private static void CreateOutputPipe(out IntPtr readHandle, out IntPtr writeHandle)
        {
            var attributes = new NativeMethods.SecurityAttributes
            {
                Length = Marshal.SizeOf(typeof(NativeMethods.SecurityAttributes)),
                InheritHandle = true
            };

            if (!NativeMethods.CreatePipe(out readHandle, out writeHandle, ref attributes, 0))
            {
                throw LastWin32("Unable to create an output pipe.");
            }

            if (!NativeMethods.SetHandleInformation(readHandle, NativeMethods.HandleFlagInherit, 0))
            {
                var exception = LastWin32("Unable to protect an output pipe handle from inheritance.");
                Close(ref readHandle);
                Close(ref writeHandle);
                throw exception;
            }
        }

        private static IntPtr CreateNullInput()
        {
            var attributes = new NativeMethods.SecurityAttributes
            {
                Length = Marshal.SizeOf(typeof(NativeMethods.SecurityAttributes)),
                InheritHandle = true
            };

            var handle = NativeMethods.CreateFileW(
                "NUL",
                NativeMethods.GenericRead,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                ref attributes,
                NativeMethods.OpenExisting,
                0,
                IntPtr.Zero);
            if (handle == NativeMethods.InvalidHandleValue)
            {
                throw LastWin32("Unable to open NUL for script input.");
            }

            return handle;
        }

        private static StreamReader CreateReader(ref IntPtr handle, Encoding encoding)
        {
            var safeHandle = new SafeFileHandle(handle, true);
            handle = IntPtr.Zero;
            var stream = new FileStream(safeHandle, FileAccess.Read, 4096, false);
            return new StreamReader(stream, encoding, true, 4096);
        }

        private static Encoding GetOutputEncoding(ScriptOutputEncoding outputEncoding)
        {
            switch (outputEncoding)
            {
                case ScriptOutputEncoding.Utf8:
                    return new UTF8Encoding(false, false);
                case ScriptOutputEncoding.Windows1251:
                    return Encoding.GetEncoding(1251);
                case ScriptOutputEncoding.Utf16LittleEndian:
                    return Encoding.Unicode;
            }

            try
            {
                return Encoding.GetEncoding((int)NativeMethods.GetOEMCP());
            }
            catch (ArgumentException)
            {
                return Encoding.Default;
            }
        }

        private static short ToShowWindow(ScriptWindowMode mode)
        {
            switch (mode)
            {
                case ScriptWindowMode.Hidden:
                    return NativeMethods.SwHide;
                case ScriptWindowMode.Minimized:
                    return NativeMethods.SwShowMinimized;
                default:
                    return NativeMethods.SwShowNormal;
            }
        }

        private static Win32Exception LastWin32(string message)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), message);
        }

        private static void Close(ref IntPtr handle)
        {
            if (handle != IntPtr.Zero && handle != NativeMethods.InvalidHandleValue)
            {
                NativeMethods.CloseHandle(handle);
            }

            handle = IntPtr.Zero;
        }
    }
}
