using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CmdsManager.Application;
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
        private readonly LocalizationService _text;
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _toggle = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _startAll = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _stopAll = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _about = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _exit = new ToolStripMenuItem();
        private bool _exiting;
        private bool _disposed;
        private int _lastTrayClickTick;

        public CmdsApplicationContext(MainForm mainForm, ProcessSupervisor supervisor, ConfigurationState state,
            IExecutionLog log, LocalizationService text, bool startedAutomatically)
        {
            _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _text = text ?? throw new ArgumentNullException(nameof(text));

            _toggle.Click += (sender, args) => _mainForm.ToggleFromTray();
            _startAll.Click += (sender, args) => _mainForm.RunAllEnabled();
            _stopAll.Click += async (sender, args) => await _mainForm.StopAllAsync();
            _about.Click += (sender, args) => _mainForm.ShowAbout();
            _exit.Click += async (sender, args) => await ExitApplicationAsync();
            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                _toggle, new ToolStripSeparator(), _startAll, _stopAll,
                new ToolStripSeparator(), _about, _exit
            });
            _tray = new NotifyIcon
            {
                Icon = ApplicationResources.Icon,
                Text = "CmdsManager",
                ContextMenuStrip = menu,
                Visible = true
            };
            ApplyLocalization();
            _text.Changed += HandleLocalizationChanged;
            _tray.MouseClick += HandleTrayClick;
            _mainForm.ExitRequested += async (sender, args) => await ExitApplicationAsync();

            var handle = _mainForm.Handle;
            var shouldStartHidden = startedAutomatically
                ? _state.Current.Application.StartHiddenWhenAutoStarted
                : _state.Current.Application.StartMinimized;
            if (!shouldStartHidden) _mainForm.Show();
            else _mainForm.Hide();
            _mainForm.BeginInvoke((Action)(async () => await AutoStartScriptsAsync()));
        }

        public void ActivateFromAnotherInstance()
        {
            if (_mainForm.IsDisposed || !_mainForm.IsHandleCreated) return;
            _mainForm.BeginInvoke((Action)(() =>
            {
                _mainForm.ShowFromTray();
                Infrastructure.Windows.NativeMethods.SetForegroundWindow(_mainForm.Handle);
            }));
        }

        public void HandleExternalCommand(string command)
        {
            if (command != null && command.StartsWith("RUN ", StringComparison.Ordinal))
            {
                try
                {
                    var selector = Encoding.UTF8.GetString(Convert.FromBase64String(command.Substring(4)));
                    if (!_mainForm.IsDisposed && _mainForm.IsHandleCreated)
                        _mainForm.BeginInvoke((Action)(() => _mainForm.RunScript(selector)));
                    return;
                }
                catch (FormatException)
                {
                }
            }
            if (command != null && command.StartsWith("START ", StringComparison.Ordinal))
            {
                try
                {
                    var values = Encoding.UTF8.GetString(Convert.FromBase64String(command.Substring(6))).Split('\0');
                    if (values.Length >= 2 && !_mainForm.IsDisposed && _mainForm.IsHandleCreated)
                        _mainForm.BeginInvoke((Action)(() => _mainForm.RunManagedChild(values[0], values.Skip(1).ToArray())));
                    return;
                }
                catch (FormatException)
                {
                }
            }
            ActivateFromAnotherInstance();
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
                _text.Changed -= HandleLocalizationChanged;
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
            if (args.Button != MouseButtons.Left) return;
            var now = Environment.TickCount;
            if (unchecked(now - _lastTrayClickTick) >= 0 && unchecked(now - _lastTrayClickTick) < 250) return;
            _lastTrayClickTick = now;
            _mainForm.ToggleFromTray();
        }

        private async Task AutoStartScriptsAsync()
        {
            if (!_state.Current.Application.AutoStartScripts) return;
            var failures = 0;
            var scripts = _state.Current.Scripts
                .Where(script => script.Enabled && script.Launch.AutoStartWithApplication)
                .OrderBy(script => script.Launch.AutoStartOrder)
                .ThenBy(script => script.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            foreach (var script in scripts)
            {
                if (_exiting) return;
                if (script.Launch.AutoStartDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(script.Launch.AutoStartDelaySeconds));
                try { _supervisor.Start(script, _state.Current.PowerShell7Path); }
                catch (Exception) { failures++; }
            }
            if (failures > 0)
                _tray.ShowBalloonTip(5000, "CmdsManager", _text.Get("Tray.AutoStartFailed", failures), ToolTipIcon.Warning);
        }

        private async Task ExitApplicationAsync()
        {
            if (_exiting) return;
            if (_supervisor.HasRunningProcesses)
            {
                var answer = MessageBox.Show(_mainForm, _text["Tray.ExitConfirm"], _text["Tray.ExitTitle"],
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }
            _exiting = true;
            _tray.Text = NotifyText(_text["Tray.Exiting"]);
            try
            {
                await _mainForm.StopAllAsync();
                var timer = Stopwatch.StartNew();
                while (_supervisor.HasRunningProcesses && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(100);
            }
            catch (Exception exception) { _log.Error("Error while stopping scripts during exit.", exception); }
            finally
            {
                _tray.Visible = false;
                _mainForm.AllowClose = true;
                _mainForm.Close();
                ExitThread();
            }
        }

        private void HandleLocalizationChanged(object sender, EventArgs args)
        {
            if (_mainForm.IsDisposed || !_mainForm.IsHandleCreated) return;
            if (_mainForm.InvokeRequired) _mainForm.BeginInvoke((Action)ApplyLocalization);
            else ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            _toggle.Text = _text["Tray.Toggle"];
            _startAll.Text = _text["Main.StartAll"];
            _stopAll.Text = _text["Main.StopAll"];
            _about.Text = _text["Main.About"];
            _exit.Text = _text["Main.Exit"];
        }

        private static string NotifyText(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "CmdsManager" : value;
            return value.Length > 63 ? value.Substring(0, 63) : value;
        }
    }
}
