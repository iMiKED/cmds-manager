using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CmdsManager.Domain;

namespace CmdsManager.Presentation.Forms
{
    public sealed class SettingsForm : Form
    {
        private readonly CheckBox _startWithWindows = new CheckBox { Text = "Запускать CmdsManager при входе в Windows", AutoSize = true };
        private readonly CheckBox _startMinimized = new CheckBox { Text = "При ручном старте сразу скрывать в трей", AutoSize = true };
        private readonly CheckBox _autoStartScripts = new CheckBox { Text = "Запускать отмеченные скрипты при старте", AutoSize = true };
        private readonly CheckBox _confirmDelete = new CheckBox { Text = "Подтверждать удаление записи", AutoSize = true };
        private readonly CheckBox _logScriptOutput = new CheckBox { Text = "Записывать stdout/stderr в журнал (может содержать секреты)", AutoSize = true };
        private readonly TextBox _editorPath = new TextBox();
        private readonly TextBox _editorArguments = new TextBox();
        private readonly TextBox _powerShell7Path = new TextBox();
        private readonly NumericUpDown _retention = new NumericUpDown { Minimum = 1, Maximum = 3650 };

        public SettingsForm(ApplicationSettings settings, string powerShell7Path)
        {
            var source = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
            Text = "Настройки CmdsManager";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(720, 430);
            Icon = SystemIcons.Application;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(table, string.Empty, _startWithWindows);
            AddRow(table, string.Empty, _startMinimized);
            AddRow(table, string.Empty, _autoStartScripts);
            AddRow(table, string.Empty, _confirmDelete);
            AddRow(table, "Редактор", WithFileButton(_editorPath, "Приложения|*.exe|Все файлы|*.*"));
            AddRow(table, "Аргументы редактора", _editorArguments);
            AddRow(table, "Путь к pwsh.exe", WithFileButton(_powerShell7Path, "PowerShell 7|pwsh.exe|Приложения|*.exe"));
            AddRow(table, "Хранить журналы, дней", _retention);
            AddRow(table, string.Empty, _logScriptOutput);

            var warning = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                ForeColor = SystemColors.GrayText,
                Text = "Автозапуск записывается только для текущего пользователя и не требует прав администратора. Portable-папку после включения автозапуска лучше не перемещать."
            };
            AddRow(table, string.Empty, warning);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            var save = new Button { Text = "Сохранить", AutoSize = true };
            var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += SaveAndClose;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            AddRow(table, string.Empty, buttons);
            Controls.Add(table);

            _startWithWindows.Checked = source.StartWithWindows;
            _startMinimized.Checked = source.StartMinimized;
            _autoStartScripts.Checked = source.AutoStartScripts;
            _confirmDelete.Checked = source.ConfirmBeforeDelete;
            _editorPath.Text = source.EditorPath;
            _editorArguments.Text = source.EditorArguments;
            _powerShell7Path.Text = powerShell7Path ?? string.Empty;
            _retention.Value = Math.Min(_retention.Maximum, Math.Max(_retention.Minimum, source.LogRetentionDays));
            _logScriptOutput.Checked = source.LogScriptOutput;

            SettingsResult = source;
            AcceptButton = save;
            CancelButton = cancel;
        }

        public ApplicationSettings SettingsResult { get; private set; }
        public string PowerShell7PathResult { get; private set; }

        private void SaveAndClose(object sender, EventArgs args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_editorPath.Text))
                {
                    throw new InvalidOperationException("Укажите путь к редактору.");
                }

                var expandedEditor = Environment.ExpandEnvironmentVariables(_editorPath.Text.Trim());
                if (!File.Exists(expandedEditor))
                {
                    throw new FileNotFoundException("Редактор не найден.", expandedEditor);
                }

                var powerShellPath = Environment.ExpandEnvironmentVariables(_powerShell7Path.Text.Trim());
                if (powerShellPath.Length > 0 && !File.Exists(powerShellPath) && !Directory.Exists(powerShellPath))
                {
                    throw new FileNotFoundException("Указанный путь PowerShell 7 не найден.", powerShellPath);
                }

                SettingsResult.StartWithWindows = _startWithWindows.Checked;
                SettingsResult.StartMinimized = _startMinimized.Checked;
                SettingsResult.AutoStartScripts = _autoStartScripts.Checked;
                SettingsResult.ConfirmBeforeDelete = _confirmDelete.Checked;
                SettingsResult.EditorPath = _editorPath.Text.Trim();
                SettingsResult.EditorArguments = _editorArguments.Text.Trim();
                SettingsResult.LogRetentionDays = decimal.ToInt32(_retention.Value);
                SettingsResult.LogScriptOutput = _logScriptOutput.Checked;
                PowerShell7PathResult = _powerShell7Path.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Проверка настроек", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private Control WithFileButton(TextBox textBox, string filter)
        {
            textBox.Dock = DockStyle.Fill;
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var browse = new Button { Text = "Обзор…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            browse.Click += (sender, args) =>
            {
                using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        textBox.Text = dialog.FileName;
                    }
                }
            };
            panel.Controls.Add(textBox, 0, 0);
            panel.Controls.Add(browse, 1, 0);
            return panel;
        }

        private static void AddRow(TableLayoutPanel table, string labelText, Control control)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 8) }, 0, row);
            control.Dock = control is CheckBox || control is FlowLayoutPanel || control is Label ? DockStyle.Top : DockStyle.Fill;
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(control, 1, row);
        }
    }
}
