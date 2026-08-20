using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Windows;
using CmdsManager.Presentation.Controls;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    public sealed class SettingsForm : Form
    {
        private const int InlineActionButtonWidth = 88;
        private sealed class HotkeyEditorRow
        {
            internal CheckBox Enabled { get; set; }
            internal FluentHotkeyBox Gesture { get; set; }
            internal FluentButton Reset { get; set; }
        }

        private readonly LocalizationService _text;
        private readonly CheckBox _startWithWindows = new FluentCheckBox();
        private readonly CheckBox _startMinimized = new FluentCheckBox();
        private readonly CheckBox _autoStartScripts = new FluentCheckBox();
        private readonly CheckBox _confirmDelete = new FluentCheckBox();
        private readonly CheckBox _logScriptOutput = new FluentCheckBox();
        private readonly CheckBox _consoleAutoRecord = new FluentCheckBox();
        private readonly Dictionary<HotkeyAction, HotkeyEditorRow> _hotkeyEditors =
            new Dictionary<HotkeyAction, HotkeyEditorRow>();
        private readonly FluentTextBox _editorPath = new FluentTextBox();
        private readonly FluentTextBox _editorArguments = new FluentTextBox();
        private readonly FluentTextBox _powerShell7Path = new FluentTextBox();
        private readonly FluentNumericUpDown _retention = new FluentNumericUpDown
        {
            Minimum = 1,
            Maximum = 3650,
            Width = 72,
            TextAlign = HorizontalAlignment.Right
        };
        private readonly FluentNumericUpDown _consoleBufferSize = new FluentNumericUpDown
        {
            Minimum = 64,
            Maximum = 1048576,
            Width = 92,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right
        };
        private readonly FluentNumericUpDown _consoleLogMaxSize = new FluentNumericUpDown
        {
            Minimum = 1,
            Maximum = 4096,
            Width = 76,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right
        };
        private readonly ComboBox _language = new FluentComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _theme = new FluentComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly FluentTextBox _fontDisplay = new FluentTextBox { ReadOnly = true };
        private readonly Button _consoleTextColor = ColorButton();
        private readonly Button _consoleBackgroundColor = ColorButton();
        private readonly Button _tabTextColor = ColorButton();
        private readonly Button _activeTabTextColor = ColorButton();
        private readonly Button _tabBackgroundColor = ColorButton();
        private readonly Button _activeTabBackgroundColor = ColorButton();
        private readonly FluentNumericUpDown _consoleOpacity = OpacityField();
        private readonly FluentNumericUpDown _tabOpacity = OpacityField();
        private readonly FluentNumericUpDown _activeTabOpacity = OpacityField();
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
            ClientSize = new Size(520, 330);
            Icon = ApplicationResources.Icon;

            _startWithWindows.Text = _text["Settings.StartWithWindows"];
            _startMinimized.Text = _text["Settings.StartMinimized"];
            _autoStartScripts.Text = _text["Settings.AutoStartScripts"];
            _confirmDelete.Text = _text["Settings.ConfirmDelete"];
            _logScriptOutput.Text = _text["Settings.LogOutput"];
            _consoleAutoRecord.Text = _text["Settings.ConsoleAutoRecord"];
            foreach (var code in localization.Languages.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                _language.Items.Add(new DisplayItem<string>(code, _text.GetForLanguage(code, "Language.Name")));
            _theme.Items.Add(new DisplayItem<ApplicationTheme>(ApplicationTheme.System, _text["Theme.System"]));
            _theme.Items.Add(new DisplayItem<ApplicationTheme>(ApplicationTheme.Light, _text["Theme.Light"]));
            _theme.Items.Add(new DisplayItem<ApplicationTheme>(ApplicationTheme.Dark, _text["Theme.Dark"]));

            var general = CreateTable();
            AddRow(general, _text["Settings.Language"], _language);
            AddRow(general, _text["Settings.Theme"], _theme);
            AddRow(general, _text["Settings.ConsoleFont"], WithFontButton());
            AddRow(general, string.Empty, _startWithWindows);
            AddRow(general, string.Empty, _startMinimized);
            AddRow(general, string.Empty, _autoStartScripts);
            AddRow(general, string.Empty, _confirmDelete);

            var tools = CreateTable();
            AddRow(tools, _text["Settings.Editor"], WithFileButton(_editorPath, _text["Settings.AppFilter"]));
            AddRow(tools, _text["Settings.EditorArguments"], _editorArguments);
            AddRow(tools, _text["Settings.PowerShell7"], WithFileButton(_powerShell7Path, _text["Settings.PowerShellFilter"]));
            AddRow(tools, _text["Settings.Retention"], _retention);
            AddFullRow(tools, _logScriptOutput);

            var console = CreateTable(190);
            AddRow(console, _text["Settings.ConsoleBufferSize"], _consoleBufferSize);
            AddRow(console, _text["Settings.ConsoleLogMaxSize"], _consoleLogMaxSize);
            AddFullRow(console, _consoleAutoRecord);
            AddFiller(console);

            SetColor(_consoleTextColor, ConsoleAppearance.ParseColor(source.ConsoleForegroundColor, Color.Gainsboro));
            SetColor(_consoleBackgroundColor, ConsoleAppearance.ParseColor(source.ConsoleBackgroundColor, Color.FromArgb(28, 28, 28)));
            SetColor(_tabTextColor, ConsoleAppearance.ParseColor(source.ConsoleTabForegroundColor, Color.FromArgb(38, 43, 50)));
            SetColor(_activeTabTextColor, ConsoleAppearance.ParseColor(source.ConsoleActiveTabForegroundColor, Color.FromArgb(245, 247, 250)));
            SetColor(_tabBackgroundColor, ConsoleAppearance.ParseColor(source.ConsoleTabBackgroundColor, Color.FromArgb(252, 252, 253)));
            SetColor(_activeTabBackgroundColor, ConsoleAppearance.ParseColor(source.ConsoleActiveTabBackgroundColor, Color.FromArgb(28, 28, 28)));
            _consoleOpacity.Value = source.ConsoleBackgroundOpacity;
            _tabOpacity.Value = source.ConsoleTabBackgroundOpacity;
            _activeTabOpacity.Value = source.ConsoleActiveTabBackgroundOpacity;

            var appearance = CreateTable(174);
            AddRow(appearance, _text["Settings.ConsoleTextColor"], ColorOnly(_consoleTextColor));
            AddRow(appearance, _text["Settings.ConsoleBackground"], ColorWithOpacity(_consoleBackgroundColor, _consoleOpacity));
            AddRow(appearance, _text["Settings.TabTextColor"], ColorOnly(_tabTextColor));
            AddRow(appearance, _text["Settings.ActiveTabTextColor"], ColorOnly(_activeTabTextColor));
            AddRow(appearance, _text["Settings.TabBackground"], ColorWithOpacity(_tabBackgroundColor, _tabOpacity));
            AddRow(appearance, _text["Settings.ActiveTabBackground"], ColorWithOpacity(_activeTabBackgroundColor, _activeTabOpacity));
            AddFiller(appearance);

            var hotkeys = CreateHotkeyTabs(source.Hotkeys);

            var tabs = new FluentTabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page(_text["Settings.Tab.General"], general));
            tabs.TabPages.Add(Page(_text["Settings.Tab.Console"], console));
            tabs.TabPages.Add(Page(_text["Settings.Tab.Appearance"], appearance));
            tabs.TabPages.Add(Page(_text["Settings.Tab.Hotkeys"], hotkeys));
            tabs.TabPages.Add(Page(_text["Settings.Tab.Tools"], tools));
            var save = FluentDialogButtons.Primary(_text["Common.Save"]);
            var cancel = FluentDialogButtons.Secondary(_text["Common.Cancel"], DialogResult.Cancel);
            save.Click += SaveAndClose;
            var buttons = FluentDialogButtons.Footer(save, cancel);
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
            _consoleBufferSize.Value = Math.Min(_consoleBufferSize.Maximum,
                Math.Max(_consoleBufferSize.Minimum, source.ConsoleBufferSizeKb));
            _consoleLogMaxSize.Value = Math.Min(_consoleLogMaxSize.Maximum,
                Math.Max(_consoleLogMaxSize.Minimum, source.ConsoleLogMaxSizeMb));
            _consoleAutoRecord.Checked = source.ConsoleAutoRecord;
            _fontName = source.ConsoleFontName;
            _fontSize = source.ConsoleFontSize;
            UpdateFontDisplay();
            SelectValue(_language, localization.Language);
            SelectValue(_theme, source.Theme);
            _theme.SelectedIndexChanged += (sender, args) =>
                AppThemeManager.ApplyWindow(this, GetValue(_theme, ApplicationTheme.System));

            SettingsResult = source;
            LanguageResult = localization.Language;
            AcceptButton = save;
            CancelButton = cancel;
            AppThemeManager.ApplyWindow(this, source.Theme);
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
                SettingsResult.Hotkeys = ReadHotkeySettings();
                SettingsResult.EditorPath = _editorPath.Text.Trim();
                SettingsResult.EditorArguments = _editorArguments.Text.Trim();
                SettingsResult.LogRetentionDays = decimal.ToInt32(_retention.Value);
                SettingsResult.LogScriptOutput = _logScriptOutput.Checked;
                SettingsResult.ConsoleBufferSizeKb = decimal.ToInt32(_consoleBufferSize.Value);
                SettingsResult.ConsoleLogMaxSizeMb = decimal.ToInt32(_consoleLogMaxSize.Value);
                SettingsResult.ConsoleAutoRecord = _consoleAutoRecord.Checked;
                SettingsResult.Theme = GetValue(_theme, ApplicationTheme.System);
                SettingsResult.ConsoleFontName = _fontName;
                SettingsResult.ConsoleFontSize = _fontSize;
                SettingsResult.ConsoleForegroundColor = ConsoleAppearance.ToHex(_consoleTextColor.BackColor);
                SettingsResult.ConsoleBackgroundColor = ConsoleAppearance.ToHex(_consoleBackgroundColor.BackColor);
                SettingsResult.ConsoleBackgroundOpacity = decimal.ToInt32(_consoleOpacity.Value);
                SettingsResult.ConsoleTabForegroundColor = ConsoleAppearance.ToHex(_tabTextColor.BackColor);
                SettingsResult.ConsoleActiveTabForegroundColor = ConsoleAppearance.ToHex(_activeTabTextColor.BackColor);
                SettingsResult.ConsoleTabBackgroundColor = ConsoleAppearance.ToHex(_tabBackgroundColor.BackColor);
                SettingsResult.ConsoleTabBackgroundOpacity = decimal.ToInt32(_tabOpacity.Value);
                SettingsResult.ConsoleActiveTabBackgroundColor = ConsoleAppearance.ToHex(_activeTabBackgroundColor.BackColor);
                SettingsResult.ConsoleActiveTabBackgroundOpacity = decimal.ToInt32(_activeTabOpacity.Value);
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
            _fontDisplay.Margin = Padding.Empty;
            _fontDisplay.MinimumSize = new Size(120, 29);
            var panel = TwoColumnPanel();
            var choose = new FluentButton { Text = _text["Settings.ChooseFont"] };
            ConfigureInlineActionButton(choose);
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

        private static void ConfigureInlineActionButton(FluentButton button)
        {
            button.AutoSize = false;
            button.Size = new Size(InlineActionButtonWidth, 29);
            button.Margin = new Padding(5, 0, 0, 0);
        }

        private Control CreateHotkeyTabs(HotkeySettings source)
        {
            source = source?.Clone() ?? new HotkeySettings();
            var tabs = new FluentTabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page(_text["Settings.Hotkeys.Global"], CreateHotkeyTable(source,
                HotkeyAction.ShowApp, HotkeyAction.QuickLaunch, HotkeyAction.EmergencyStopAll)));
            tabs.TabPages.Add(Page(_text["Settings.Hotkeys.Scripts"], CreateHotkeyTable(source,
                HotkeyAction.StartSelected, HotkeyAction.StopSelected, HotkeyAction.RestartSelected,
                HotkeyAction.AddScript, HotkeyAction.EditScript, HotkeyAction.DeleteScript)));
            tabs.TabPages.Add(Page(_text["Settings.Hotkeys.Window"], CreateHotkeyTable(source,
                HotkeyAction.OpenSettings, HotkeyAction.NextConsoleTab, HotkeyAction.PreviousConsoleTab,
                HotkeyAction.CloseConsoleTab, HotkeyAction.ToggleConsoleDetach, HotkeyAction.ToggleConsolePane)));
            tabs.TabPages.Add(Page(_text["Settings.Hotkeys.Console"], CreateHotkeyTable(source,
                HotkeyAction.FindConsole, HotkeyAction.FindNext, HotkeyAction.FindPrevious,
                HotkeyAction.ToggleScrollLock, HotkeyAction.ToggleConsoleFullScreen,
                HotkeyAction.ToggleWordWrap, HotkeyAction.ClearConsole)));
            tabs.TabPages.Add(Page(_text["Settings.Hotkeys.Text"], CreateHotkeyTable(source,
                HotkeyAction.CopyConsoleSelection, HotkeyAction.SelectAllConsole, HotkeyAction.SaveConsole,
                HotkeyAction.IncreaseConsoleFont, HotkeyAction.DecreaseConsoleFont,
                HotkeyAction.ResetConsoleFont)));
            return tabs;
        }

        private TableLayoutPanel CreateHotkeyTable(HotkeySettings source, params HotkeyAction[] actions)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5),
                AutoScroll = false,
                ColumnCount = 4,
                RowCount = 0,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InlineActionButtonWidth));

            foreach (var action in actions)
            {
                var binding = source[action] ?? HotkeySettings.CreateDefaultBinding(action);
                var name = _text["Hotkey." + action];
                var enabled = new FluentCheckBox
                {
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(4, 5, 4, 3),
                    Checked = binding.Enabled,
                    AccessibleName = name
                };
                var label = new Label
                {
                    Text = name,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Margin = new Padding(2, 0, 8, 0)
                };
                var gesture = new FluentHotkeyBox
                {
                    RequireModifier = HotkeySettings.GetScope(action) == HotkeyScope.Global,
                    Gesture = binding.Gesture,
                    Dock = DockStyle.Fill,
                    MinimumSize = new Size(120, 29),
                    Margin = new Padding(0, 0, 5, 0),
                    AccessibleName = name,
                    AccessibleDescription = _text["Settings.HotkeyHint"]
                };
                var reset = new FluentButton
                {
                    Text = _text["Settings.HotkeyReset"],
                    AutoSize = false,
                    Size = new Size(InlineActionButtonWidth, 29),
                    Anchor = AnchorStyles.Left,
                    Margin = Padding.Empty,
                    AccessibleName = _text.Get("Settings.HotkeyResetAccessible", name)
                };
                var row = new HotkeyEditorRow { Enabled = enabled, Gesture = gesture, Reset = reset };
                _hotkeyEditors[action] = row;
                enabled.CheckedChanged += (sender, args) => gesture.Enabled = enabled.Checked;
                reset.Click += (sender, args) => gesture.Gesture = HotkeySettings.DefaultGesture(action);
                gesture.Enabled = enabled.Checked;

                var rowIndex = table.RowCount++;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
                table.Controls.Add(enabled, 0, rowIndex);
                table.Controls.Add(label, 1, rowIndex);
                table.Controls.Add(gesture, 2, rowIndex);
                table.Controls.Add(reset, 3, rowIndex);
            }

            var fillerRow = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filler = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            table.Controls.Add(filler, 0, fillerRow);
            table.SetColumnSpan(filler, 4);
            return table;
        }

        private HotkeySettings ReadHotkeySettings()
        {
            var result = new HotkeySettings();
            var used = new Dictionary<HotkeyScope, Dictionary<string, HotkeyAction>>();
            foreach (HotkeyScope scope in Enum.GetValues(typeof(HotkeyScope)))
                used[scope] = new Dictionary<string, HotkeyAction>(StringComparer.OrdinalIgnoreCase);

            foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            {
                var row = _hotkeyEditors[action];
                var scope = HotkeySettings.GetScope(action);
                ShowAppHotkeyGesture parsed;
                if (!ShowAppHotkeyGesture.TryParse(row.Gesture.Gesture, scope == HotkeyScope.Global, out parsed))
                    throw new InvalidOperationException(_text.Get("Settings.HotkeyRequired", _text["Hotkey." + action]));

                var binding = result[action];
                binding.Enabled = row.Enabled.Checked;
                binding.Gesture = parsed.ToString();
                if (!binding.Enabled) continue;

                HotkeyAction duplicate;
                if (used[scope].TryGetValue(binding.Gesture, out duplicate))
                {
                    throw new InvalidOperationException(_text.Get("Settings.HotkeyConflict",
                        _text["Hotkey." + duplicate], _text["Hotkey." + action], binding.Gesture));
                }
                used[scope][binding.Gesture] = action;
            }
            return result;
        }

        private Control WithFileButton(FluentTextBox textBox, string filter)
        {
            textBox.Dock = DockStyle.Fill;
            textBox.Margin = Padding.Empty;
            var panel = TwoColumnPanel();
            var browse = new FluentButton { Text = _text["Common.Browse"], AutoSize = true, Margin = new Padding(5, 0, 0, 0) };
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

        private static TableLayoutPanel CreateTable(int labelWidth = 135)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = false, ColumnCount = 2, RowCount = 0 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
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

        private static Button ColorButton()
        {
            var button = new FluentButton
            {
                AutoSize = false,
                Size = new Size(96, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false,
                Margin = Padding.Empty,
                UseAssignedColors = true
            };
            button.Tag = AppThemeManager.PreserveColorsTag;
            button.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150);
            button.Click += (sender, args) =>
            {
                var source = (Button)sender;
                using (var dialog = new ColorDialog { Color = source.BackColor, FullOpen = true })
                {
                    if (dialog.ShowDialog(source.FindForm()) == DialogResult.OK) SetColor(source, dialog.Color);
                }
            };
            return button;
        }

        private static FluentNumericUpDown OpacityField()
        {
            return new FluentNumericUpDown
            {
                Minimum = 0,
                Maximum = 100,
                Width = 58,
                TextAlign = HorizontalAlignment.Right
            };
        }

        private static Control ColorOnly(Button button)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            panel.Controls.Add(button);
            return panel;
        }

        private static Control ColorWithOpacity(Button button, FluentNumericUpDown opacity)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            opacity.Margin = new Padding(8, 1, 0, 0);
            panel.Controls.Add(button);
            panel.Controls.Add(opacity);
            panel.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(3, 5, 0, 0) });
            return panel;
        }

        private static void SetColor(Button button, Color color)
        {
            button.BackColor = Color.FromArgb(color.R, color.G, color.B);
            button.ForeColor = color.GetBrightness() < 0.48f ? Color.White : Color.Black;
            button.Text = ConsoleAppearance.ToHex(color);
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
            if (control is FluentNumericUpDown)
            {
                control.Dock = DockStyle.None;
                control.Anchor = AnchorStyles.Left;
            }
            else control.Dock = control is CheckBox || control is Label ? DockStyle.Top : DockStyle.Fill;
            control.Margin = new Padding(2, 2, 2, 3);
            table.Controls.Add(control, 1, row);
        }

        private static void AddFullRow(TableLayoutPanel table, Control control)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(2, 3, 2, 3);
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 2);
        }

        private static void AddFiller(TableLayoutPanel table)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var filler = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            table.Controls.Add(filler, 0, row);
            table.SetColumnSpan(filler, 2);
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
