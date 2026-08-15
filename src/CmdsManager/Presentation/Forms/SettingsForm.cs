using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;

namespace CmdsManager.Presentation.Forms
{
    public sealed class SettingsForm : Form
    {
        private readonly LocalizationService _text;
        private readonly CheckBox _startWithWindows = new CheckBox { AutoSize = true };
        private readonly CheckBox _startMinimized = new CheckBox { AutoSize = true };
        private readonly CheckBox _autoStartScripts = new CheckBox { AutoSize = true };
        private readonly CheckBox _confirmDelete = new CheckBox { AutoSize = true };
        private readonly CheckBox _logScriptOutput = new CheckBox { AutoSize = true };
        private readonly TextBox _editorPath = new TextBox();
        private readonly TextBox _editorArguments = new TextBox();
        private readonly TextBox _powerShell7Path = new TextBox();
        private readonly NumericUpDown _retention = new NumericUpDown { Minimum = 1, Maximum = 3650 };
        private readonly ComboBox _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _fontDisplay = new TextBox { ReadOnly = true };
        private string _fontName;
        private float _fontSize;

        public SettingsForm(ApplicationSettings settings, string powerShell7Path, LocalizationSettings localization, LocalizationService text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            var source = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
            localization = localization ?? throw new ArgumentNullException(nameof(localization));
            Text = _text["Settings.Title"];
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(580, 350);
            Icon = SystemIcons.Application;

            _startWithWindows.Text = _text["Settings.StartWithWindows"];
            _startMinimized.Text = _text["Settings.StartMinimized"];
            _autoStartScripts.Text = _text["Settings.AutoStartScripts"];
            _confirmDelete.Text = _text["Settings.ConfirmDelete"];
            _logScriptOutput.Text = _text["Settings.LogOutput"];
            foreach (var code in localization.Languages.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                _language.Items.Add(new DisplayItem<string>(code, _text.GetForLanguage(code, "Language.Name")));

            var general = CreateTable();
            AddRow(general, _text["Settings.Language"], _language);
            AddRow(general, _text["Settings.ConsoleFont"], WithFontButton());
            AddRow(general, string.Empty, _startWithWindows);
            AddRow(general, string.Empty, _startMinimized);
            AddRow(general, string.Empty, _autoStartScripts);
            AddRow(general, string.Empty, _confirmDelete);
            AddRow(general, string.Empty, new Label
            {
                AutoSize = true,
                MaximumSize = new Size(390, 0),
                ForeColor = SystemColors.GrayText,
                Text = _text["Settings.Warning"]
            });

            var tools = CreateTable();
            AddRow(tools, _text["Settings.Editor"], WithFileButton(_editorPath, _text["Settings.AppFilter"]));
            AddRow(tools, _text["Settings.EditorArguments"], _editorArguments);
            AddRow(tools, _text["Settings.PowerShell7"], WithFileButton(_powerShell7Path, _text["Settings.PowerShellFilter"]));
            AddRow(tools, _text["Settings.Retention"], _retention);
            AddRow(tools, string.Empty, _logScriptOutput);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page(_text["Settings.Tab.General"], general));
            tabs.TabPages.Add(Page(_text["Settings.Tab.Tools"], tools));
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
            layout.Controls.Add(tabs, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);

            _startWithWindows.Checked = source.StartWithWindows;
            _startMinimized.Checked = source.StartMinimized;
            _autoStartScripts.Checked = source.AutoStartScripts;
            _confirmDelete.Checked = source.ConfirmBeforeDelete;
            _editorPath.Text = source.EditorPath;
            _editorArguments.Text = source.EditorArguments;
            _powerShell7Path.Text = powerShell7Path ?? string.Empty;
            _retention.Value = Math.Min(_retention.Maximum, Math.Max(_retention.Minimum, source.LogRetentionDays));
            _logScriptOutput.Checked = source.LogScriptOutput;
            _fontName = source.ConsoleFontName;
            _fontSize = source.ConsoleFontSize;
            UpdateFontDisplay();
            SelectValue(_language, localization.Language);

            SettingsResult = source;
            LanguageResult = localization.Language;
            AcceptButton = save;
            CancelButton = cancel;
        }

        public ApplicationSettings SettingsResult { get; private set; }
        public string PowerShell7PathResult { get; private set; }
        public string LanguageResult { get; private set; }

        private void SaveAndClose(object sender, EventArgs args)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_editorPath.Text)) throw new InvalidOperationException(_text["Settings.EditorRequired"]);
                var expandedEditor = Environment.ExpandEnvironmentVariables(_editorPath.Text.Trim());
                if (!File.Exists(expandedEditor)) throw new FileNotFoundException(_text["Settings.EditorMissing"], expandedEditor);
                var powerShellPath = Environment.ExpandEnvironmentVariables(_powerShell7Path.Text.Trim());
                if (powerShellPath.Length > 0 && !File.Exists(powerShellPath) && !Directory.Exists(powerShellPath))
                    throw new FileNotFoundException(_text["Settings.PowerShellMissing"], powerShellPath);

                SettingsResult.StartWithWindows = _startWithWindows.Checked;
                SettingsResult.StartMinimized = _startMinimized.Checked;
                SettingsResult.AutoStartScripts = _autoStartScripts.Checked;
                SettingsResult.ConfirmBeforeDelete = _confirmDelete.Checked;
                SettingsResult.EditorPath = _editorPath.Text.Trim();
                SettingsResult.EditorArguments = _editorArguments.Text.Trim();
                SettingsResult.LogRetentionDays = decimal.ToInt32(_retention.Value);
                SettingsResult.LogScriptOutput = _logScriptOutput.Checked;
                SettingsResult.ConsoleFontName = _fontName;
                SettingsResult.ConsoleFontSize = _fontSize;
                PowerShell7PathResult = _powerShell7Path.Text.Trim();
                LanguageResult = GetValue(_language, "ru");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, _text["Settings.ValidationTitle"], MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private Control WithFontButton()
        {
            _fontDisplay.Dock = DockStyle.Fill;
            var panel = TwoColumnPanel();
            var choose = new Button { Text = _text["Settings.ChooseFont"], AutoSize = true, Margin = new Padding(5, 0, 0, 0) };
            choose.Click += (sender, args) =>
            {
                Font current;
                try { current = new Font(_fontName, _fontSize); }
                catch (ArgumentException) { current = new Font(FontFamily.GenericMonospace, 10f); }
                using (current)
                using (var dialog = new FontDialog { Font = current, FixedPitchOnly = true, ShowEffects = false, MinSize = 6, MaxSize = 48 })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    _fontName = dialog.Font.Name;
                    _fontSize = dialog.Font.SizeInPoints;
                    UpdateFontDisplay();
                }
            };
            panel.Controls.Add(_fontDisplay, 0, 0);
            panel.Controls.Add(choose, 1, 0);
            return panel;
        }

        private Control WithFileButton(TextBox textBox, string filter)
        {
            textBox.Dock = DockStyle.Fill;
            var panel = TwoColumnPanel();
            var browse = new Button { Text = _text["Common.Browse"], AutoSize = true, Margin = new Padding(5, 0, 0, 0) };
            browse.Click += (sender, args) =>
            {
                using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK) textBox.Text = dialog.FileName;
                }
            };
            panel.Controls.Add(textBox, 0, 0);
            panel.Controls.Add(browse, 1, 0);
            return panel;
        }

        private void UpdateFontDisplay()
        {
            _fontDisplay.Text = _fontName + ", " + _fontSize.ToString("0.##") + " pt";
        }

        private static TableLayoutPanel CreateTable()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true, ColumnCount = 2, RowCount = 0 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }

        private static TableLayoutPanel TwoColumnPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return panel;
        }

        private static TabPage Page(string title, Control content)
        {
            var page = new TabPage(title) { Padding = Padding.Empty };
            page.Controls.Add(content);
            return page;
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
    }
}
