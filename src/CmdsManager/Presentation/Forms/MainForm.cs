using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Presentation.Controls;

namespace CmdsManager.Presentation.Forms
{
    public sealed class MainForm : Form
    {
        private readonly ConfigurationState _state;
        private readonly ConfigurationStore _store;
        private readonly ProcessSupervisor _supervisor;
        private readonly IScriptEditorLauncher _editor;
        private readonly IApplicationStartupRegistration _startup;
        private readonly IExecutionLog _log;
        private readonly LocalizationService _text;
        private readonly DataGridView _grid = new DataGridView();
        private readonly Timer _activityTimer = new Timer { Interval = 500 };
        private readonly Font _activityFont = new Font("Segoe UI Symbol", 11f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly ToolStripTextBox _filter = new ToolStripTextBox();
        private readonly ConsoleTabsControl _console;
        private readonly ToolStripButton _addButton;
        private readonly ToolStripButton _editButton;
        private readonly ToolStripButton _deleteButton;
        private readonly ToolStripButton _startButton;
        private readonly ToolStripButton _stopButton;
        private readonly ToolStripButton _startAllButton;
        private readonly ToolStripButton _stopAllButton;
        private readonly ToolStripButton _reloadButton;
        private readonly ToolStripButton _settingsButton;
        private readonly ToolStripButton _aboutButton;
        private readonly ToolStripButton _exitButton;
        private readonly ToolStripLabel _filterLabel = new ToolStripLabel();
        private readonly ToolStripMenuItem _contextStart = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextStop = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextEdit = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextEditFile = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextFolder = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _contextDelete = new ToolStripMenuItem();
        private bool _activityPulse;

        public MainForm(ConfigurationState state, ConfigurationStore store, ProcessSupervisor supervisor,
            IScriptEditorLauncher editor, IApplicationStartupRegistration startup, IExecutionLog log, LocalizationService text)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _startup = startup ?? throw new ArgumentNullException(nameof(startup));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _console = new ConsoleTabsControl(_text, () => Configuration.Application) { Dock = DockStyle.Fill };

            Text = "CmdsManager";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 520);
            Size = new Size(1120, 680);
            Icon = SystemIcons.Application;

            var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
            _addButton = Button((sender, args) => AddScript());
            _editButton = Button((sender, args) => EditSelected());
            _deleteButton = Button(async (sender, args) => await DeleteSelectedAsync());
            _startButton = Button((sender, args) => StartSelected());
            _stopButton = Button(async (sender, args) => await StopSelectedAsync());
            _startAllButton = Button((sender, args) => RunAllEnabled());
            _stopAllButton = Button(async (sender, args) => await StopAllAsync());
            _reloadButton = Button((sender, args) => ReloadConfiguration());
            _settingsButton = Button((sender, args) => OpenSettings());
            _aboutButton = Button((sender, args) => ShowAbout());
            _exitButton = Button((sender, args) => ExitRequested?.Invoke(this, EventArgs.Empty));
            _filter.AutoSize = false;
            _filter.Width = 160;
            _filter.TextChanged += (sender, args) => RefreshGrid();
            strip.Items.AddRange(new ToolStripItem[]
            {
                _addButton, _editButton, _deleteButton, new ToolStripSeparator(),
                _startButton, _stopButton, _startAllButton, _stopAllButton, new ToolStripSeparator(),
                _reloadButton, _settingsButton, _aboutButton, new ToolStripSeparator(),
                _filterLabel, _filter, new ToolStripSeparator(), _exitButton
            });

            ConfigureGrid();
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 400,
                Panel1MinSize = 220,
                Panel2MinSize = 100
            };
            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(_console);
            Controls.Add(split);
            Controls.Add(strip);
            strip.Dock = DockStyle.Top;

            _grid.SelectionChanged += (sender, args) => UpdateButtons();
            _grid.CellDoubleClick += (sender, args) => { if (args.RowIndex >= 0) EditSelected(); };
            FormClosing += HandleFormClosing;
            Resize += (sender, args) => { if (WindowState == FormWindowState.Minimized) Hide(); };

            _supervisor.StateChanged += HandleStateChanged;
            _supervisor.OutputReceived += HandleOutputReceived;
            _supervisor.InstanceStarted += HandleInstanceStarted;
            _supervisor.InstanceExited += HandleInstanceExited;
            _text.Changed += HandleLocalizationChanged;
            _activityTimer.Tick += HandleActivityTick;
            ApplyLocalization();
            _activityTimer.Start();
        }

        public event EventHandler ExitRequested;
        public bool AllowClose { get; set; }
        public AppConfiguration Configuration => _state.Current;

        public void ShowFromTray()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        public void ToggleFromTray()
        {
            if (Visible && WindowState != FormWindowState.Minimized) Hide();
            else ShowFromTray();
        }

        public void RunAllEnabled()
        {
            var errors = new List<string>();
            foreach (var script in Configuration.Scripts.Where(item => item.Enabled).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                try { _supervisor.Start(script, Configuration.PowerShell7Path); }
                catch (Exception exception) { errors.Add(script.Name + ": " + exception.Message); }
            }

            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void RunScript(string selector)
        {
            selector = (selector ?? string.Empty).Trim();
            Guid identifier;
            var byId = Guid.TryParse(selector, out identifier);
            var script = Configuration.Scripts.FirstOrDefault(item =>
                (byId && item.Id == identifier) || item.Name.Equals(selector, StringComparison.CurrentCultureIgnoreCase));
            if (script == null)
            {
                MessageBox.Show(this, _text.Get("Main.ScriptNotFound", selector), _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!script.Enabled)
            {
                MessageBox.Show(this, _text["Main.Disabled"], _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { _supervisor.Start(script, Configuration.PowerShell7Path); }
            catch (Exception exception) { ShowError(_text.Get("Main.StartFailed", script.Name), exception); }
        }

        public async Task StopAllAsync()
        {
            try { await _supervisor.StopAllAsync(); }
            catch (Exception exception) { ShowError(_text["Main.StopAllFailed"], exception); }
        }

        public void ShowAbout()
        {
            using (var form = new AboutForm(_text)) form.ShowDialog(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _supervisor.StateChanged -= HandleStateChanged;
                _supervisor.OutputReceived -= HandleOutputReceived;
                _supervisor.InstanceStarted -= HandleInstanceStarted;
                _supervisor.InstanceExited -= HandleInstanceExited;
                _text.Changed -= HandleLocalizationChanged;
                _activityTimer.Stop();
                _activityTimer.Tick -= HandleActivityTick;
                _activityTimer.Dispose();
                _activityFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoGenerateColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.None;
            var activityColumn = Column("Activity", 34);
            activityColumn.MinimumWidth = 34;
            activityColumn.Resizable = DataGridViewTriState.False;
            activityColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            activityColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            activityColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            activityColumn.DefaultCellStyle.Font = _activityFont;
            _grid.Columns.Add(activityColumn);
            _grid.Columns.Add(Column("Name", 170));
            _grid.Columns.Add(Column("Type", 65));
            _grid.Columns.Add(Column("Interpreter", 150));
            _grid.Columns.Add(Column("AutoStart", 55));
            _grid.Columns.Add(Column("State", 105));
            _grid.Columns.Add(Column("Pid", 70));
            _grid.Columns.Add(Column("Started", 130));
            _grid.Columns.Add(Column("ExitCode", 55));
            var pathColumn = Column("Path", 260);
            pathColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns.Add(pathColumn);

            var context = new ContextMenuStrip();
            _contextStart.Click += (sender, args) => StartSelected();
            _contextStop.Click += async (sender, args) => await StopSelectedAsync();
            _contextEdit.Click += (sender, args) => EditSelected();
            _contextEditFile.Click += (sender, args) => EditSelectedFile();
            _contextFolder.Click += (sender, args) => ShowSelectedInFolder();
            _contextDelete.Click += async (sender, args) => await DeleteSelectedAsync();
            context.Items.AddRange(new ToolStripItem[]
            {
                _contextStart, _contextStop, new ToolStripSeparator(),
                _contextEdit, _contextEditFile, _contextFolder, new ToolStripSeparator(), _contextDelete
            });
            _grid.ContextMenuStrip = context;
        }

        private void ApplyLocalization()
        {
            _addButton.Text = _text["Main.Add"];
            _editButton.Text = _text["Main.Edit"];
            _deleteButton.Text = _text["Main.Delete"];
            _startButton.Text = _text["Main.Start"];
            _stopButton.Text = _text["Main.Stop"];
            _startAllButton.Text = _text["Main.StartAll"];
            _stopAllButton.Text = _text["Main.StopAll"];
            _reloadButton.Text = _text["Main.Reload"];
            _settingsButton.Text = _text["Main.Settings"];
            _aboutButton.Text = _text["Main.About"];
            _exitButton.Text = _text["Main.Exit"];
            _filterLabel.Text = _text["Main.Filter"];
            _filter.ToolTipText = _text["Main.FilterHint"];
            _grid.Columns["Activity"].HeaderText = "●";
            _grid.Columns["Activity"].ToolTipText = _text["Main.Column.ActivityHint"];
            _grid.Columns["Name"].HeaderText = _text["Main.Column.Name"];
            _grid.Columns["Type"].HeaderText = _text["Main.Column.Type"];
            _grid.Columns["Interpreter"].HeaderText = _text["Main.Column.Interpreter"];
            _grid.Columns["AutoStart"].HeaderText = _text["Main.Column.AutoStart"];
            _grid.Columns["State"].HeaderText = _text["Main.Column.State"];
            _grid.Columns["Pid"].HeaderText = "PID";
            _grid.Columns["Started"].HeaderText = _text["Main.Column.Started"];
            _grid.Columns["ExitCode"].HeaderText = _text["Main.Column.ExitCode"];
            _grid.Columns["Path"].HeaderText = _text["Main.Column.Path"];
            _contextStart.Text = _text["Main.Start"];
            _contextStop.Text = _text["Main.Stop"];
            _contextEdit.Text = _text["Main.Context.EditEntry"];
            _contextEditFile.Text = _text["Main.Context.EditFile"];
            _contextFolder.Text = _text["Main.Context.ShowFolder"];
            _contextDelete.Text = _text["Main.Context.DeleteEntry"];
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            var selectedId = SelectedScript?.Id;
            var filter = _filter.Text?.Trim() ?? string.Empty;
            _grid.Rows.Clear();
            foreach (var script in Configuration.Scripts.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var type = Path.GetExtension(script.Path).TrimStart('.').ToUpperInvariant();
                if (filter.Length > 0 && script.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    script.Path.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 && type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var runtime = _supervisor.GetSnapshot(script.Id);
                var rowIndex = _grid.Rows.Add(ActivityGlyph(runtime.State), script.Name, type, InterpreterText(script),
                    script.Launch.AutoStartWithApplication ? _text["Common.Yes"] : _text["Common.No"], StateText(runtime),
                    runtime.ProcessId?.ToString() ?? "-", runtime.StartedAt?.ToString("g") ?? "-",
                    runtime.LastExitCode?.ToString() ?? "-", script.Path);
                var row = _grid.Rows[rowIndex];
                row.Tag = script.Id;
                ApplyRuntimeVisual(row, script, runtime);
                if (selectedId == script.Id)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells["Name"];
                }
            }
            UpdateButtons();
        }

        private void AddScript()
        {
            using (var form = new ScriptEditorForm(null, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath), _text))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                candidate.Scripts.Add(form.Result);
                SaveConfiguration(candidate);
            }
        }

        private void EditSelected()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            using (var form = new ScriptEditorForm(selected, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath), _text))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                var index = candidate.Scripts.FindIndex(item => item.Id == selected.Id);
                candidate.Scripts[index] = form.Result;
                SaveConfiguration(candidate);
            }
        }

        private async Task DeleteSelectedAsync()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            if (_supervisor.IsRunning(selected.Id))
            {
                MessageBox.Show(this, _text["Main.DeleteRunning"], _text["Main.DeleteTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Configuration.Application.ConfirmBeforeDelete)
            {
                var answer = MessageBox.Show(this, _text.Get("Main.DeleteConfirm", selected.Name), _text["Main.DeleteTitle"],
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }
            await Task.Yield();
            var candidate = Configuration.Clone();
            candidate.Scripts.RemoveAll(item => item.Id == selected.Id);
            SaveConfiguration(candidate);
        }

        private void StartSelected()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            if (!selected.Enabled)
            {
                MessageBox.Show(this, _text["Main.Disabled"], _text["Main.RunTitle"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { _supervisor.Start(selected, Configuration.PowerShell7Path); }
            catch (Exception exception) { ShowError(_text.Get("Main.StartFailed", selected.Name), exception); }
        }

        private async Task StopSelectedAsync()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            try { await _supervisor.StopAsync(selected.Id); }
            catch (Exception exception) { ShowError(_text.Get("Main.StopFailed", selected.Name), exception); }
        }

        private void EditSelectedFile()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            try { _editor.Edit(selected.Path, Configuration.Application); }
            catch (Exception exception) { ShowError(_text["Main.EditorFailed"], exception); }
        }

        private void ShowSelectedInFolder()
        {
            var selected = SelectedScript;
            if (selected == null) return;
            try { _editor.ShowInFolder(selected.Path); }
            catch (Exception exception) { ShowError(_text["Main.FolderFailed"], exception); }
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(Configuration.Application, Configuration.PowerShell7Path, Configuration.Localization, _text))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var candidate = Configuration.Clone();
                candidate.Application = form.SettingsResult;
                candidate.PowerShell7Path = form.PowerShell7PathResult;
                candidate.Localization.Language = form.LanguageResult;
                var previousStartup = Configuration.Application.StartWithWindows;
                try
                {
                    _startup.Synchronize(candidate.Application.StartWithWindows);
                    _store.Save(candidate);
                    _state.Current = candidate;
                    _console.ApplySettings();
                    MessageBox.Show(this, _text["Main.SettingsSaved"], _text["Main.Settings"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception exception)
                {
                    try { _startup.Synchronize(previousStartup); }
                    catch (Exception rollbackException) { _log.Error("Unable to roll back the startup registration.", rollbackException); }
                    ShowError(_text["Main.SettingsSaveFailed"], exception);
                }
            }
        }

        private void ReloadConfiguration()
        {
            if (_supervisor.HasRunningProcesses)
            {
                MessageBox.Show(this, _text["Main.ReloadRunning"], _text["Main.Reload"], MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var reloaded = _store.Reload();
                _startup.Synchronize(reloaded.Application.StartWithWindows);
                _state.Current = reloaded;
                _console.ApplySettings();
                _log.Information("Configuration reloaded from disk.");
            }
            catch (Exception exception) { ShowError(_text["Main.ReloadFailed"], exception); }
        }

        private void SaveConfiguration(AppConfiguration candidate)
        {
            try { _store.Save(candidate); _state.Current = candidate; }
            catch (Exception exception) { ShowError(_text["Main.SaveFailed"], exception); }
        }

        private void HandleStateChanged(object sender, ScriptStateChangedEventArgs args)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshGrid);
        }

        private void HandleActivityTick(object sender, EventArgs args)
        {
            if (IsDisposed || !IsHandleCreated) return;

            _activityPulse = !_activityPulse;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is Guid)) continue;
                var scriptId = (Guid)row.Tag;
                var script = Configuration.Scripts.FirstOrDefault(item => item.Id == scriptId);
                if (script == null) continue;
                ApplyRuntimeVisual(row, script, _supervisor.GetSnapshot(scriptId));
            }
        }

        private void ApplyRuntimeVisual(DataGridViewRow row, ScriptDefinition script, ScriptRuntimeSnapshot runtime)
        {
            var active = runtime.State == ScriptRuntimeState.Starting ||
                runtime.State == ScriptRuntimeState.Running || runtime.State == ScriptRuntimeState.Stopping;
            var rowColor = script.Enabled || active ? SystemColors.ControlText : SystemColors.GrayText;
            if (row.DefaultCellStyle.ForeColor != rowColor) row.DefaultCellStyle.ForeColor = rowColor;

            var stateText = StateText(runtime);
            SetCellText(row.Cells["State"], stateText);
            SetCellText(row.Cells["Pid"], runtime.ProcessId?.ToString() ?? "-");
            SetCellText(row.Cells["Started"], runtime.StartedAt?.ToString("g") ?? "-");
            SetCellText(row.Cells["ExitCode"], runtime.LastExitCode?.ToString() ?? "-");

            Color backgroundColor;
            switch (runtime.State)
            {
                case ScriptRuntimeState.Starting:
                    backgroundColor = Color.FromArgb(255, 250, 225);
                    break;
                case ScriptRuntimeState.Running:
                    backgroundColor = Color.FromArgb(234, 248, 239);
                    break;
                case ScriptRuntimeState.Stopping:
                    backgroundColor = Color.FromArgb(255, 243, 224);
                    break;
                case ScriptRuntimeState.Failed:
                    backgroundColor = Color.MistyRose;
                    break;
                default:
                    backgroundColor = SystemColors.Window;
                    break;
            }
            if (row.DefaultCellStyle.BackColor != backgroundColor)
                row.DefaultCellStyle.BackColor = backgroundColor;

            var indicator = row.Cells["Activity"];
            SetCellText(indicator, ActivityGlyph(runtime.State));
            var indicatorColor = ActivityColor(runtime.State);
            if (indicator.Style.ForeColor != indicatorColor) indicator.Style.ForeColor = indicatorColor;
            if (indicator.Style.SelectionForeColor != indicatorColor) indicator.Style.SelectionForeColor = indicatorColor;
            if (!string.Equals(indicator.ToolTipText, stateText, StringComparison.Ordinal))
                indicator.ToolTipText = stateText;

            var state = row.Cells["State"];
            var stateToolTip = runtime.State == ScriptRuntimeState.Failed && !string.IsNullOrWhiteSpace(runtime.Error)
                ? runtime.Error
                : stateText;
            if (!string.Equals(state.ToolTipText, stateToolTip, StringComparison.Ordinal))
                state.ToolTipText = stateToolTip;
        }

        private static void SetCellText(DataGridViewCell cell, string value)
        {
            if (!string.Equals(Convert.ToString(cell.Value), value, StringComparison.Ordinal))
                cell.Value = value;
        }

        private string ActivityGlyph(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Starting:
                case ScriptRuntimeState.Stopping:
                    return _activityPulse ? "◐" : "◓";
                case ScriptRuntimeState.Running:
                    return _activityPulse ? "●" : "◉";
                case ScriptRuntimeState.Failed:
                    return "●";
                default:
                    return "○";
            }
        }

        private Color ActivityColor(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Starting:
                    return Color.Goldenrod;
                case ScriptRuntimeState.Running:
                    return _activityPulse ? Color.FromArgb(24, 160, 88) : Color.FromArgb(17, 116, 67);
                case ScriptRuntimeState.Stopping:
                    return Color.DarkOrange;
                case ScriptRuntimeState.Failed:
                    return Color.Firebrick;
                default:
                    return Color.Gray;
            }
        }
        private void HandleOutputReceived(object sender, ScriptOutputEventArgs args) { _console.EnqueueOutput(args); }
        private void HandleInstanceStarted(object sender, ScriptInstanceEventArgs args) { _console.EnqueueStarted(args); }
        private void HandleInstanceExited(object sender, ScriptInstanceEventArgs args) { _console.EnqueueExited(args); }

        private void HandleLocalizationChanged(object sender, EventArgs args)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)ApplyLocalization);
            else ApplyLocalization();
        }

        private ScriptDefinition SelectedScript
        {
            get
            {
                if (_grid.SelectedRows.Count == 0 || !(_grid.SelectedRows[0].Tag is Guid)) return null;
                var id = (Guid)_grid.SelectedRows[0].Tag;
                return Configuration.Scripts.FirstOrDefault(item => item.Id == id);
            }
        }

        private void UpdateButtons()
        {
            var selected = SelectedScript;
            var running = selected != null && _supervisor.IsRunning(selected.Id);
            _editButton.Enabled = selected != null;
            _deleteButton.Enabled = selected != null && !running;
            _startButton.Enabled = selected != null && selected.Enabled && (!running || selected.Launch.AllowParallelInstances);
            _stopButton.Enabled = running;
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs args)
        {
            if (!AllowClose && args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                if (Configuration.Application.CloseToTray) Hide();
                else ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowError(string message, Exception exception)
        {
            _log.Error(message, exception);
            MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + exception.Message, "CmdsManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static ToolStripButton Button(EventHandler click)
        {
            var button = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += click;
            return button;
        }

        private static DataGridViewTextBoxColumn Column(string name, int width)
        {
            return new DataGridViewTextBoxColumn { Name = name, Width = width, SortMode = DataGridViewColumnSortMode.Automatic };
        }

        private static string InterpreterText(ScriptDefinition script)
        {
            var interpreter = script.Launch.Interpreter == ScriptInterpreter.Auto ? ScriptDefinitionValidator.ResolveAutoInterpreter(script.Path) : script.Launch.Interpreter;
            switch (interpreter)
            {
                case ScriptInterpreter.Cmd: return "CMD";
                case ScriptInterpreter.WindowsPowerShell: return "Windows PS 5.1";
                case ScriptInterpreter.PowerShell7: return "PowerShell 7";
                case ScriptInterpreter.CScript: return "cscript.exe";
                case ScriptInterpreter.WScript: return "wscript.exe";
                default: return interpreter.ToString();
            }
        }

        private string StateText(ScriptRuntimeSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case ScriptRuntimeState.Starting: return _text["Main.State.Starting"];
                case ScriptRuntimeState.Running: return snapshot.ActiveCount > 1 ? _text.Get("Main.State.RunningMany", snapshot.ActiveCount) : _text["Main.State.Running"];
                case ScriptRuntimeState.Stopping: return _text["Main.State.Stopping"];
                case ScriptRuntimeState.Exited: return _text["Main.State.Exited"];
                case ScriptRuntimeState.Failed: return _text["Main.State.Failed"];
                default: return _text["Main.State.Stopped"];
            }
        }
    }
}
