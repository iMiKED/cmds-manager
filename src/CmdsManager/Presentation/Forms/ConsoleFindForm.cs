using System;
using System.Drawing;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    internal sealed class ConsoleFindForm : Form
    {
        private readonly LocalizationService _text;
        private readonly RichTextBox _target;
        private readonly Label _queryLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly FluentTextBox _query = new FluentTextBox { Dock = DockStyle.Fill };
        private readonly FluentCheckBox _matchCase = new FluentCheckBox();
        private readonly Label _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly FluentButton _previous = new FluentButton { AutoSize = true };
        private readonly FluentButton _next = new FluentButton { AutoSize = true, Primary = true };
        private readonly FluentButton _close = new FluentButton { AutoSize = true };

        internal ConsoleFindForm(LocalizationService text, RichTextBox target, ApplicationTheme theme)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            Text = _text["Console.FindTitle"];
            Icon = ApplicationResources.Icon;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 102);
            KeyPreview = true;

            var queryRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(8, 7, 8, 2)
            };
            queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            queryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _queryLabel.Margin = new Padding(0, 6, 8, 0);
            _query.Margin = Padding.Empty;
            queryRow.Controls.Add(_queryLabel, 0, 0);
            queryRow.Controls.Add(_query, 1, 0);

            var actionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(8, 1, 6, 5)
            };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _matchCase.Margin = new Padding(0, 5, 10, 0);
            _status.Margin = new Padding(0, 7, 6, 0);
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            buttons.Controls.AddRange(new Control[] { _previous, _next, _close });
            actionRow.Controls.Add(_matchCase, 0, 0);
            actionRow.Controls.Add(_status, 1, 0);
            actionRow.Controls.Add(buttons, 2, 0);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.Controls.Add(queryRow, 0, 0);
            layout.Controls.Add(actionRow, 0, 1);
            Controls.Add(layout);

            _query.TextChanged += (sender, args) => _status.Text = string.Empty;
            _previous.Click += (sender, args) => Find(false);
            _next.Click += (sender, args) => Find(true);
            _close.Click += (sender, args) => Close();
            _target.Disposed += HandleTargetDisposed;
            AcceptButton = _next;
            ApplyLocalization();
            ApplyApplicationTheme(theme);
        }

        internal string SearchText
        {
            get => _query.Text;
            set => _query.Text = value ?? string.Empty;
        }

        internal bool FindNext()
        {
            return Find(true);
        }

        internal bool FindPrevious()
        {
            return Find(false);
        }

        internal void ApplyApplicationTheme(ApplicationTheme theme)
        {
            AppThemeManager.ApplyWindow(this, theme);
        }

        internal void ApplyLocalization()
        {
            Text = _text["Console.FindTitle"];
            _queryLabel.Text = _text["Console.FindLabel"];
            _matchCase.Text = _text["Console.FindMatchCase"];
            _previous.Text = _text["Console.FindPrevious"];
            _next.Text = _text["Console.FindNext"];
            _close.Text = _text["Common.Close"];
        }

        protected override void OnShown(EventArgs args)
        {
            base.OnShown(args);
            _query.Focus();
            _query.SelectAll();
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Escape)
            {
                Close();
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            if (args.KeyCode == Keys.Enter && args.Shift)
            {
                Find(false);
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(args);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _target.Disposed -= HandleTargetDisposed;
            base.Dispose(disposing);
        }

        private bool Find(bool forward)
        {
            var query = _query.Text;
            var source = _target.Text;
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(source))
            {
                _status.Text = string.Empty;
                _query.Focus();
                return false;
            }

            var comparison = _matchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int index;
            if (forward)
            {
                var start = Math.Min(source.Length, _target.SelectionStart + _target.SelectionLength);
                index = source.IndexOf(query, start, comparison);
                if (index < 0 && start > 0) index = source.IndexOf(query, 0, comparison);
            }
            else
            {
                var start = Math.Min(source.Length - 1, _target.SelectionStart - 1);
                index = start >= 0 ? source.LastIndexOf(query, start, comparison) : -1;
                if (index < 0 && source.Length > 0) index = source.LastIndexOf(query, source.Length - 1, comparison);
            }

            if (index < 0)
            {
                _status.Text = _text["Console.FindNoMatches"];
                return false;
            }

            _target.Select(index, query.Length);
            _target.ScrollToCaret();
            _status.Text = string.Empty;
            return true;
        }

        private void HandleTargetDisposed(object sender, EventArgs args)
        {
            if (!IsDisposed) Close();
        }
    }
}
