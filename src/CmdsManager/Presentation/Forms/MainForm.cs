using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Configuration;
using CmdsManager.Infrastructure.Execution;

namespace CmdsManager.Presentation.Forms
{
    public sealed class MainForm : Form
    {
        private const int MaxOutputCharactersPerScript = 100000;
        private readonly ConfigurationState _state;
        private readonly ConfigurationStore _store;
        private readonly ProcessSupervisor _supervisor;
        private readonly IScriptEditorLauncher _editor;
        private readonly IApplicationStartupRegistration _startup;
        private readonly IExecutionLog _log;
        private readonly DataGridView _grid = new DataGridView();
        private readonly RichTextBox _output = new RichTextBox();
        private readonly ToolStripTextBox _filter = new ToolStripTextBox();
        private readonly Dictionary<Guid, StringBuilder> _outputByScript = new Dictionary<Guid, StringBuilder>();
        private readonly ToolStripButton _startButton;
        private readonly ToolStripButton _stopButton;
        private readonly ToolStripButton _editButton;
        private readonly ToolStripButton _deleteButton;

        public MainForm(
            ConfigurationState state,
            ConfigurationStore store,
            ProcessSupervisor supervisor,
            IScriptEditorLauncher editor,
            IApplicationStartupRegistration startup,
            IExecutionLog log)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _startup = startup ?? throw new ArgumentNullException(nameof(startup));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            Text = "CmdsManager";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 560);
            Size = new Size(1180, 720);
            Icon = SystemIcons.Application;

            var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
            var add = Button("Добавить", (sender, args) => AddScript());
            _editButton = Button("Изменить", (sender, args) => EditSelected());
            _deleteButton = Button("Удалить", async (sender, args) => await DeleteSelectedAsync());
            _startButton = Button("Запустить", (sender, args) => StartSelected());
            _stopButton = Button("Остановить", async (sender, args) => await StopSelectedAsync());
            var startAll = Button("Запустить всё", (sender, args) => RunAllEnabled());
            var stopAll = Button("Остановить всё", async (sender, args) => await StopAllAsync());
            var reload = Button("Перечитать INI", (sender, args) => ReloadConfiguration());
            var settings = Button("Настройки", (sender, args) => OpenSettings());
            var about = Button("О программе", (sender, args) => ShowAbout());
            var exit = Button("Выход", (sender, args) => ExitRequested?.Invoke(this, EventArgs.Empty));
            _filter.AutoSize = false;
            _filter.Width = 180;
            _filter.ToolTipText = "Фильтр по имени, пути и типу";
            _filter.TextChanged += (sender, args) => RefreshGrid();
            strip.Items.AddRange(new ToolStripItem[]
            {
                add, _editButton, _deleteButton,
                new ToolStripSeparator(),
                _startButton, _stopButton, startAll, stopAll,
                new ToolStripSeparator(),
                reload, settings, about,
                new ToolStripSeparator(),
                new ToolStripLabel("Фильтр:"), _filter,
                new ToolStripSeparator(), exit
            });

            ConfigureGrid();
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 430,
                Panel1MinSize = 220,
                Panel2MinSize = 100
            };
            split.Panel1.Controls.Add(_grid);

            _output.Dock = DockStyle.Fill;
            _output.ReadOnly = true;
            _output.WordWrap = false;
            _output.BackColor = Color.FromArgb(28, 28, 28);
            _output.ForeColor = Color.Gainsboro;
            _output.Font = new Font(FontFamily.GenericMonospace, 9f);
            split.Panel2.Controls.Add(_output);

            Controls.Add(split);
            Controls.Add(strip);
            strip.Dock = DockStyle.Top;

            _grid.SelectionChanged += (sender, args) =>
            {
                ShowSelectedOutput();
                UpdateButtons();
            };
            _grid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0)
                {
                    EditSelected();
                }
            };

            FormClosing += HandleFormClosing;
            Resize += (sender, args) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    Hide();
                }
            };

            _supervisor.StateChanged += HandleStateChanged;
            _supervisor.OutputReceived += HandleOutputReceived;
            RefreshGrid();
        }

        public event EventHandler ExitRequested;

        public bool AllowClose { get; set; }

        public AppConfiguration Configuration => _state.Current;

        public void ShowFromTray()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Show();
            Activate();
            BringToFront();
        }

        public void ToggleFromTray()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                Hide();
            }
            else
            {
                ShowFromTray();
            }
        }

        public void RunAllEnabled()
        {
            var errors = new List<string>();
            foreach (var script in Configuration.Scripts.Where(item => item.Enabled).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    _supervisor.Start(script, Configuration.PowerShell7Path);
                }
                catch (InvalidOperationException exception)
                {
                    errors.Add(script.Name + ": " + exception.Message);
                }
                catch (Exception exception)
                {
                    errors.Add(script.Name + ": " + exception.Message);
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Запуск скриптов", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public async Task StopAllAsync()
        {
            try
            {
                await _supervisor.StopAllAsync();
            }
            catch (Exception exception)
            {
                ShowError("Не удалось остановить все скрипты.", exception);
            }
        }

        public void ShowAbout()
        {
            using (var form = new AboutForm(_store.ConfigPath))
            {
                form.ShowDialog(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _supervisor.StateChanged -= HandleStateChanged;
                _supervisor.OutputReceived -= HandleOutputReceived;
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
            _grid.Columns.Add(Column("Name", "Название", 170));
            _grid.Columns.Add(Column("Type", "Тип", 65));
            _grid.Columns.Add(Column("Interpreter", "Интерпретатор", 150));
            _grid.Columns.Add(Column("AutoStart", "Авто", 55));
            _grid.Columns.Add(Column("State", "Состояние", 105));
            _grid.Columns.Add(Column("Pid", "PID", 70));
            _grid.Columns.Add(Column("Started", "Запущен", 130));
            _grid.Columns.Add(Column("ExitCode", "Код", 55));
            var pathColumn = Column("Path", "Путь", 260);
            pathColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns.Add(pathColumn);

            var context = new ContextMenuStrip();
            context.Items.Add("Запустить", null, (sender, args) => StartSelected());
            context.Items.Add("Остановить", null, async (sender, args) => await StopSelectedAsync());
            context.Items.Add(new ToolStripSeparator());
            context.Items.Add("Изменить запись", null, (sender, args) => EditSelected());
            context.Items.Add("Редактировать файл", null, (sender, args) => EditSelectedFile());
            context.Items.Add("Показать в папке", null, (sender, args) => ShowSelectedInFolder());
            context.Items.Add(new ToolStripSeparator());
            context.Items.Add("Удалить запись", null, async (sender, args) => await DeleteSelectedAsync());
            _grid.ContextMenuStrip = context;
        }

        private void RefreshGrid()
        {
            var selectedId = SelectedScript?.Id;
            var filter = _filter.Text?.Trim() ?? string.Empty;
            _grid.Rows.Clear();

            foreach (var script in Configuration.Scripts.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var type = Path.GetExtension(script.Path).TrimStart('.').ToUpperInvariant();
                if (filter.Length > 0 &&
                    script.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    script.Path.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    type.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var runtime = _supervisor.GetSnapshot(script.Id);
                var rowIndex = _grid.Rows.Add(
                    script.Name,
                    type,
                    InterpreterText(script),
                    script.Launch.AutoStartWithApplication ? "Да" : "Нет",
                    StateText(runtime),
                    runtime.ProcessId?.ToString() ?? "-",
                    runtime.StartedAt?.ToString("g") ?? "-",
                    runtime.LastExitCode?.ToString() ?? "-",
                    script.Path);
                var row = _grid.Rows[rowIndex];
                row.Tag = script.Id;
                if (!script.Enabled)
                {
                    row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                }

                if (runtime.State == ScriptRuntimeState.Failed)
                {
                    row.DefaultCellStyle.BackColor = Color.MistyRose;
                    row.Cells["State"].ToolTipText = runtime.Error;
                }

                if (selectedId == script.Id)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                }
            }

            UpdateButtons();
        }

        private void AddScript()
        {
            using (var form = new ScriptEditorForm(null, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath)))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var candidate = Configuration.Clone();
                candidate.Scripts.Add(form.Result);
                SaveConfiguration(candidate);
            }
        }

        private void EditSelected()
        {
            var selected = SelectedScript;
            if (selected == null)
            {
                return;
            }

            using (var form = new ScriptEditorForm(selected, Configuration.Defaults, Path.GetDirectoryName(_store.ConfigPath)))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var candidate = Configuration.Clone();
                var index = candidate.Scripts.FindIndex(item => item.Id == selected.Id);
                candidate.Scripts[index] = form.Result;
                SaveConfiguration(candidate);
            }
        }

        private async Task DeleteSelectedAsync()
        {
            var selected = SelectedScript;
            if (selected == null)
            {
                return;
            }

            if (_supervisor.IsRunning(selected.Id))
            {
                MessageBox.Show(this, "Сначала остановите скрипт, затем удалите запись.", "Удаление записи", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (Configuration.Application.ConfirmBeforeDelete)
            {
                var answer = MessageBox.Show(
                    this,
                    "Удалить запись «" + selected.Name + "»?\n\nСам файл скрипта удалён не будет.",
                    "Удаление записи",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            await Task.Yield();
            var candidate = Configuration.Clone();
            candidate.Scripts.RemoveAll(item => item.Id == selected.Id);
            SaveConfiguration(candidate);
            _outputByScript.Remove(selected.Id);
        }

        private void StartSelected()
        {
            var selected = SelectedScript;
            if (selected == null)
            {
                return;
            }

            if (!selected.Enabled)
            {
                MessageBox.Show(this, "Запись отключена. Включите её в редакторе перед запуском.", "Запуск скрипта", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _supervisor.Start(selected, Configuration.PowerShell7Path);
            }
            catch (Exception exception)
            {
                ShowError("Не удалось запустить «" + selected.Name + "».", exception);
            }
        }

        private async Task StopSelectedAsync()
        {
            var selected = SelectedScript;
            if (selected == null)
            {
                return;
            }

            try
            {
                await _supervisor.StopAsync(selected.Id);
            }
            catch (Exception exception)
            {
                ShowError("Не удалось остановить «" + selected.Name + "».", exception);
            }
        }

        private void EditSelectedFile()
        {
            var selected = SelectedScript;
            if (selected == null)
            {
                return;
            }

            try
            {
                _editor.Edit(selected.Path, Configuration.Application);
            }
            catch (Exception exception)
            {
                ShowError("Не удалось открыть редактор.", exception);
            }
        }

        private void ShowSelectedInFolder()
        {
            var selected = SelectedScript;
            if (selected == null)
            {
                return;
            }

            try
            {
                _editor.ShowInFolder(selected.Path);
            }
            catch (Exception exception)
            {
                ShowError("Не удалось открыть папку.", exception);
            }
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(Configuration.Application, Configuration.PowerShell7Path))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var candidate = Configuration.Clone();
                candidate.Application = form.SettingsResult;
                candidate.PowerShell7Path = form.PowerShell7PathResult;
                var previousStartup = Configuration.Application.StartWithWindows;

                try
                {
                    _startup.Synchronize(candidate.Application.StartWithWindows);
                    _store.Save(candidate);
                    _state.Current = candidate;
                    RefreshGrid();
                    MessageBox.Show(this, "Настройки сохранены. Новый срок хранения журналов применяется после перезапуска приложения.", "Настройки", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception exception)
                {
                    try
                    {
                        _startup.Synchronize(previousStartup);
                    }
                    catch (Exception rollbackException)
                    {
                        _log.Error("Unable to roll back the startup registration.", rollbackException);
                    }

                    ShowError("Не удалось сохранить настройки.", exception);
                }
            }
        }

        private void ReloadConfiguration()
        {
            if (_supervisor.HasRunningProcesses)
            {
                MessageBox.Show(this, "Перед перечитыванием INI остановите все скрипты.", "Перечитать INI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var reloaded = _store.Reload();
                _startup.Synchronize(reloaded.Application.StartWithWindows);
                _state.Current = reloaded;
                RefreshGrid();
                _log.Information("Configuration reloaded from disk.");
            }
            catch (Exception exception)
            {
                ShowError("Не удалось перечитать INI.", exception);
            }
        }

        private void SaveConfiguration(AppConfiguration candidate)
        {
            try
            {
                _store.Save(candidate);
                _state.Current = candidate;
                RefreshGrid();
            }
            catch (Exception exception)
            {
                ShowError("Не удалось сохранить INI.", exception);
            }
        }

        private void HandleStateChanged(object sender, ScriptStateChangedEventArgs args)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke((Action)RefreshGrid);
        }

        private void HandleOutputReceived(object sender, ScriptOutputEventArgs args)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke((Action)(() => AppendOutput(args)));
        }

        private void AppendOutput(ScriptOutputEventArgs args)
        {
            StringBuilder builder;
            if (!_outputByScript.TryGetValue(args.ScriptId, out builder))
            {
                builder = new StringBuilder();
                _outputByScript.Add(args.ScriptId, builder);
            }

            builder.Append('[').Append(args.ProcessId).Append(args.IsError ? " ERR] " : " OUT] ").AppendLine(args.Line);
            if (builder.Length > MaxOutputCharactersPerScript)
            {
                builder.Remove(0, builder.Length - MaxOutputCharactersPerScript);
            }

            if (SelectedScript?.Id == args.ScriptId)
            {
                _output.Text = builder.ToString();
                _output.SelectionStart = _output.TextLength;
                _output.ScrollToCaret();
            }
        }

        private void ShowSelectedOutput()
        {
            var selected = SelectedScript;
            StringBuilder builder;
            _output.Text = selected != null && _outputByScript.TryGetValue(selected.Id, out builder) ? builder.ToString() : string.Empty;
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        }

        private ScriptDefinition SelectedScript
        {
            get
            {
                if (_grid.SelectedRows.Count == 0 || !(_grid.SelectedRows[0].Tag is Guid))
                {
                    return null;
                }

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
                if (Configuration.Application.CloseToTray)
                {
                    Hide();
                }
                else
                {
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void ShowError(string message, Exception exception)
        {
            _log.Error(message, exception);
            MessageBox.Show(this, message + Environment.NewLine + Environment.NewLine + exception.Message, "CmdsManager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static ToolStripButton Button(string text, EventHandler click)
        {
            var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += click;
            return button;
        }

        private static DataGridViewTextBoxColumn Column(string name, string title, int width)
        {
            return new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.Automatic };
        }

        private static string InterpreterText(ScriptDefinition script)
        {
            var interpreter = script.Launch.Interpreter == ScriptInterpreter.Auto
                ? ScriptDefinitionValidator.ResolveAutoInterpreter(script.Path)
                : script.Launch.Interpreter;
            switch (interpreter)
            {
                case ScriptInterpreter.Cmd:
                    return "CMD";
                case ScriptInterpreter.WindowsPowerShell:
                    return "Windows PS 5.1";
                case ScriptInterpreter.PowerShell7:
                    return "PowerShell 7";
                case ScriptInterpreter.CScript:
                    return "cscript.exe";
                case ScriptInterpreter.WScript:
                    return "wscript.exe";
                default:
                    return interpreter.ToString();
            }
        }

        private static string StateText(ScriptRuntimeSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case ScriptRuntimeState.Starting:
                    return "Запуск…";
                case ScriptRuntimeState.Running:
                    return snapshot.ActiveCount > 1 ? "Работает (" + snapshot.ActiveCount + ")" : "Работает";
                case ScriptRuntimeState.Stopping:
                    return "Остановка…";
                case ScriptRuntimeState.Exited:
                    return "Завершён";
                case ScriptRuntimeState.Failed:
                    return "Ошибка";
                default:
                    return "Остановлен";
            }
        }
    }
}
