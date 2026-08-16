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
using CmdsManager.Presentation.Theming;
using Microsoft.Win32;

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
        private readonly Font _activityFont = new Font("Segoe UI Symbol", 11f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _gridHeaderFont = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly ToolStripTextBox _filter = new ToolStripTextBox();
        private readonly ToolStrip _toolbar;
        private readonly Dictionary<ToolStripButton, ToolbarIcon> _toolbarIcons =
            new Dictionary<ToolStripButton, ToolbarIcon>();
        private readonly ConsoleTabsControl _console;
        private readonly SplitContainer _mainSplit;
        private readonly System.Windows.Forms.Timer _layoutSaveTimer = new System.Windows.Forms.Timer { Interval = 600 };
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
        private bool _refreshingGrid;
        private bool _restoringPaneLayout;
        private bool _consolePaneMaximized;
        private int _normalConsolePaneHeight;
        private AppThemePalette _palette = AppThemePalette.Light();

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

            Text = ApplicationResources.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 520);
            Size = new Size(1120, 680);
            Icon = ApplicationResources.Icon;
            KeyPreview = true;

            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = false,
                Height = 42,
                Padding = new Padding(7, 4, 7, 4),
                ImageScalingSize = new Size(16, 16),
                CanOverflow = true
            };
            _addButton = Button((sender, args) => AddScript(), ToolbarIcon.Add);
            _editButton = Button((sender, args) => EditSelected(), ToolbarIcon.Edit);
            _deleteButton = Button(async (sender, args) => await DeleteSelectedAsync(), ToolbarIcon.Delete, FluentToolRole.Danger);
            _startButton = Button((sender, args) => StartSelected(), ToolbarIcon.Start, FluentToolRole.Primary);
            _stopButton = Button(async (sender, args) => await StopSelectedAsync(), ToolbarIcon.Stop);
            _startAllButton = Button((sender, args) => RunAllEnabled(), ToolbarIcon.StartAll);
            _stopAllButton = Button(async (sender, args) => await StopAllAsync(), ToolbarIcon.StopAll);
            _reloadButton = Button((sender, args) => ReloadConfiguration(), ToolbarIcon.Reload);
            _settingsButton = Button((sender, args) => OpenSettings(), ToolbarIcon.Settings);
            _aboutButton = Button((sender, args) => ShowAbout(), ToolbarIcon.About);
            _exitButton = Button((sender, args) => ExitRequested?.Invoke(this, EventArgs.Empty), ToolbarIcon.Exit, FluentToolRole.Danger);
            UseCompactImageOnly(_reloadButton, _settingsButton, _aboutButton, _exitButton);
            _filter.AutoSize = false;
            _filter.Width = 160;
            _filter.Height = 24;
            _filter.TextChanged += (sender, args) => RefreshGrid();
            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                _addButton, _editButton, _deleteButton, new ToolStripSeparator(),
                _startButton, _stopButton, _startAllButton, _stopAllButton, new ToolStripSeparator(),
                _reloadButton, _settingsButton, _aboutButton, new ToolStripSeparator(),
                _filterLabel, _filter, new ToolStripSeparator(), _exitButton
            });

            ConfigureGrid();
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 400,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel2,
                Panel1MinSize = OneScriptPanelHeight,
                Panel2MinSize = 100
            };
            _mainSplit.Panel1.Controls.Add(_grid);
            _mainSplit.Panel2.Controls.Add(_console);
            Controls.Add(_mainSplit);
            Controls.Add(_toolbar);
            _toolbar.Dock = DockStyle.Top;

            _grid.SelectionChanged += HandleGridSelectionChanged;
            _grid.RowPostPaint += HandleGridRowPostPaint;
            _grid.CellDoubleClick += (sender, args) => { if (args.RowIndex >= 0) EditSelected(); };
            _mainSplit.SplitterMoved += HandleSplitterMoved;
            _mainSplit.DoubleClick += (sender, args) => ToggleConsolePaneMaximized();
            _mainSplit.SizeChanged += HandleSplitSizeChanged;
            _layoutSaveTimer.Tick += HandleLayoutSaveTimer;
            FormClosing += HandleFormClosing;
            Shown += (sender, args) => ApplyConsolePaneHeight();
            KeyDown += HandleMainKeyDown;
            Resize += HandleMainResize;

            _supervisor.StateChanged += HandleStateChanged;
            _supervisor.OutputReceived += HandleOutputReceived;
            _supervisor.InstanceStarted += HandleInstanceStarted;
            _supervisor.InstanceExited += HandleInstanceExited;
            _text.Changed += HandleLocalizationChanged;
            SystemEvents.UserPreferenceChanged += HandleSystemPreferenceChanged;
            _console.CloseRequested += HandleConsoleCloseRequested;
            _console.PaneMaximizeRequested += HandleConsolePaneMaximizeRequested;
            ApplyLocalization();
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

        public void RunManagedChild(string parentWorkingDirectory, string[] startArguments)
        {
            try
            {
                var request = ManagedStartRequestParser.Parse(parentWorkingDirectory, startArguments);
                var script = request.ToScriptDefinition();
                _supervisor.Start(script, Configuration.PowerShell7Path);
            }
            catch (Exception exception)
            {
                ShowError(_text["Main.ChildStartFailed"], exception);
            }
        }

        public async Task StopAllAsync()
        {
            try { await _supervisor.StopAllAsync(); }
            catch (Exception exception) { ShowError(_text["Main.StopAllFailed"], exception); }
        }

        public void ShowAbout()
        {
            using (var form = new AboutForm(_text, Configuration.Application.Theme)) form.ShowDialog(this);
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
                SystemEvents.UserPreferenceChanged -= HandleSystemPreferenceChanged;
                _console.CloseRequested -= HandleConsoleCloseRequested;
                _console.PaneMaximizeRequested -= HandleConsolePaneMaximizeRequested;
                _layoutSaveTimer.Stop();
                _layoutSaveTimer.Dispose();
                foreach (var button in _toolbarIcons.Keys)
                {
                    var image = button.Image;
                    button.Image = null;
                    image?.Dispose();
                }
                _activityFont.Dispose();
                _gridHeaderFont.Dispose();
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
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.RowTemplate.Height = 38;
            _grid.ColumnHeadersHeight = 34;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.ColumnHeadersDefaultCellStyle.Font = _gridHeaderFont;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.None;
            var activityColumn = Column("Activity", 40);
            activityColumn.MinimumWidth = 40;
            activityColumn.Resizable = DataGridViewTriState.False;
            activityColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
            activityColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            activityColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            activityColumn.DefaultCellStyle.Padding = Padding.Empty;
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
            foreach (var button in _toolbarIcons.Keys) button.ToolTipText = button.Text;
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
            ApplyTheme();
        }

        private void RefreshGrid()
        {
            var selectedId = SelectedScript?.Id;
            var filter = _filter.Text?.Trim() ?? string.Empty;
            _refreshingGrid = true;
            try
            {
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
            }
            finally { _refreshingGrid = false; }
            UpdateScriptPanelMinimum();
            UpdateButtons();
        }

        private void AddScript()
        {
            using (var form = new ScriptEditorForm(null, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath), _text,
                Configuration.Application.Theme))
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
            using (var form = new ScriptEditorForm(selected, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath), _text,
                Configuration.Application.Theme))
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
                    ApplyTheme();
                    ApplyConsolePaneHeight();
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
                ApplyTheme();
                ApplyConsolePaneHeight();
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
            if (!Configuration.Scripts.Any(item => item.Id == args.Snapshot.ScriptId)) return;
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshGrid);
        }

        private void HandleGridSelectionChanged(object sender, EventArgs args)
        {
            if (_refreshingGrid) return;
            UpdateButtons();
            var selected = SelectedScript;
            if (selected != null) _console.SelectScript(selected.Id);
        }

        private void ApplyRuntimeVisual(DataGridViewRow row, ScriptDefinition script, ScriptRuntimeSnapshot runtime)
        {
            var active = runtime.State == ScriptRuntimeState.Starting ||
                runtime.State == ScriptRuntimeState.Running || runtime.State == ScriptRuntimeState.Stopping;
            var rowColor = script.Enabled || active ? _palette.Text : _palette.DisabledText;
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
                    backgroundColor = _palette.StartingBackground;
                    break;
                case ScriptRuntimeState.Running:
                    backgroundColor = _palette.RunningBackground;
                    break;
                case ScriptRuntimeState.Stopping:
                    backgroundColor = _palette.StoppingBackground;
                    break;
                case ScriptRuntimeState.Failed:
                    backgroundColor = _palette.FailedBackground;
                    break;
                default:
                    backgroundColor = row.Index % 2 == 0 ? _palette.Surface : _palette.SurfaceAlternate;
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

        private static string ActivityGlyph(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Starting:
                    return "◐";
                case ScriptRuntimeState.Stopping:
                    return "◓";
                case ScriptRuntimeState.Running:
                    return "●";
                case ScriptRuntimeState.Failed:
                    return "●";
                default:
                    return "○";
            }
        }

        private static Color ActivityColor(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Starting:
                    return Color.Goldenrod;
                case ScriptRuntimeState.Running:
                    return Color.FromArgb(24, 160, 88);
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

        private async void HandleConsoleCloseRequested(object sender, ConsoleTabCloseRequestedEventArgs args)
        {
            if (!args.IsRunning) return;
            try { await _supervisor.StopInstanceAsync(args.ScriptId, args.ProcessId); }
            catch (Exception exception) { ShowError(_text["Console.StopFailed"], exception); }
        }

        private void HandleConsolePaneMaximizeRequested(object sender, EventArgs args)
        {
            ToggleConsolePaneMaximized();
        }

        private void HandleMainKeyDown(object sender, KeyEventArgs args)
        {
            if (args.KeyCode != Keys.F11) return;
            if (_console.ToggleSelectedTabFullScreen())
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
        }

        private void HandleMainResize(object sender, EventArgs args)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                return;
            }
        }

        private void HandleSplitSizeChanged(object sender, EventArgs args)
        {
            if (!_consolePaneMaximized || _restoringPaneLayout || _mainSplit.Height <= 0) return;
            _restoringPaneLayout = true;
            try { _mainSplit.SplitterDistance = _mainSplit.Panel1MinSize; }
            finally { _restoringPaneLayout = false; }
        }

        private void ToggleConsolePaneMaximized()
        {
            if (_mainSplit.Height <= 0) return;
            var wasRestoring = _restoringPaneLayout;
            _restoringPaneLayout = true;
            try
            {
                if (_consolePaneMaximized)
                {
                    _consolePaneMaximized = false;
                    UpdateScriptPanelMinimum();
                    SetConsolePaneHeight(_normalConsolePaneHeight > 0
                        ? _normalConsolePaneHeight
                        : Configuration.Application.ConsolePaneHeight);
                }
                else
                {
                    _normalConsolePaneHeight = Math.Max(_mainSplit.Panel2MinSize, _mainSplit.Panel2.Height);
                    _mainSplit.Panel1MinSize = OneScriptPanelHeight;
                    _mainSplit.SplitterDistance = _mainSplit.Panel1MinSize;
                    _consolePaneMaximized = true;
                }
                _console.SetPaneMaximized(_consolePaneMaximized);
            }
            finally
            {
                _restoringPaneLayout = wasRestoring;
            }
            if (!_consolePaneMaximized) SchedulePaneHeightSave();
        }

        private void ApplyConsolePaneHeight()
        {
            if (_mainSplit.Height <= 0) return;
            var wasRestoring = _restoringPaneLayout;
            _restoringPaneLayout = true;
            try
            {
                _consolePaneMaximized = false;
                UpdateScriptPanelMinimum();
                SetConsolePaneHeight(Configuration.Application.ConsolePaneHeight);
                _normalConsolePaneHeight = _mainSplit.Panel2.Height;
                _console.SetPaneMaximized(false);
            }
            finally
            {
                _restoringPaneLayout = wasRestoring;
            }
        }

        private void SetConsolePaneHeight(int requestedHeight)
        {
            var maximum = Math.Max(_mainSplit.Panel2MinSize,
                _mainSplit.Height - _mainSplit.SplitterWidth - _mainSplit.Panel1MinSize);
            var height = Math.Max(_mainSplit.Panel2MinSize, Math.Min(maximum, requestedHeight));
            _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize,
                _mainSplit.Height - _mainSplit.SplitterWidth - height);
        }

        private void HandleSplitterMoved(object sender, SplitterEventArgs args)
        {
            if (_restoringPaneLayout) return;
            if (_consolePaneMaximized && _mainSplit.SplitterDistance > OneScriptPanelHeight + 1)
            {
                if (Control.MouseButtons == MouseButtons.Left)
                {
                    _consolePaneMaximized = false;
                    UpdateScriptPanelMinimum();
                }
                else
                {
                    _restoringPaneLayout = true;
                    try { _mainSplit.SplitterDistance = OneScriptPanelHeight; }
                    finally { _restoringPaneLayout = false; }
                    return;
                }
            }
            _console.SetPaneMaximized(_consolePaneMaximized);
            if (_consolePaneMaximized) return;
            _normalConsolePaneHeight = _mainSplit.Panel2.Height;
            SchedulePaneHeightSave();
        }

        private void SchedulePaneHeightSave()
        {
            Configuration.Application.ConsolePaneHeight = Math.Max(_mainSplit.Panel2MinSize,
                _normalConsolePaneHeight);
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private void HandleLayoutSaveTimer(object sender, EventArgs args)
        {
            _layoutSaveTimer.Stop();
            SavePaneHeightSilently();
        }

        private int OneScriptPanelHeight => Math.Max(48,
            _grid.ColumnHeadersHeight + _grid.RowTemplate.Height + 4);

        private void UpdateScriptPanelMinimum()
        {
            if (_mainSplit == null || _mainSplit.Height <= 0) return;
            var maximum = Math.Max(OneScriptPanelHeight,
                _mainSplit.Height - _mainSplit.SplitterWidth - _mainSplit.Panel2MinSize);
            var rowsHeight = Math.Max(1, _grid.Rows.Count) * _grid.RowTemplate.Height;
            var normalMinimum = Math.Min(maximum,
                Math.Max(OneScriptPanelHeight, _grid.ColumnHeadersHeight + rowsHeight + 4));
            var target = _consolePaneMaximized ? OneScriptPanelHeight : normalMinimum;
            var wasRestoring = _restoringPaneLayout;
            _restoringPaneLayout = true;
            try
            {
                _mainSplit.Panel1MinSize = OneScriptPanelHeight;
                if (_mainSplit.SplitterDistance < target) _mainSplit.SplitterDistance = target;
                _mainSplit.Panel1MinSize = target;
            }
            finally
            {
                _restoringPaneLayout = wasRestoring;
            }
        }

        private void SavePaneHeightSilently()
        {
            try
            {
                _store.Save(Configuration);
            }
            catch (Exception exception)
            {
                _log.Warning("Unable to persist the console pane height: " + exception.Message);
            }
        }

        private void HandleLocalizationChanged(object sender, EventArgs args)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)ApplyLocalization);
            else ApplyLocalization();
        }

        private void HandleSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (Configuration.Application.Theme != ApplicationTheme.System || IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)ApplyTheme);
            else ApplyTheme();
        }

        private void ApplyTheme()
        {
            _palette = AppThemeManager.Resolve(Configuration.Application.Theme);
            AppThemeManager.ApplyWindow(this, Configuration.Application.Theme);
            AppThemeManager.ApplyToolStrip(_toolbar, _palette);
            AppThemeManager.ApplyToolStrip(_grid.ContextMenuStrip, _palette);
            _console.ApplyApplicationTheme(Configuration.Application.Theme);
            ApplyToolbarIcons();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is Guid)) continue;
                var script = Configuration.Scripts.FirstOrDefault(item => item.Id == (Guid)row.Tag);
                if (script != null) ApplyRuntimeVisual(row, script, _supervisor.GetSnapshot(script.Id));
            }
            _grid.Invalidate();
        }

        private void ApplyToolbarIcons()
        {
            foreach (var pair in _toolbarIcons)
            {
                var previous = pair.Key.Image;
                var role = pair.Key.Tag is FluentToolRole ? (FluentToolRole)pair.Key.Tag : FluentToolRole.Normal;
                var color = role == FluentToolRole.Primary ? Color.White :
                    role == FluentToolRole.Danger ? _palette.Danger : _palette.Text;
                pair.Key.Image = ToolbarIconFactory.Create(pair.Value, color);
                previous?.Dispose();
            }
        }

        private void HandleGridRowPostPaint(object sender, DataGridViewRowPostPaintEventArgs args)
        {
            if (args.RowIndex < 0 || !_grid.Rows[args.RowIndex].Selected) return;
            using (var brush = new SolidBrush(_palette.Accent))
                args.Graphics.FillRectangle(brush, args.RowBounds.Left, args.RowBounds.Top, 3, args.RowBounds.Height);
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
            else if (AllowClose)
            {
                _layoutSaveTimer.Stop();
                if (!_consolePaneMaximized && _normalConsolePaneHeight > 0)
                {
                    Configuration.Application.ConsolePaneHeight = _normalConsolePaneHeight;
                    SavePaneHeightSilently();
                }
            }
        }

        private void ShowError(string message, Exception exception)
        {
            _log.Error(message, exception);
            MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + exception.Message,
                ApplicationResources.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private ToolStripButton Button(EventHandler click, ToolbarIcon icon, FluentToolRole role = FluentToolRole.Normal)
        {
            var button = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ImageScaling = ToolStripItemImageScaling.None,
                AutoSize = true,
                Margin = new Padding(1, 0, 1, 0),
                Padding = new Padding(5, 3, 5, 3),
                Tag = role,
                Overflow = ToolStripItemOverflow.AsNeeded
            };
            button.Click += click;
            _toolbarIcons.Add(button, icon);
            return button;
        }

        private static void UseCompactImageOnly(params ToolStripButton[] buttons)
        {
            foreach (var button in buttons)
            {
                button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                button.AutoToolTip = true;
                button.Padding = new Padding(5, 3, 5, 3);
            }
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
