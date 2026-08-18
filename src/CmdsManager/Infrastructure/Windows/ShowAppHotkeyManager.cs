using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CmdsManager.Domain;

namespace CmdsManager.Infrastructure.Windows
{
    public sealed class ShowAppHotkeyRegistrationException : InvalidOperationException
    {
        internal ShowAppHotkeyRegistrationException(string gesture, int nativeErrorCode)
            : base("Unable to register the global hotkey " + gesture + ". Win32 error " + nativeErrorCode + ".")
        {
            Gesture = gesture ?? string.Empty;
            NativeErrorCode = nativeErrorCode;
        }

        public string Gesture { get; }
        public int NativeErrorCode { get; }
    }

    internal interface IShowAppHotkeyNativeApi
    {
        bool Register(IntPtr window, int identifier, uint modifiers, uint virtualKey, out int errorCode);
        bool Unregister(IntPtr window, int identifier);
    }

    internal sealed class Win32ShowAppHotkeyNativeApi : IShowAppHotkeyNativeApi
    {
        public bool Register(IntPtr window, int identifier, uint modifiers, uint virtualKey, out int errorCode)
        {
            var registered = NativeMethods.RegisterHotKey(window, identifier, modifiers, virtualKey);
            errorCode = registered ? 0 : Marshal.GetLastWin32Error();
            return registered;
        }

        public bool Unregister(IntPtr window, int identifier)
        {
            return NativeMethods.UnregisterHotKey(window, identifier);
        }
    }

    public sealed class ShowAppHotkeyManager : NativeWindow, IDisposable
    {
        private const int FirstIdentifier = 0x434D;
        private const int SecondIdentifier = 0x434E;
        private const uint NoRepeat = 0x4000;
        private readonly IShowAppHotkeyNativeApi _native;
        private ShowAppHotkeyGesture _registered;
        private int _registeredIdentifier;
        private bool _disposed;

        public ShowAppHotkeyManager() : this(new Win32ShowAppHotkeyNativeApi())
        {
        }

        internal ShowAppHotkeyManager(IShowAppHotkeyNativeApi native)
        {
            _native = native ?? throw new ArgumentNullException(nameof(native));
            CreateHandle(new CreateParams { Caption = "Cmds Manager Show App Hotkey" });
        }

        public event EventHandler Pressed;

        public string RegisteredGesture => _registered?.ToString() ?? string.Empty;

        public void Apply(ApplicationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ThrowIfDisposed();

            ShowAppHotkeyGesture candidate = null;
            if (settings.ShowAppHotkeyEnabled &&
                !ShowAppHotkeyGesture.TryParse(settings.ShowAppHotkey, out candidate))
            {
                throw new FormatException("ShowAppHotkey must contain a modifier and a supported key.");
            }

            Apply(candidate);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_registered != null)
            {
                _native.Unregister(Handle, _registeredIdentifier);
                _registered = null;
                _registeredIdentifier = 0;
            }
            DestroyHandle();
            GC.SuppressFinalize(this);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmHotkey)
                ProcessHotkeyMessage(message.WParam.ToInt32());
            base.WndProc(ref message);
        }

        internal void ProcessHotkeyMessage(int identifier)
        {
            if (!_disposed && _registered != null && identifier == _registeredIdentifier)
                Pressed?.Invoke(this, EventArgs.Empty);
        }

        private void Apply(ShowAppHotkeyGesture candidate)
        {
            if (Equals(candidate, _registered)) return;
            if (candidate == null)
            {
                if (_registered != null) _native.Unregister(Handle, _registeredIdentifier);
                _registered = null;
                _registeredIdentifier = 0;
                return;
            }

            var candidateIdentifier = _registeredIdentifier == FirstIdentifier
                ? SecondIdentifier
                : FirstIdentifier;
            int errorCode;
            if (!_native.Register(Handle, candidateIdentifier,
                    (uint)candidate.Modifiers | NoRepeat, candidate.VirtualKey, out errorCode))
            {
                throw new ShowAppHotkeyRegistrationException(candidate.ToString(), errorCode);
            }

            if (_registered != null) _native.Unregister(Handle, _registeredIdentifier);
            _registered = candidate;
            _registeredIdentifier = candidateIdentifier;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShowAppHotkeyManager));
        }
    }
}
