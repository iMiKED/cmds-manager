using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CmdsManager.Domain;

namespace CmdsManager.Infrastructure.Windows
{
    public sealed class ShowAppHotkeyRegistrationException : InvalidOperationException
    {
        internal ShowAppHotkeyRegistrationException(HotkeyAction action, string gesture, int nativeErrorCode)
            : base("Unable to register the global hotkey " + gesture + ". Win32 error " + nativeErrorCode + ".")
        {
            Action = action;
            Gesture = gesture ?? string.Empty;
            NativeErrorCode = nativeErrorCode;
        }

        public HotkeyAction Action { get; }
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
        private sealed class Registration
        {
            internal HotkeyAction Action { get; set; }
            internal ShowAppHotkeyGesture Gesture { get; set; }
            internal int Identifier { get; set; }
        }

        private const int FirstBankIdentifier = 0x434D;
        private const int SecondBankIdentifier = 0x435D;
        private const uint NoRepeat = 0x4000;
        private static readonly HotkeyAction[] GlobalActions =
        {
            HotkeyAction.ShowApp,
            HotkeyAction.QuickLaunch,
            HotkeyAction.EmergencyStopAll
        };

        private readonly IShowAppHotkeyNativeApi _native;
        private readonly Dictionary<HotkeyAction, Registration> _registered =
            new Dictionary<HotkeyAction, Registration>();
        private int _bank;
        private bool _disposed;

        public ShowAppHotkeyManager() : this(new Win32ShowAppHotkeyNativeApi())
        {
        }

        internal ShowAppHotkeyManager(IShowAppHotkeyNativeApi native)
        {
            _native = native ?? throw new ArgumentNullException(nameof(native));
            CreateHandle(new CreateParams { Caption = "Cmds Manager Global Hotkeys" });
        }

        public event EventHandler Pressed;
        public event EventHandler QuickLaunchPressed;
        public event EventHandler EmergencyStopAllPressed;

        public string RegisteredGesture => GetRegisteredGesture(HotkeyAction.ShowApp);

        public string GetRegisteredGesture(HotkeyAction action)
        {
            Registration registration;
            return _registered.TryGetValue(action, out registration)
                ? registration.Gesture.ToString()
                : string.Empty;
        }

        public void Apply(ApplicationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ThrowIfDisposed();

            var candidates = new Dictionary<HotkeyAction, ShowAppHotkeyGesture>();
            foreach (var action in GlobalActions)
            {
                var binding = settings.Hotkeys[action];
                if (!binding.Enabled) continue;
                ShowAppHotkeyGesture gesture;
                if (!ShowAppHotkeyGesture.TryParse(binding.Gesture, out gesture))
                    throw new FormatException(action + " must contain a modifier and a supported key.");
                if (candidates.Values.Any(item => item.Equals(gesture)))
                    throw new FormatException("Enabled global hotkeys must use different key combinations.");
                candidates[action] = gesture;
            }

            if (SameAsRegistered(candidates)) return;
            ReplaceRegistrations(candidates);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister(_registered.Values);
            _registered.Clear();
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
            if (_disposed) return;
            var registration = _registered.Values.FirstOrDefault(item => item.Identifier == identifier);
            if (registration == null) return;
            switch (registration.Action)
            {
                case HotkeyAction.ShowApp:
                    Pressed?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.QuickLaunch:
                    QuickLaunchPressed?.Invoke(this, EventArgs.Empty);
                    break;
                case HotkeyAction.EmergencyStopAll:
                    EmergencyStopAllPressed?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private void ReplaceRegistrations(IDictionary<HotkeyAction, ShowAppHotkeyGesture> candidates)
        {
            var previous = _registered.Values.Select(item => new Registration
            {
                Action = item.Action,
                Gesture = item.Gesture,
                Identifier = item.Identifier
            }).ToArray();
            var candidateBank = _bank == 1 ? 2 : 1;
            var added = new List<Registration>();

            Unregister(previous);
            try
            {
                foreach (var pair in candidates.OrderBy(item => (int)item.Key))
                {
                    var registration = new Registration
                    {
                        Action = pair.Key,
                        Gesture = pair.Value,
                        Identifier = IdentifierFor(pair.Key, candidateBank)
                    };
                    int errorCode;
                    if (!_native.Register(Handle, registration.Identifier,
                            (uint)registration.Gesture.Modifiers | NoRepeat,
                            registration.Gesture.VirtualKey, out errorCode))
                    {
                        throw new ShowAppHotkeyRegistrationException(pair.Key,
                            registration.Gesture.ToString(), errorCode);
                    }
                    added.Add(registration);
                }
            }
            catch
            {
                Unregister(added);
                foreach (var registration in previous)
                {
                    int ignored;
                    _native.Register(Handle, registration.Identifier,
                        (uint)registration.Gesture.Modifiers | NoRepeat,
                        registration.Gesture.VirtualKey, out ignored);
                }
                throw;
            }

            _registered.Clear();
            foreach (var registration in added) _registered[registration.Action] = registration;
            _bank = candidateBank;
        }

        private bool SameAsRegistered(IDictionary<HotkeyAction, ShowAppHotkeyGesture> candidates)
        {
            if (candidates.Count != _registered.Count) return false;
            foreach (var pair in candidates)
            {
                Registration registered;
                if (!_registered.TryGetValue(pair.Key, out registered) || !registered.Gesture.Equals(pair.Value))
                    return false;
            }
            return true;
        }

        private void Unregister(IEnumerable<Registration> registrations)
        {
            foreach (var registration in registrations.ToArray())
                _native.Unregister(Handle, registration.Identifier);
        }

        private static int IdentifierFor(HotkeyAction action, int bank)
        {
            var first = bank == 2 ? SecondBankIdentifier : FirstBankIdentifier;
            return first + Array.IndexOf(GlobalActions, action);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShowAppHotkeyManager));
        }
    }
}
