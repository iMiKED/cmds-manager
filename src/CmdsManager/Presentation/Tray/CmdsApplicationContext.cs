using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Presentation.Forms;

namespace CmdsManager.Presentation.Tray
{
    public sealed class CmdsApplicationContext : ApplicationContext
    {
        private readonly MainForm _mainForm;
        private readonly ProcessSupervisor _supervisor;
        private readonly ConfigurationState _state;
        private readonly IExecutionLog _log;
        private readonly NotifyIcon _tray;
        private bool _exiting;
        private bool _disposed;
        private int _lastTrayClickTick;

        public CmdsApplicationContext(
            MainForm mainForm,
            ProcessSupervisor supervisor,
            ConfigurationState state,
            IExecutionLog log,
            bool startedAutomatically)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            var menu = new ContextMenuStrip();
            menu.Items.Add("Открыть / скрыть", null, (sender, args) => _mainForm.ToggleFromTray());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Запустить всё", null, (sender, args) => _mainForm.RunAllEnabled());
            menu.Items.Add("Остановить всё", null, async (sender, args) => await _mainForm.StopAllAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("О программе", null, (sender, args) => _mainForm.ShowAbout());
            menu.Items.Add("Выход", null, async (sender, args) => await ExitApplicationAsync());

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "CmdsManager",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += HandleTrayClick;
            _mainForm.ExitRequested += async (sender, args) => await ExitApplicationAsync();

            var handle = _mainForm.Handle;
            var shouldStartHidden = startedAutomatically
                ? _state.Current.Application.StartHiddenWhenAutoStarted
                : _state.Current.Application.StartMinimized;
            if (!shouldStartHidden)
            {
                _mainForm.Show();
            }
            else
            {
                _mainForm.Hide();
            }

            _mainForm.BeginInvoke((Action)(async () => await AutoStartScriptsAsync()));
        }

        public void ActivateFromAnotherInstance()
        {
            if (_mainForm.IsDisposed || !_mainForm.IsHandleCreated)
            {
                return;
            }

            _mainForm.BeginInvoke((Action)(() =>
            {
                _mainForm.ShowFromTray();
                Infrastructure.Windows.NativeMethods.SetForegroundWindow(_mainForm.Handle);
            }));
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                base.Dispose(disposing);
                return;
            }

            _disposed = true;
            if (disposing)
            {
                _tray.Visible = false;
                _tray.Dispose();
                if (!_mainForm.IsDisposed)
                {
                    _mainForm.AllowClose = true;
                    _mainForm.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void HandleTrayClick(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left)
            {
                return;
            }

            var now = Environment.TickCount;
            if (unchecked(now - _lastTrayClickTick) >= 0 && unchecked(now - _lastTrayClickTick) < 250)
            {
                return;
            }

            _lastTrayClickTick = now;
            _mainForm.ToggleFromTray();
        }

        private async Task AutoStartScriptsAsync()
        {
            if (!_state.Current.Application.AutoStartScripts)
            {
                return;
            }

            var failures = 0;
            var scripts = _state.Current.Scripts
                .Where(script => script.Enabled && script.Launch.AutoStartWithApplication)
                .OrderBy(script => script.Launch.AutoStartOrder)
                .ThenBy(script => script.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            foreach (var script in scripts)
            {
                if (_exiting)
                {
                    return;
                }

                if (script.Launch.AutoStartDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(script.Launch.AutoStartDelaySeconds));
                }

                try
                {
                    _supervisor.Start(script, _state.Current.PowerShell7Path);
                }
                catch (Exception)
                {
                    failures++;
                }
            }

            if (failures > 0)
            {
                _tray.ShowBalloonTip(5000, "CmdsManager", "Не удалось автоматически запустить скриптов: " + failures + ". Откройте приложение для подробностей.", ToolTipIcon.Warning);
            }
        }

        private async Task ExitApplicationAsync()
        {
            if (_exiting)
            {
                return;
            }

            if (_supervisor.HasRunningProcesses)
            {
                var answer = MessageBox.Show(
                    _mainForm,
                    "Все запущенные через CmdsManager скрипты будут остановлены. Выйти?",
                    "Выход из CmdsManager",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            _exiting = true;
            _tray.Text = "CmdsManager — завершение";
            try
            {
                await _mainForm.StopAllAsync();
                var timer = Stopwatch.StartNew();
                while (_supervisor.HasRunningProcesses && timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(100);
                }
            }
            catch (Exception exception)
            {
                _log.Error("Error while stopping scripts during exit.", exception);
            }
            finally
            {
                _tray.Visible = false;
                _mainForm.AllowClose = true;
                _mainForm.Close();
                ExitThread();
            }
        }
    }
}
