using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    internal sealed class QuickLaunchForm : Form
    {
        private sealed class ScriptItem
        {
            internal Guid Id { get; set; }
            internal string Name { get; set; }
            internal string Path { get; set; }

            public override string ToString()
            {
                return Name;
            }
        }

        private readonly ScriptItem[] _scripts;
        private readonly FluentTextBox _filter = new FluentTextBox();
        private readonly ListBox _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false
        };
        private readonly FluentButton _run;

        internal QuickLaunchForm(IEnumerable<ScriptDefinition> scripts, LocalizationService text,
            ApplicationTheme theme)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            _scripts = (scripts ?? Enumerable.Empty<ScriptDefinition>())
                .Where(item => item.Enabled)
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new ScriptItem { Id = item.Id, Name = item.Name, Path = item.Path })
                .ToArray();

            Text = text["QuickLaunch.Title"];
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(430, 330);
            Icon = ApplicationResources.Icon;
            KeyPreview = true;

            var prompt = new Label
            {
                Text = text["QuickLaunch.Prompt"],
                AutoSize = true,
                Margin = new Padding(2, 0, 2, 5)
            };
            _filter.Dock = DockStyle.Fill;
            _filter.Margin = new Padding(0, 0, 0, 8);
            _filter.AccessibleName = text["QuickLaunch.Prompt"];
            _list.AccessibleName = text["QuickLaunch.List"];
            _run = FluentDialogButtons.Primary(text["QuickLaunch.Run"]);
            var cancel = FluentDialogButtons.Secondary(text["Common.Cancel"], DialogResult.Cancel);
            _run.Click += AcceptSelection;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(prompt, 0, 0);
            layout.Controls.Add(_filter, 0, 1);
            layout.Controls.Add(_list, 0, 2);
            layout.Controls.Add(FluentDialogButtons.Footer(_run, cancel), 0, 3);
            Controls.Add(layout);

            _filter.TextChanged += (sender, args) => RefreshList();
            _list.SelectedIndexChanged += (sender, args) => _run.Enabled = _list.SelectedItem != null;
            _list.DoubleClick += (sender, args) => AcceptSelection(sender, args);
            Shown += (sender, args) =>
            {
                _filter.Focus();
                Activate();
            };
            RefreshList();
            AcceptButton = _run;
            CancelButton = cancel;
            AppThemeManager.ApplyWindow(this, theme);
        }

        internal Guid SelectedScriptId { get; private set; }

        private void RefreshList()
        {
            var filter = (_filter.Text ?? string.Empty).Trim();
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var script in _scripts)
                {
                    if (filter.Length > 0 &&
                        script.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        script.Path.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;
                    _list.Items.Add(script);
                }
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            }
            finally
            {
                _list.EndUpdate();
            }
            _run.Enabled = _list.SelectedItem != null;
        }

        private void AcceptSelection(object sender, EventArgs args)
        {
            var selected = _list.SelectedItem as ScriptItem;
            if (selected == null) return;
            SelectedScriptId = selected.Id;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
