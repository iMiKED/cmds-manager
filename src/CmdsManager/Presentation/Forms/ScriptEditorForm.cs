using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Execution;

namespace CmdsManager.Presentation.Forms
{
    public sealed class ScriptEditorForm : Form
    {
        private readonly Guid _id;
        private readonly ScriptCommandBuilder _paths;
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _path = new TextBox();
        private readonly CheckBox _enabled = new CheckBox { Text = "Запись активна", AutoSize = true };
        private readonly ComboBox _interpreter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _arguments = new TextBox();
        private readonly TextBox _workingDirectory = new TextBox();
        private readonly ComboBox _windowMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _captureOutput = new CheckBox { Text = "Перехватывать stdout/stderr", AutoSize = true };
        private readonly CheckBox _allowParallel = new CheckBox { Text = "Разрешить параллельные экземпляры", AutoSize = true };
        private readonly CheckBox _autoStart = new CheckBox { Text = "Запускать при старте CmdsManager", AutoSize = true };
        private readonly NumericUpDown _autoStartOrder = new NumericUpDown { Minimum = -100000, Maximum = 100000 };
        private readonly NumericUpDown _autoStartDelay = new NumericUpDown { Minimum = 0, Maximum = 86400 };
        private readonly ComboBox _stopPolicy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _stopTimeout = new NumericUpDown { Minimum = 0, Maximum = 3600 };

        public ScriptEditorForm(ScriptDefinition source, LaunchProfile defaults, string configurationDirectory)
        {
            _paths = new ScriptCommandBuilder(configurationDirectory);
            var model = source?.Clone() ?? new ScriptDefinition { Launch = defaults?.Clone() ?? new LaunchProfile() };
            _id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;

            Text = source == null ? "Добавление скрипта" : "Редактирование скрипта";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(720, 650);
            Icon = SystemIcons.Application;

            FillCombo(_windowMode,
                new DisplayItem<ScriptWindowMode>(ScriptWindowMode.Hidden, "Скрыто"),
                new DisplayItem<ScriptWindowMode>(ScriptWindowMode.Normal, "Обычное окно"),
                new DisplayItem<ScriptWindowMode>(ScriptWindowMode.Minimized, "Свёрнуто"));
            FillCombo(_stopPolicy,
                new DisplayItem<ScriptStopPolicy>(ScriptStopPolicy.GracefulThenKill, "Корректно, затем принудительно"),
                new DisplayItem<ScriptStopPolicy>(ScriptStopPolicy.Kill, "Сразу принудительно"));

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                AutoScroll = true,
                ColumnCount = 2,
                RowCount = 0
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(table, "Название", _name);
            AddRow(table, "Файл", WithButton(_path, "Обзор…", BrowseScript));
            AddRow(table, string.Empty, _enabled);
            AddRow(table, "Интерпретатор", _interpreter);
            AddRow(table, "Аргументы", _arguments);
            AddRow(table, "Рабочая папка", WithButton(_workingDirectory, "Обзор…", BrowseWorkingDirectory));
            AddRow(table, "Режим окна", _windowMode);
            AddRow(table, string.Empty, _captureOutput);
            AddRow(table, string.Empty, _allowParallel);
            AddRow(table, string.Empty, _autoStart);
            AddRow(table, "Порядок автозапуска", _autoStartOrder);
            AddRow(table, "Задержка автозапуска, с", _autoStartDelay);
            AddRow(table, "Остановка", _stopPolicy);
            AddRow(table, "Таймаут остановки, с", _stopTimeout);

            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                ForeColor = SystemColors.GrayText,
                Text = "Повышение прав в первой версии не поддерживается. Удаление записи не удаляет исходный файл."
            };
            AddRow(table, string.Empty, note);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            var ok = new Button { Text = "Сохранить", AutoSize = true };
            var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += SaveAndClose;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            AddRow(table, string.Empty, buttons);

            Controls.Add(table);
            AcceptButton = ok;
            CancelButton = cancel;

            _name.Text = model.Name;
            _path.Text = model.Path;
            _enabled.Checked = model.Enabled;
            _arguments.Text = model.Launch.Arguments;
            _workingDirectory.Text = model.Launch.WorkingDirectory;
            _captureOutput.Checked = model.Launch.CaptureOutput;
            _allowParallel.Checked = model.Launch.AllowParallelInstances;
            _autoStart.Checked = model.Launch.AutoStartWithApplication;
            _autoStartOrder.Value = Clamp(model.Launch.AutoStartOrder, _autoStartOrder.Minimum, _autoStartOrder.Maximum);
            _autoStartDelay.Value = Clamp(model.Launch.AutoStartDelaySeconds, _autoStartDelay.Minimum, _autoStartDelay.Maximum);
            _stopTimeout.Value = Clamp(model.Launch.StopTimeoutSeconds, _stopTimeout.Minimum, _stopTimeout.Maximum);
            SelectValue(_windowMode, model.Launch.WindowMode);
            SelectValue(_stopPolicy, model.Launch.StopPolicy);
            RefreshInterpreterItems(model.Launch.Interpreter);

            _path.TextChanged += (sender, args) => RefreshInterpreterItems(GetValue(_interpreter, ScriptInterpreter.Auto));
            _interpreter.SelectedIndexChanged += (sender, args) => UpdateCaptureAvailability();
            UpdateCaptureAvailability();
        }

        public ScriptDefinition Result { get; private set; }

        private void SaveAndClose(object sender, EventArgs eventArgs)
        {
            try
            {
                var candidate = new ScriptDefinition
                {
                    Id = _id,
                    Name = _name.Text.Trim(),
                    Enabled = _enabled.Checked,
                    Path = _path.Text.Trim(),
                    Launch = new LaunchProfile
                    {
                        Interpreter = GetValue(_interpreter, ScriptInterpreter.Auto),
                        Arguments = _arguments.Text.Trim(),
                        WorkingDirectory = _workingDirectory.Text.Trim(),
                        WindowMode = GetValue(_windowMode, ScriptWindowMode.Hidden),
                        CaptureOutput = _captureOutput.Checked,
                        AllowParallelInstances = _allowParallel.Checked,
                        AutoStartWithApplication = _autoStart.Checked,
                        AutoStartOrder = decimal.ToInt32(_autoStartOrder.Value),
                        AutoStartDelaySeconds = decimal.ToInt32(_autoStartDelay.Value),
                        StopPolicy = GetValue(_stopPolicy, ScriptStopPolicy.GracefulThenKill),
                        StopTimeoutSeconds = decimal.ToInt32(_stopTimeout.Value)
                    }
                };

                ScriptDefinitionValidator.Validate(candidate, false);
                var resolvedPath = _paths.ResolvePath(candidate.Path);
                if (!File.Exists(resolvedPath))
                {
                    throw new FileNotFoundException("Файл скрипта не найден.", resolvedPath);
                }

                if (!string.IsNullOrWhiteSpace(candidate.Launch.WorkingDirectory) && !Directory.Exists(_paths.ResolvePath(candidate.Launch.WorkingDirectory)))
                {
                    throw new DirectoryNotFoundException("Рабочая папка не найдена.");
                }

                Result = candidate;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Проверка записи", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BrowseScript(object sender, EventArgs args)
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Поддерживаемые скрипты|*.cmd;*.bat;*.ps1;*.vbs|Все файлы|*.*",
                CheckFileExists = true,
                Multiselect = false,
                Title = "Выберите скрипт"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _path.Text = dialog.FileName;
                    if (string.IsNullOrWhiteSpace(_name.Text))
                    {
                        _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }
                }
            }
        }

        private void BrowseWorkingDirectory(object sender, EventArgs args)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Выберите рабочую папку" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _workingDirectory.Text = dialog.SelectedPath;
                }
            }
        }

        private void RefreshInterpreterItems(ScriptInterpreter preferred)
        {
            var extension = Path.GetExtension(_path.Text.Trim());
            var values = new[]
            {
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.Auto, "Автоматически"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.Cmd, "CMD"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.WindowsPowerShell, "Windows PowerShell 5.1"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.PowerShell7, "PowerShell 7"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.CScript, "VBS — cscript.exe"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.WScript, "VBS — wscript.exe")
            };

            var allowed = values.Where(item =>
                item.Value == ScriptInterpreter.Auto ||
                (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)) && item.Value == ScriptInterpreter.Cmd ||
                extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && (item.Value == ScriptInterpreter.WindowsPowerShell || item.Value == ScriptInterpreter.PowerShell7) ||
                extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase) && (item.Value == ScriptInterpreter.CScript || item.Value == ScriptInterpreter.WScript) ||
                string.IsNullOrEmpty(extension)).ToArray();

            _interpreter.BeginUpdate();
            _interpreter.Items.Clear();
            _interpreter.Items.AddRange(allowed);
            SelectValue(_interpreter, allowed.Any(item => Equals(item.Value, preferred)) ? preferred : ScriptInterpreter.Auto);
            _interpreter.EndUpdate();
            UpdateCaptureAvailability();
        }

        private void UpdateCaptureAvailability()
        {
            var wscript = GetValue(_interpreter, ScriptInterpreter.Auto) == ScriptInterpreter.WScript;
            if (wscript)
            {
                _captureOutput.Checked = false;
            }

            _captureOutput.Enabled = !wscript;
        }

        private static Control WithButton(TextBox textBox, string buttonText, EventHandler click)
        {
            textBox.Dock = DockStyle.Fill;
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var button = new Button { Text = buttonText, AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            button.Click += click;
            panel.Controls.Add(textBox, 0, 0);
            panel.Controls.Add(button, 1, 0);
            return panel;
        }

        private static void AddRow(TableLayoutPanel table, string labelText, Control control)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 8) };
            control.Dock = control is CheckBox || control is FlowLayoutPanel || control is Label ? DockStyle.Top : DockStyle.Fill;
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private static void FillCombo<T>(ComboBox combo, params DisplayItem<T>[] values)
        {
            combo.Items.AddRange(values.Cast<object>().ToArray());
        }

        private static void SelectValue<T>(ComboBox combo, T value)
        {
            for (var index = 0; index < combo.Items.Count; index++)
            {
                var item = combo.Items[index] as DisplayItem<T>;
                if (item != null && Equals(item.Value, value))
                {
                    combo.SelectedIndex = index;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static T GetValue<T>(ComboBox combo, T fallback)
        {
            var item = combo.SelectedItem as DisplayItem<T>;
            return item == null ? fallback : item.Value;
        }

        private static decimal Clamp(int value, decimal minimum, decimal maximum)
        {
            return Math.Min(maximum, Math.Max(minimum, value));
        }
    }
}
