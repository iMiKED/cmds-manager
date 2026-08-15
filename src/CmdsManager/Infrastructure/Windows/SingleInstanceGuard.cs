using System;
using System.Threading;

namespace CmdsManager.Infrastructure.Windows
{
    public sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = @"Local\CmdsManager.Main.Instance";
        private const string ActivationEventName = @"Local\CmdsManager.Main.Activate";
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _activationEvent;
        private Thread _listener;
        private volatile bool _stopping;
        private bool _disposed;

        public SingleInstanceGuard()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            IsPrimaryInstance = createdNew;
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        }

        public bool IsPrimaryInstance { get; }

        public void SignalPrimaryInstance()
        {
            if (!IsPrimaryInstance)
            {
                _activationEvent.Set();
            }
        }

        public void StartListening(Action activationRequested)
        {
            if (!IsPrimaryInstance)
            {
                throw new InvalidOperationException("Only the primary instance can listen for activation.");
            }

            if (activationRequested == null)
            {
                throw new ArgumentNullException(nameof(activationRequested));
            }

            if (_listener != null)
            {
                return;
            }

            _listener = new Thread(() =>
            {
                while (!_stopping)
                {
                    _activationEvent.WaitOne();
                    if (!_stopping)
                    {
                        activationRequested();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "CmdsManager activation listener"
            };
            _listener.Start();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            _activationEvent.Set();
            if (_listener != null && _listener.IsAlive)
            {
                _listener.Join(1000);
            }

            _activationEvent.Dispose();
            if (IsPrimaryInstance)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }

            _mutex.Dispose();
        }
    }
}
