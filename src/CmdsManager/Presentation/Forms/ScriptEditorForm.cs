using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Execution;

namespace CmdsManager.Presentation.Forms
{
    public sealed class ScriptEditorForm : Form
    {
        private readonly Guid _id;
        private readonly ScriptCommandBuilder _paths;
        private readonly LocalizationService _text;
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _path = new TextBox();
        private readonly CheckBox _enabled = new CheckBox { AutoSize = true };
        private readonly ComboBox _interpreter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _arguments = new TextBox();
        private readonly TextBox _workingDirectory = new TextBox();
        private readonly ComboBox _windowMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _captureOutput = new CheckBox { AutoSize = true };
        private readonly ComboBox _outputEncoding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _allowParallel = new CheckBox { AutoSize = true };
        private readonly CheckBox _autoStart = new CheckBox { AutoSize = true };
        private readonly NumericUpDown _autoStartOrder = new NumericUpDown { Minimum = -100000, Maximum = 100000, Width = 58, TextAlign = HorizontalAlignment.Right };
        private readonly NumericUpDown _autoStartDelay = new NumericUpDown { Minimum = 0, Maximum = 86400, Width = 58, TextAlign = HorizontalAlignment.Right };
        private readonly ComboBox _stopPolicy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _stopTimeout = new NumericUpDown { Minimum = 0, Maximum = 3600, Width = 58, TextAlign = HorizontalAlignment.Right };

        public ScriptEditorForm(ScriptDefinition source, LaunchProfile defaults, string configurationDirectory, LocalizationService text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _paths = new ScriptCommandBuilder(configurationDirectory);
            var model = source?.Clone() ?? new ScriptDefinition { Launch = defaults?.Clone() ?? new LaunchProfile() };
            _id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;

            Text = source == null ? _text["Script.Title.Add"] : _text["Script.Title.Edit"];
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(560, 465);
            Icon = ApplicationResources.Icon;

            _enabled.Text = _text["Script.Enabled"];
            _captureOutput.Text = _text["Script.Capture"];
            _allowParallel.Text = _text["Script.Parallel"];
            _autoStart.Text = _text["Script.AutoStart"];
            FillCombo(_windowMode,
                new DisplayItem<ScriptWindowMode>(ScriptWindowMode.Hidden, _text["Script.Window.Hidden"]),
                new DisplayItem<ScriptWindowMode>(ScriptWindowMode.Normal, _text["Script.Window.Normal"]),
                new DisplayItem<ScriptWindowMode>(ScriptWindowMode.Minimized, _text["Script.Window.Minimized"]));
            FillCombo(_stopPolicy,
                new DisplayItem<ScriptStopPolicy>(ScriptStopPolicy.GracefulThenKill, _text["Script.Stop.Graceful"]),
                new DisplayItem<ScriptStopPolicy>(ScriptStopPolicy.Kill, _text["Script.Stop.Kill"]));
            FillCombo(_outputEncoding,
                new DisplayItem<ScriptOutputEncoding>(ScriptOutputEncoding.Auto, _text["Script.Encoding.Auto"]),
                new DisplayItem<ScriptOutputEncoding>(ScriptOutputEncoding.Utf8, _text["Script.Encoding.Utf8"]),
                new DisplayItem<ScriptOutputEncoding>(ScriptOutputEncoding.Oem, _text["Script.Encoding.Oem"]),
                new DisplayItem<ScriptOutputEncoding>(ScriptOutputEncoding.Windows1251, _text["Script.Encoding.Windows1251"]),
                new DisplayItem<ScriptOutputEncoding>(ScriptOutputEncoding.Utf16LittleEndian, _text["Script.Encoding.Utf16"]));

            var content = CreateTable();
            AddRow(content, _text["Script.Name"], NameAndEnabled());
            AddRow(content, _text["Script.File"], WithButton(_path, _text["Common.Browse"], BrowseScript));
            AddRow(content, _text["Script.Interpreter"], _interpreter);
            AddRow(content, _text["Script.Arguments"], _arguments);
            AddRow(content, _text["Script.WorkingDirectory"], WithButton(_workingDirectory, _text["Common.Browse"], BrowseWorkingDirectory));
            AddRow(content, _text["Script.WindowMode"], _windowMode);
            AddRow(content, _text["Script.StopPolicy"], _stopPolicy);
            AddRow(content, string.Empty, _captureOutput);
            AddRow(content, _text["Script.Encoding"], _outputEncoding);
            AddRow(content, string.Empty, _allowParallel);
            AddRow(content, string.Empty, _autoStart);
            AddRow(content, string.Empty, CompactNumericControls());
            AddFullRow(content, new Label
            {
                AutoSize = true,
                MaximumSize = new Size(500, 0),
                ForeColor = SystemColors.GrayText,
                Text = _text["Script.Note"]
            });

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(0, 4, 6, 5)
            };
            var save = new Button { Text = _text["Common.Save"], AutoSize = true };
            var cancel = new Button { Text = _text["Common.Cancel"], AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += SaveAndClose;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(content, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = save;
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
            SelectValue(_outputEncoding, model.Launch.OutputEncoding);
            RefreshInterpreterItems(model.Launch.Interpreter);

            _path.TextChanged += (sender, args) => RefreshInterpreterItems(GetValue(_interpreter, ScriptInterpreter.Auto));
            _interpreter.SelectedIndexChanged += (sender, args) => UpdateCaptureAvailability();
            _captureOutput.CheckedChanged += (sender, args) => UpdateCaptureAvailability();
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
                        OutputEncoding = GetValue(_outputEncoding, ScriptOutputEncoding.Auto),
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
                if (!File.Exists(resolvedPath)) throw new FileNotFoundException(_text["Script.FileMissing"], resolvedPath);
                if (!string.IsNullOrWhiteSpace(candidate.Launch.WorkingDirectory) && !Directory.Exists(_paths.ResolvePath(candidate.Launch.WorkingDirectory)))
                    throw new DirectoryNotFoundException(_text["Script.DirectoryMissing"]);

                Result = candidate;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, _text["Script.ValidationTitle"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BrowseScript(object sender, EventArgs args)
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = _text["Script.FileFilter"],
                CheckFileExists = true,
                Multiselect = false,
                Title = _text["Script.SelectFile"]
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _path.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }

        private void BrowseWorkingDirectory(object sender, EventArgs args)
        {
            using (var dialog = new FolderBrowserDialog { Description = _text["Script.SelectDirectory"] })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _workingDirectory.Text = dialog.SelectedPath;
            }
        }

        private void RefreshInterpreterItems(ScriptInterpreter preferred)
        {
            var extension = Path.GetExtension(_path.Text.Trim());
            var values = new[]
            {
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.Auto, _text["Script.Interpreter.Auto"]),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.Cmd, "CMD"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.WindowsPowerShell, "Windows PowerShell 5.1"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.PowerShell7, "PowerShell 7"),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.CScript, _text["Script.Interpreter.VbsConsole"]),
                new DisplayItem<ScriptInterpreter>(ScriptInterpreter.WScript, _text["Script.Interpreter.VbsWindow"])
            };
            var allowed = values.Where(item => item.Value == ScriptInterpreter.Auto ||
                ((extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)) && item.Value == ScriptInterpreter.Cmd) ||
                (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) && (item.Value == ScriptInterpreter.WindowsPowerShell || item.Value == ScriptInterpreter.PowerShell7)) ||
                (extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase) && (item.Value == ScriptInterpreter.CScript || item.Value == ScriptInterpreter.WScript)) ||
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
            if (wscript) _captureOutput.Checked = false;
            _captureOutput.Enabled = !wscript;
            _outputEncoding.Enabled = !wscript && _captureOutput.Checked;
        }

        private static TableLayoutPanel CreateTable()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = false, ColumnCount = 2, RowCount = 0 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private Control NameAndEnabled()
        {
            _name.Dock = DockStyle.Fill;
            _name.Margin = new Padding(0, 2, 0, 3);
            _enabled.Margin = new Padding(8, 5, 2, 2);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(_name, 0, 0);
            panel.Controls.Add(_enabled, 1, 0);
            return panel;
        }

        private Control CompactNumericControls()
        {
            var panel = HorizontalPanel();
            AddCompactControl(panel, _text["Script.AutoStartOrder"], _autoStartOrder);
            AddCompactControl(panel, _text["Script.AutoStartDelay"], _autoStartDelay);
            AddCompactControl(panel, _text["Script.StopTimeout"], _stopTimeout);
            return panel;
        }

        private static FlowLayoutPanel HorizontalPanel()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
        }

        private static Label InlineLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(2, 7, 1, 2) };
        }

        private static void AddCompactControl(FlowLayoutPanel panel, string label, NumericUpDown control)
        {
            panel.Controls.Add(InlineLabel(label));
            control.Margin = new Padding(3, 2, 7, 2);
            panel.Controls.Add(control);
        }

        private static Control WithButton(TextBox textBox, string buttonText, EventHandler click)
        {
            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 2, 0, 3);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var button = new Button { Text = buttonText, AutoSize = true, Margin = new Padding(5, 0, 0, 0) };
            button.Click += click;
            panel.Controls.Add(textBox, 0, 0);
            panel.Controls.Add(button, 1, 0);
            return panel;
        }

        private static void AddRow(TableLayoutPanel table, string labelText, Control control)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 6, 6, 5) }, 0, row);
            control.Dock = control is CheckBox || control is Label ? DockStyle.Top : DockStyle.Fill;
            control.Margin = new Padding(2, 2, 2, 3);
            table.Controls.Add(control, 1, row);
        }

        private static void AddFullRow(TableLayoutPanel table, Control control)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = control is Label ? DockStyle.Top : DockStyle.Fill;
            control.Margin = new Padding(2, 2, 2, 3);
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
        }

        private static void FillCombo<T>(ComboBox combo, params DisplayItem<T>[] values) { combo.Items.AddRange(values.Cast<object>().ToArray()); }
        private static void SelectValue<T>(ComboBox combo, T value)
        {
            for (var index = 0; index < combo.Items.Count; index++)
            {
                var item = combo.Items[index] as DisplayItem<T>;
                if (item != null && Equals(item.Value, value)) { combo.SelectedIndex = index; return; }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }
        private static T GetValue<T>(ComboBox combo, T fallback)
        {
            var item = combo.SelectedItem as DisplayItem<T>;
            return item == null ? fallback : item.Value;
        }
        private static decimal Clamp(int value, decimal minimum, decimal maximum) { return Math.Min(maximum, Math.Max(minimum, value)); }
    }
}
