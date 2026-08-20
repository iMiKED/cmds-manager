using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    internal sealed class QuickLaunchForm : Form
    {
        private const int WindowWidth = 720;
        private const int SearchHeight = 66;
        private const int ResultHeight = 62;
        private const int EmptyHeight = 112;
        private const int MaxVisibleResults = 6;
        private const int CornerRadius = 12;
        private const int DropShadowClassStyle = 0x00020000;

        private sealed class ScriptItem
        {
            internal Guid Id { get; set; }
            internal string Name { get; set; }
            internal string Path { get; set; }
            internal string Interpreter { get; set; }
            internal string TypeCode { get; set; }
            internal ScriptRuntimeSnapshot Runtime { get; set; }
            internal string StateText { get; set; }

            internal bool ShowsRuntimeState
            {
                get
                {
                    return Runtime != null && (Runtime.State == ScriptRuntimeState.Starting ||
                        Runtime.State == ScriptRuntimeState.Running ||
                        Runtime.State == ScriptRuntimeState.Stopping ||
                        Runtime.State == ScriptRuntimeState.Failed);
                }
            }

            public override string ToString()
            {
                var state = ShowsRuntimeState ? ", " + StateText : string.Empty;
                return Name + ", " + Interpreter + ", " + Path + state;
            }
        }

        private sealed class SearchSurface : Panel
        {
            private readonly AppThemePalette _palette;
            private readonly Label _cue;
            private string _placeholder;

            internal SearchSurface(AppThemePalette palette, Font queryFont, string placeholder)
            {
                _palette = palette;
                _placeholder = placeholder ?? string.Empty;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                BackColor = palette.Surface;
                Height = SearchHeight;
                Dock = DockStyle.Top;
                Editor = new TextBox
                {
                    BorderStyle = BorderStyle.None,
                    BackColor = palette.Surface,
                    ForeColor = palette.Text,
                    Font = queryFont,
                    ShortcutsEnabled = true,
                    AccessibleName = placeholder,
                    AccessibleDescription = placeholder
                };
                _cue = new Label
                {
                    AutoSize = false,
                    BackColor = palette.Surface,
                    ForeColor = palette.MutedText,
                    Font = queryFont,
                    Text = _placeholder,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.IBeam,
                    TabStop = false
                };
                _cue.MouseDown += (sender, args) =>
                {
                    _cue.Visible = false;
                    Editor.Focus();
                };
                Editor.KeyDown += (sender, args) => _cue.Visible = false;
                Editor.TextChanged += (sender, args) => _cue.Visible = Editor.TextLength == 0;
                Editor.Leave += (sender, args) => _cue.Visible = Editor.TextLength == 0;
                Controls.Add(Editor);
                Controls.Add(_cue);
                _cue.BringToFront();
                LayoutEditor();
            }

            internal TextBox Editor { get; }

            internal string Placeholder
            {
                get { return _placeholder; }
                set
                {
                    _placeholder = value ?? string.Empty;
                    if (_cue != null) _cue.Text = _placeholder;
                }
            }

            protected override void OnResize(EventArgs args)
            {
                base.OnResize(args);
                LayoutEditor();
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                base.OnPaint(args);
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(_palette.MutedText, 1.6f))
                {
                    var glass = new RectangleF(21.5f, 23.5f, 13f, 13f);
                    args.Graphics.DrawEllipse(pen, glass);
                    args.Graphics.DrawLine(pen, 33f, 35f, 39f, 41f);
                }
                using (var separator = new Pen(_palette.GridLine))
                    args.Graphics.DrawLine(separator, 0, Height - 1, Width, Height - 1);
            }

            private void LayoutEditor()
            {
                if (Editor == null) return;
                var height = Editor.PreferredHeight;
                Editor.SetBounds(52, Math.Max(0, (Height - height) / 2), Math.Max(1, Width - 70), height);
                if (_cue != null) _cue.Bounds = Editor.Bounds;
            }
        }

        private sealed class ResultsListBox : ListBox
        {
            private const int WsVScroll = 0x00200000;

            internal ResultsListBox()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                DrawMode = DrawMode.OwnerDrawFixed;
                BorderStyle = BorderStyle.None;
                IntegralHeight = false;
                ItemHeight = ResultHeight;
                Dock = DockStyle.Fill;
            }

            internal event EventHandler ViewportChanged;

            protected override CreateParams CreateParams
            {
                get
                {
                    var parameters = base.CreateParams;
                    parameters.Style &= ~WsVScroll;
                    return parameters;
                }
            }

            protected override void OnMouseWheel(MouseEventArgs args)
            {
                base.OnMouseWheel(args);
                ViewportChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private sealed class ScrollIndicator : Control
        {
            private readonly AppThemePalette _palette;
            private int _total;
            private int _visibleCount;
            private int _topIndex;

            internal ScrollIndicator(AppThemePalette palette)
            {
                _palette = palette;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                Dock = DockStyle.Right;
                Width = 7;
                BackColor = palette.Surface;
                Enabled = false;
            }

            internal void Configure(int total, int visibleCount, int topIndex)
            {
                _total = Math.Max(0, total);
                _visibleCount = Math.Max(1, visibleCount);
                _topIndex = Math.Max(0, topIndex);
                Visible = _total > _visibleCount;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                base.OnPaint(args);
                if (_total <= _visibleCount) return;
                var trackHeight = Math.Max(1, Height - 16);
                var thumbHeight = Math.Max(28, trackHeight * _visibleCount / _total);
                thumbHeight = Math.Min(trackHeight, thumbHeight);
                var maximumTop = Math.Max(1, _total - _visibleCount);
                var thumbTop = 8 + (trackHeight - thumbHeight) * Math.Min(_topIndex, maximumTop) / maximumTop;
                using (var path = FluentGeometry.RoundedRectangle(
                    new RectangleF(2f, thumbTop, 3f, thumbHeight), 1.5f))
                using (var brush = new SolidBrush(_palette.DisabledText))
                    args.Graphics.FillPath(brush, path);
            }
        }

        private readonly ScriptItem[] _scripts;
        private readonly LocalizationService _text;
        private readonly AppThemePalette _palette;
        private readonly SearchSurface _search;
        private readonly ResultsListBox _list = new ResultsListBox();
        private readonly ScrollIndicator _scrollIndicator;
        private readonly Panel _empty = new Panel { Dock = DockStyle.Fill };
        private readonly Label _emptyTitle = new Label { AutoSize = false, TextAlign = ContentAlignment.BottomCenter };
        private readonly Label _emptyHint = new Label { AutoSize = false, TextAlign = ContentAlignment.TopCenter };
        private readonly ToolTip _toolTip = new ToolTip { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 12000 };
        private readonly Font _queryFont = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _titleFont = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _detailFont = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _badgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _keyFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
        private bool _dismissOnDeactivate;
        private int _toolTipIndex = -1;

        internal QuickLaunchForm(IEnumerable<ScriptDefinition> scripts, LocalizationService text,
            ApplicationTheme theme)
            : this(scripts, text, theme, null)
        {
        }

        internal QuickLaunchForm(IEnumerable<ScriptDefinition> scripts, LocalizationService text,
            ApplicationTheme theme, Func<Guid, ScriptRuntimeSnapshot> runtimeForScript)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _palette = AppThemeManager.Resolve(theme);
            _scripts = (scripts ?? Enumerable.Empty<ScriptDefinition>())
                .Where(item => item.Enabled)
                .Select(item => CreateItem(item, runtimeForScript))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            Text = text["QuickLaunch.Title"];
            StartPosition = FormStartPosition.Manual;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(WindowWidth, SearchHeight + EmptyHeight);
            MinimumSize = new Size(620, SearchHeight + ResultHeight + 2);
            Icon = ApplicationResources.Icon;
            KeyPreview = true;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = _palette.Surface;
            ForeColor = _palette.Text;
            Padding = new Padding(1);

            _search = new SearchSurface(_palette, _queryFont, text["QuickLaunch.SearchPlaceholder"]);
            _search.Editor.TextChanged += (sender, args) => RefreshList();
            _list.BackColor = _palette.Surface;
            _list.ForeColor = _palette.Text;
            _list.AccessibleName = text["QuickLaunch.List"];
            _list.AccessibleDescription = text["QuickLaunch.Run"];
            _list.DrawItem += DrawResult;
            _list.DoubleClick += (sender, args) => AcceptSelection();
            _list.MouseMove += HandleResultMouseMove;
            _list.ViewportChanged += (sender, args) => UpdateScrollIndicator();
            _list.MouseLeave += (sender, args) =>
            {
                _toolTipIndex = -1;
                _toolTip.SetToolTip(_list, string.Empty);
            };

            _empty.BackColor = _palette.Surface;
            _emptyTitle.Text = text["QuickLaunch.NoResults"];
            _emptyTitle.Font = _titleFont;
            _emptyTitle.ForeColor = _palette.Text;
            _emptyTitle.BackColor = _palette.Surface;
            _emptyHint.Text = text["QuickLaunch.NoResultsHint"];
            _emptyHint.Font = _detailFont;
            _emptyHint.ForeColor = _palette.MutedText;
            _emptyHint.BackColor = _palette.Surface;
            _empty.Controls.Add(_emptyTitle);
            _empty.Controls.Add(_emptyHint);
            _empty.Resize += (sender, args) => LayoutEmptyState();

            var content = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Surface };
            _scrollIndicator = new ScrollIndicator(_palette);
            content.Controls.Add(_list);
            content.Controls.Add(_scrollIndicator);
            content.Controls.Add(_empty);
            Controls.Add(content);
            Controls.Add(_search);

            Paint += DrawWindowBorder;
            Shown += HandleShown;
            Deactivate += (sender, args) =>
            {
                if (!_dismissOnDeactivate || IsDisposed) return;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            RefreshList();
        }

        internal Guid SelectedScriptId { get; private set; }
        internal TextBox SearchEditor => _search.Editor;
        internal ListBox ResultsList => _list;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ClassStyle |= DropShadowClassStyle;
                return parameters;
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var modifiers = keyData & Keys.Modifiers;
            if (modifiers == Keys.Shift && key == Keys.Tab)
            {
                MoveSelection(-1);
                return true;
            }
            if (modifiers == Keys.None)
            {
                if (key == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return true;
                }
                if (key == Keys.Enter)
                {
                    AcceptSelection();
                    return true;
                }
                if (key == Keys.Down || key == Keys.Tab)
                {
                    MoveSelection(1);
                    return true;
                }
                if (key == Keys.Up)
                {
                    MoveSelection(-1);
                    return true;
                }
                if (key == Keys.PageDown)
                {
                    MoveSelection(MaxVisibleResults);
                    return true;
                }
                if (key == Keys.PageUp)
                {
                    MoveSelection(-MaxVisibleResults);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnSizeChanged(EventArgs args)
        {
            base.OnSizeChanged(args);
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTip.Dispose();
            base.Dispose(disposing);
            if (!disposing) return;
            _queryFont.Dispose();
            _titleFont.Dispose();
            _detailFont.Dispose();
            _badgeFont.Dispose();
            _keyFont.Dispose();
        }

        private ScriptItem CreateItem(ScriptDefinition script, Func<Guid, ScriptRuntimeSnapshot> runtimeForScript)
        {
            ScriptRuntimeSnapshot runtime = null;
            try { runtime = runtimeForScript?.Invoke(script.Id); }
            catch { runtime = null; }
            runtime = runtime ?? new ScriptRuntimeSnapshot
            {
                ScriptId = script.Id,
                State = ScriptRuntimeState.Stopped
            };
            var interpreter = ResolveInterpreter(script);
            return new ScriptItem
            {
                Id = script.Id,
                Name = script.Name ?? string.Empty,
                Path = script.Path ?? string.Empty,
                Interpreter = InterpreterText(interpreter),
                TypeCode = TypeCode(interpreter),
                Runtime = runtime,
                StateText = StateText(runtime)
            };
        }

        private void HandleShown(object sender, EventArgs args)
        {
            PositionWindow();
            _search.Editor.Focus();
            _search.Editor.SelectAll();
            Activate();
            BeginInvoke((Action)(() => _dismissOnDeactivate = true));
        }

        private void RefreshList()
        {
            var query = (_search.Editor.Text ?? string.Empty).Trim();
            var results = _scripts
                .Select(item => new { Item = item, Score = MatchScore(item, query) })
                .Where(result => result.Score >= 0)
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(result => result.Item)
                .ToArray();

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                _list.Items.AddRange(results.Cast<object>().ToArray());
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            }
            finally
            {
                _list.EndUpdate();
            }

            _list.Visible = results.Length > 0;
            _empty.Visible = results.Length == 0;
            var contentHeight = results.Length == 0
                ? EmptyHeight
                : Math.Min(MaxVisibleResults, results.Length) * ResultHeight;
            ClientSize = new Size(WindowWidth, SearchHeight + contentHeight + Padding.Vertical);
            LayoutEmptyState();
            UpdateScrollIndicator();
            if (Visible) PositionWindow();
        }

        private void MoveSelection(int delta)
        {
            if (_list.Items.Count == 0) return;
            var current = Math.Max(0, _list.SelectedIndex);
            _list.SelectedIndex = Math.Max(0, Math.Min(_list.Items.Count - 1, current + delta));
            _list.TopIndex = Math.Max(0, Math.Min(_list.SelectedIndex,
                Math.Max(0, _list.Items.Count - MaxVisibleResults)));
            UpdateScrollIndicator();
        }

        private void UpdateScrollIndicator()
        {
            if (_scrollIndicator == null) return;
            var visibleCount = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight));
            _scrollIndicator.Configure(_list.Items.Count, visibleCount, _list.TopIndex);
        }

        private void AcceptSelection()
        {
            var selected = _list.SelectedItem as ScriptItem;
            if (selected == null) return;
            SelectedScriptId = selected.Id;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void PositionWindow()
        {
            var screen = Owner != null && Owner.Visible
                ? Screen.FromControl(Owner)
                : Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;
            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = area.Top + Math.Max(42, Math.Min(150, area.Height / 7));
        }

        private void LayoutEmptyState()
        {
            if (_empty == null || _emptyTitle == null || _emptyHint == null) return;
            var center = Math.Max(0, _empty.ClientSize.Height / 2);
            _emptyTitle.SetBounds(24, Math.Max(8, center - 30), Math.Max(1, _empty.ClientSize.Width - 48), 26);
            _emptyHint.SetBounds(24, Math.Max(34, center - 2), Math.Max(1, _empty.ClientSize.Width - 48), 28);
        }

        private void HandleResultMouseMove(object sender, MouseEventArgs args)
        {
            var index = _list.IndexFromPoint(args.Location);
            if (index == _toolTipIndex) return;
            _toolTipIndex = index;
            if (index < 0 || index >= _list.Items.Count)
            {
                _toolTip.SetToolTip(_list, string.Empty);
                return;
            }
            _list.SelectedIndex = index;
            var item = (ScriptItem)_list.Items[index];
            var status = item.ShowsRuntimeState ? Environment.NewLine + item.StateText : string.Empty;
            _toolTip.SetToolTip(_list, item.Name + Environment.NewLine + item.Path +
                Environment.NewLine + item.Interpreter + status);
        }

        private void DrawResult(object sender, DrawItemEventArgs args)
        {
            if (args.Index < 0 || args.Index >= _list.Items.Count) return;
            var item = (ScriptItem)_list.Items[args.Index];
            var selected = (args.State & DrawItemState.Selected) != 0;
            var graphics = args.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var surface = new SolidBrush(_palette.Surface))
                graphics.FillRectangle(surface, args.Bounds);

            var row = new Rectangle(args.Bounds.X + 8, args.Bounds.Y + 3,
                Math.Max(1, args.Bounds.Width - 16), Math.Max(1, args.Bounds.Height - 6));
            if (selected)
            {
                using (var path = FluentGeometry.RoundedRectangle(row, 8f))
                using (var brush = new SolidBrush(_palette.Selection))
                    graphics.FillPath(brush, path);
                using (var accent = new SolidBrush(_palette.Accent))
                    graphics.FillRectangle(accent, row.Left, row.Top + 10, 3, row.Height - 20);
            }

            var badge = new Rectangle(row.Left + 13, row.Top + 11, 34, 34);
            var badgeColor = TypeColor(item.TypeCode);
            using (var badgePath = FluentGeometry.RoundedRectangle(badge, 8f))
            using (var badgeBrush = new SolidBrush(badgeColor))
                graphics.FillPath(badgeBrush, badgePath);
            TextRenderer.DrawText(graphics, item.TypeCode, _badgeFont, badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var right = row.Right - 13;
            if (selected)
            {
                const int keyWidth = 48;
                var keyBounds = new Rectangle(right - keyWidth, row.Top + 17, keyWidth, 24);
                using (var keyPath = FluentGeometry.RoundedRectangle(keyBounds, 5f))
                using (var keyBrush = new SolidBrush(_palette.SurfaceAlternate))
                using (var keyBorder = new Pen(_palette.Border))
                {
                    graphics.FillPath(keyBrush, keyPath);
                    graphics.DrawPath(keyBorder, keyPath);
                }
                TextRenderer.DrawText(graphics, "Enter", _keyFont, keyBounds, _palette.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                right = keyBounds.Left - 12;
            }

            if (item.ShowsRuntimeState)
            {
                var stateSize = TextRenderer.MeasureText(item.StateText, _detailFont,
                    new Size(150, 22), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                var stateWidth = Math.Min(150, stateSize.Width + 17);
                var stateBounds = new Rectangle(right - stateWidth, row.Top + 18, stateWidth, 22);
                using (var dot = new SolidBrush(StateColor(item.Runtime.State)))
                    graphics.FillEllipse(dot, stateBounds.Left, stateBounds.Top + 7, 8, 8);
                TextRenderer.DrawText(graphics, item.StateText, _detailFont,
                    new Rectangle(stateBounds.Left + 13, stateBounds.Top, stateBounds.Width - 13, stateBounds.Height),
                    _palette.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                right = stateBounds.Left - 10;
            }

            var textLeft = badge.Right + 14;
            var textWidth = Math.Max(20, right - textLeft);
            TextRenderer.DrawText(graphics, item.Name, _titleFont,
                new Rectangle(textLeft, row.Top + 8, textWidth, 21), _palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(graphics, item.Interpreter + "  •  " + item.Path, _detailFont,
                new Rectangle(textLeft, row.Top + 31, textWidth, 18), _palette.MutedText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        private void DrawWindowBorder(object sender, PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FluentGeometry.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, ClientSize.Width - 1f),
                    Math.Max(1f, ClientSize.Height - 1f)), CornerRadius))
            using (var pen = new Pen(_palette.Border))
                args.Graphics.DrawPath(pen, path);
        }

        private static int MatchScore(ScriptItem item, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return 0;
            var tokens = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var total = 0;
            foreach (var token in tokens)
            {
                var name = ScoreText(item.Name, token, 5000);
                var path = ScoreText(item.Path, token, 2800);
                var interpreter = ScoreText(item.Interpreter, token, 1800);
                var state = item.ShowsRuntimeState ? ScoreText(item.StateText, token, 1400) : -1;
                var score = Math.Max(Math.Max(name, path), Math.Max(interpreter, state));
                if (score < 0) return -1;
                total += score;
            }
            return total;
        }

        private static int ScoreText(string value, string token, int weight)
        {
            value = value ?? string.Empty;
            if (value.Equals(token, StringComparison.CurrentCultureIgnoreCase)) return weight + 1000;
            if (value.StartsWith(token, StringComparison.CurrentCultureIgnoreCase)) return weight + 700;
            var index = value.IndexOf(token, StringComparison.CurrentCultureIgnoreCase);
            if (index >= 0) return weight + 400 - Math.Min(300, index);

            var sourceIndex = 0;
            var gaps = 0;
            foreach (var character in token)
            {
                var found = value.IndexOf(character.ToString(), sourceIndex,
                    StringComparison.CurrentCultureIgnoreCase);
                if (found < 0) return -1;
                gaps += found - sourceIndex;
                sourceIndex = found + 1;
            }
            return Math.Max(1, weight / 3 - gaps);
        }

        private ScriptRuntimeState ResolveState(ScriptRuntimeSnapshot runtime)
        {
            return runtime?.State ?? ScriptRuntimeState.Stopped;
        }

        private string StateText(ScriptRuntimeSnapshot runtime)
        {
            switch (ResolveState(runtime))
            {
                case ScriptRuntimeState.Starting: return _text["Main.State.Starting"];
                case ScriptRuntimeState.Running:
                    return runtime.ActiveCount > 1
                        ? _text.Get("Main.State.RunningMany", runtime.ActiveCount)
                        : _text["Main.State.Running"];
                case ScriptRuntimeState.Stopping: return _text["Main.State.Stopping"];
                case ScriptRuntimeState.Exited: return _text["Main.State.Exited"];
                case ScriptRuntimeState.Failed: return _text["Main.State.Failed"];
                default: return _text["Main.State.Stopped"];
            }
        }

        private static ScriptInterpreter ResolveInterpreter(ScriptDefinition script)
        {
            if (script?.Launch == null) return ScriptInterpreter.Auto;
            if (script.Launch.Interpreter != ScriptInterpreter.Auto) return script.Launch.Interpreter;
            try { return ScriptDefinitionValidator.ResolveAutoInterpreter(script.Path); }
            catch { return ScriptInterpreter.Auto; }
        }

        private static string InterpreterText(ScriptInterpreter interpreter)
        {
            switch (interpreter)
            {
                case ScriptInterpreter.Cmd: return "CMD";
                case ScriptInterpreter.WindowsPowerShell: return "Windows PowerShell 5.1";
                case ScriptInterpreter.PowerShell7: return "PowerShell 7";
                case ScriptInterpreter.CScript: return "cscript.exe";
                case ScriptInterpreter.WScript: return "wscript.exe";
                default: return "Script";
            }
        }

        private static string TypeCode(ScriptInterpreter interpreter)
        {
            switch (interpreter)
            {
                case ScriptInterpreter.WindowsPowerShell:
                case ScriptInterpreter.PowerShell7:
                    return "PS";
                case ScriptInterpreter.CScript:
                case ScriptInterpreter.WScript:
                    return "VB";
                case ScriptInterpreter.Cmd:
                    return "CMD";
                default:
                    return "RUN";
            }
        }

        private Color TypeColor(string typeCode)
        {
            if (typeCode == "PS") return _palette.IsDark ? Color.FromArgb(26, 111, 156) : Color.FromArgb(0, 114, 198);
            if (typeCode == "VB") return _palette.IsDark ? Color.FromArgb(116, 87, 24) : Color.FromArgb(160, 112, 12);
            if (typeCode == "CMD") return _palette.IsDark ? Color.FromArgb(60, 74, 91) : Color.FromArgb(71, 85, 105);
            return _palette.Accent;
        }

        private Color StateColor(ScriptRuntimeState state)
        {
            switch (state)
            {
                case ScriptRuntimeState.Running: return Color.FromArgb(34, 197, 94);
                case ScriptRuntimeState.Starting: return Color.FromArgb(234, 179, 8);
                case ScriptRuntimeState.Stopping: return Color.FromArgb(249, 115, 22);
                case ScriptRuntimeState.Failed: return _palette.Danger;
                default: return _palette.MutedText;
            }
        }
    }
}
