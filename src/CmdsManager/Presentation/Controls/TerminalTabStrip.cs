using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmdsManager.Presentation.Controls
{
    public sealed class TerminalTabEventArgs : EventArgs
    {
        public TerminalTabEventArgs(int key)
        {
            Key = key;
        }

        public int Key { get; }
    }

    public sealed class TerminalTabStrip : Control
    {
        private const int TopMargin = 4;
        private const int LeftMargin = 2;
        private const int TabWing = 8;
        private const int TabRadius = 8;
        private const int MinimumTabWidth = 150;
        private const int MaximumTabWidth = 240;
        private const int OverflowAreaWidth = 40;
        private const int CloseSize = 18;

        private sealed class TabItem
        {
            internal int Key { get; set; }
            internal string Text { get; set; }
            internal string ToolTipText { get; set; }
            internal bool IsRunning { get; set; }
            internal Rectangle LogicalBounds { get; set; }
            internal Rectangle Bounds { get; set; }
            internal Rectangle CloseBounds { get; set; }
        }

        private readonly List<TabItem> _items = new List<TabItem>();
        private readonly ToolTip _toolTip = new ToolTip
        {
            AutomaticDelay = 450,
            AutoPopDelay = 10000,
            ReshowDelay = 100
        };
        private readonly ContextMenuStrip _overflowMenu = new ContextMenuStrip();
        private readonly Font _ownedFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        private int _selectedIndex = -1;
        private int _hotIndex = -1;
        private int _hotCloseIndex = -1;
        private int _toolTipIndex = -1;
        private int _scrollOffset;
        private bool _showOverflow;
        private bool _hotOverflow;

        public TerminalTabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            TabStop = true;
            Height = 40;
            MinimumSize = new Size(0, 36);
            Font = _ownedFont;
            BackColor = Color.FromArgb(243, 244, 246);
            InactiveTabColor = Color.FromArgb(252, 252, 253);
            HoverTabColor = Color.FromArgb(100, 126, 160);
            ActiveTabColor = Color.FromArgb(28, 28, 28);
            ActiveTextColor = Color.FromArgb(245, 247, 250);
            InactiveTextColor = Color.FromArgb(38, 43, 50);
            RunningColor = Color.FromArgb(39, 190, 112);
            StoppedColor = Color.FromArgb(137, 146, 157);
        }

        public event EventHandler<TerminalTabEventArgs> SelectedTabChanged;
        public event EventHandler<TerminalTabEventArgs> CloseRequested;

        public Color ActiveTabColor { get; set; }
        public Color InactiveTabColor { get; set; }
        public Color HoverTabColor { get; set; }
        public Color ActiveTextColor { get; set; }
        public Color InactiveTextColor { get; set; }
        public Color RunningColor { get; set; }
        public Color StoppedColor { get; set; }

        public int TabCount => _items.Count;
        public int SelectedIndex => _selectedIndex;
        public int SelectedKey => _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex].Key
            : -1;

        public string GetTabText(int index)
        {
            return ItemAt(index).Text;
        }

        public int GetTabKey(int index)
        {
            return ItemAt(index).Key;
        }

        public bool IsTabRunning(int index)
        {
            return ItemAt(index).IsRunning;
        }

        public Rectangle GetTabBounds(int index)
        {
            return ItemAt(index).Bounds;
        }

        public Rectangle GetCloseBounds(int index)
        {
            return ItemAt(index).CloseBounds;
        }

        public void AddTab(int key, string text, string toolTipText, bool isRunning)
        {
            var existing = IndexOfKey(key);
            if (existing >= 0)
            {
                UpdateTab(key, text, toolTipText, isRunning);
                return;
            }

            _items.Add(new TabItem
            {
                Key = key,
                Text = text ?? string.Empty,
                ToolTipText = toolTipText ?? string.Empty,
                IsRunning = isRunning
            });
            var selectionChanged = _selectedIndex < 0;
            if (selectionChanged) _selectedIndex = 0;
            RecalculateLayout();
            EnsureSelectedVisible();
            Invalidate();
            if (selectionChanged) RaiseSelectedTabChanged();
        }

        public void UpdateTab(int key, string text, string toolTipText, bool isRunning)
        {
            var index = IndexOfKey(key);
            if (index < 0) return;
            var item = _items[index];
            item.Text = text ?? string.Empty;
            item.ToolTipText = toolTipText ?? string.Empty;
            item.IsRunning = isRunning;
            RecalculateLayout();
            EnsureSelectedVisible();
            Invalidate();
        }

        public bool RemoveTab(int key)
        {
            var index = IndexOfKey(key);
            if (index < 0) return false;
            var previousSelectedKey = SelectedKey;
            _items.RemoveAt(index);
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                _scrollOffset = 0;
            }
            else if (index < _selectedIndex)
            {
                _selectedIndex--;
            }
            else if (index == _selectedIndex)
            {
                _selectedIndex = Math.Min(index, _items.Count - 1);
            }

            _hotIndex = -1;
            _hotCloseIndex = -1;
            SetToolTipIndex(-1);
            RecalculateLayout();
            EnsureSelectedVisible();
            Invalidate();
            if (previousSelectedKey != SelectedKey) RaiseSelectedTabChanged();
            return true;
        }

        public bool SelectTab(int key)
        {
            return SelectIndex(IndexOfKey(key));
        }

        public bool SelectIndex(int index)
        {
            if (index < 0 || index >= _items.Count) return false;
            if (_selectedIndex == index)
            {
                EnsureSelectedVisible();
                return true;
            }

            _selectedIndex = index;
            EnsureSelectedVisible();
            Invalidate();
            RaiseSelectedTabChanged();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
                _overflowMenu.Dispose();
                _ownedFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnFontChanged(EventArgs args)
        {
            base.OnFontChanged(args);
            RecalculateLayout();
            Invalidate();
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            RecalculateLayout();
            EnsureSelectedVisible();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            args.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (_items.Count == 0) return;

            var previousSmoothing = args.Graphics.SmoothingMode;
            var previousPixelOffset = args.Graphics.PixelOffsetMode;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            args.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var viewportRight = _showOverflow ? OverflowBounds.Left - 2 : ClientSize.Width;
            var state = args.Graphics.Save();
            args.Graphics.SetClip(new Rectangle(0, 0, Math.Max(0, viewportRight), ClientSize.Height));
            for (var index = 0; index < _items.Count; index++)
            {
                if (index != _selectedIndex) DrawTab(args.Graphics, index);
            }
            if (_selectedIndex >= 0) DrawTab(args.Graphics, _selectedIndex);
            args.Graphics.Restore(state);

            if (_showOverflow) DrawOverflowButton(args.Graphics);
            args.Graphics.SmoothingMode = previousSmoothing;
            args.Graphics.PixelOffsetMode = previousPixelOffset;
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            Focus();
            if (_showOverflow && OverflowBounds.Contains(args.Location))
            {
                if (args.Button == MouseButtons.Left) ShowOverflowMenu();
                return;
            }

            var index = HitTest(args.Location);
            if (index < 0) return;
            var closeClicked = _items[index].CloseBounds.Contains(args.Location);
            SelectIndex(index);
            if (args.Button == MouseButtons.Left && closeClicked)
                CloseRequested?.Invoke(this, new TerminalTabEventArgs(_items[index].Key));
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            var hotOverflow = _showOverflow && OverflowBounds.Contains(args.Location);
            var index = hotOverflow ? -1 : HitTest(args.Location);
            var closeIndex = index >= 0 && _items[index].CloseBounds.Contains(args.Location) ? index : -1;
            SetToolTipIndex(index);
            if (_hotIndex == index && _hotCloseIndex == closeIndex && _hotOverflow == hotOverflow) return;
            _hotIndex = index;
            _hotCloseIndex = closeIndex;
            _hotOverflow = hotOverflow;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            _hotIndex = -1;
            _hotCloseIndex = -1;
            _hotOverflow = false;
            SetToolTipIndex(-1);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs args)
        {
            base.OnMouseWheel(args);
            if (!_showOverflow) return;
            _scrollOffset = Math.Max(0, Math.Min(MaximumScrollOffset,
                _scrollOffset - Math.Sign(args.Delta) * 90));
            RecalculateVisibleBounds();
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            base.OnKeyDown(args);
            if (_items.Count == 0) return;
            if (args.KeyCode == Keys.Left)
            {
                SelectIndex((_selectedIndex - 1 + _items.Count) % _items.Count);
                args.Handled = true;
            }
            else if (args.KeyCode == Keys.Right)
            {
                SelectIndex((_selectedIndex + 1) % _items.Count);
                args.Handled = true;
            }
        }

        private TabItem ItemAt(int index)
        {
            if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }

        private int IndexOfKey(int key)
        {
            return _items.FindIndex(item => item.Key == key);
        }

        private int HitTest(Point location)
        {
            for (var index = _items.Count - 1; index >= 0; index--)
            {
                if (_items[index].Bounds.Contains(location)) return index;
            }
            return -1;
        }

        private void DrawTab(Graphics graphics, int index)
        {
            var item = _items[index];
            if (item.Bounds.Right <= 0 || item.Bounds.Left >= ClientSize.Width) return;
            var selected = index == _selectedIndex;
            var hot = index == _hotIndex;
            var background = selected ? ActiveTabColor : hot ? HoverTabColor : InactiveTabColor;
            var foreground = selected || hot ? ActiveTextColor : InactiveTextColor;

            using (var path = CreateTabPath(item.Bounds))
            using (var brush = new SolidBrush(background))
                graphics.FillPath(brush, path);

            var statusBounds = new Rectangle(item.Bounds.Left + TabWing + 10,
                TopMargin + (ClientSize.Height - TopMargin - 8) / 2, 8, 8);
            using (var status = new SolidBrush(item.IsRunning ? RunningColor : StoppedColor))
                graphics.FillEllipse(status, statusBounds);

            if (index == _hotCloseIndex)
            {
                var closeBackground = selected || hot
                    ? Color.FromArgb(55, 255, 255, 255)
                    : Color.FromArgb(24, 0, 0, 0);
                using (var brush = new SolidBrush(closeBackground))
                    graphics.FillEllipse(brush, item.CloseBounds);
            }

            using (var closePen = new Pen(foreground, 1.35f))
            {
                var left = item.CloseBounds.Left + 5;
                var top = item.CloseBounds.Top + 5;
                var right = item.CloseBounds.Right - 5;
                var bottom = item.CloseBounds.Bottom - 5;
                graphics.DrawLine(closePen, left, top, right, bottom);
                graphics.DrawLine(closePen, right, top, left, bottom);
            }

            var textBounds = Rectangle.FromLTRB(statusBounds.Right + 8, TopMargin,
                item.CloseBounds.Left - 7, ClientSize.Height);
            TextRenderer.DrawText(graphics, item.Text, Font, textBounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private static GraphicsPath CreateTabPath(Rectangle bounds)
        {
            var left = bounds.Left;
            var right = bounds.Right;
            var top = bounds.Top;
            var bottom = bounds.Bottom;
            var bodyLeft = left + TabWing;
            var bodyRight = right - TabWing;
            var path = new GraphicsPath();
            path.StartFigure();
            path.AddLine(bodyLeft + TabRadius, top, bodyRight - TabRadius, top);
            path.AddBezier(bodyRight - TabRadius, top, bodyRight - 3, top,
                bodyRight, top + 3, bodyRight, top + TabRadius);
            path.AddLine(bodyRight, top + TabRadius, bodyRight, bottom - TabWing);
            path.AddBezier(bodyRight, bottom - TabWing, bodyRight, bottom - 3,
                right - 3, bottom, right, bottom);
            path.AddLine(right, bottom, left, bottom);
            path.AddBezier(left, bottom, left + 3, bottom,
                bodyLeft, bottom - 3, bodyLeft, bottom - TabWing);
            path.AddLine(bodyLeft, bottom - TabWing, bodyLeft, top + TabRadius);
            path.AddBezier(bodyLeft, top + TabRadius, bodyLeft, top + 3,
                bodyLeft + 3, top, bodyLeft + TabRadius, top);
            path.CloseFigure();
            return path;
        }

        private void DrawOverflowButton(Graphics graphics)
        {
            var bounds = OverflowBounds;
            var background = _hotOverflow ? Color.FromArgb(222, 226, 232) : Color.FromArgb(249, 250, 251);
            using (var path = RoundedRectangle(bounds, 6))
            using (var brush = new SolidBrush(background))
                graphics.FillPath(brush, path);

            var centerX = bounds.Left + bounds.Width / 2;
            var centerY = bounds.Top + bounds.Height / 2 + 1;
            using (var pen = new Pen(Color.FromArgb(70, 75, 82), 1.4f))
            {
                graphics.DrawLine(pen, centerX - 4, centerY - 2, centerX, centerY + 2);
                graphics.DrawLine(pen, centerX, centerY + 2, centerX + 4, centerY - 2);
            }
        }

        private void ShowOverflowMenu()
        {
            _overflowMenu.Items.Clear();
            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                var key = item.Key;
                var menuItem = new ToolStripMenuItem(item.Text)
                {
                    Checked = index == _selectedIndex,
                    ToolTipText = item.ToolTipText
                };
                menuItem.Click += (sender, args) => SelectTab(key);
                _overflowMenu.Items.Add(menuItem);
            }
            _overflowMenu.Show(this, new Point(OverflowBounds.Left, OverflowBounds.Bottom));
        }

        private void RecalculateLayout()
        {
            var x = LeftMargin;
            foreach (var item in _items)
            {
                var textWidth = TextRenderer.MeasureText(item.Text ?? string.Empty, Font,
                    new Size(MaximumTabWidth, ClientSize.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                var width = Math.Max(MinimumTabWidth, Math.Min(MaximumTabWidth, textWidth + 72));
                item.LogicalBounds = new Rectangle(x, TopMargin, width,
                    Math.Max(1, ClientSize.Height - TopMargin + 1));
                x += width;
            }

            _showOverflow = x + LeftMargin > ClientSize.Width;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, MaximumScrollOffset));
            RecalculateVisibleBounds();
        }

        private void RecalculateVisibleBounds()
        {
            foreach (var item in _items)
            {
                var bounds = item.LogicalBounds;
                bounds.Offset(-_scrollOffset, 0);
                item.Bounds = bounds;
                var bodyRight = bounds.Right - TabWing;
                var closeTop = TopMargin + Math.Max(0, (ClientSize.Height - TopMargin - CloseSize) / 2);
                item.CloseBounds = new Rectangle(bodyRight - CloseSize - 8, closeTop, CloseSize, CloseSize);
            }
        }

        private void EnsureSelectedVisible()
        {
            if (!_showOverflow || _selectedIndex < 0 || _selectedIndex >= _items.Count)
            {
                if (!_showOverflow) _scrollOffset = 0;
                RecalculateVisibleBounds();
                return;
            }

            var logical = _items[_selectedIndex].LogicalBounds;
            var viewportLeft = LeftMargin;
            var viewportRight = OverflowBounds.Left - 3;
            if (logical.Left - _scrollOffset < viewportLeft)
                _scrollOffset = Math.Max(0, logical.Left - viewportLeft);
            else if (logical.Right - _scrollOffset > viewportRight)
                _scrollOffset = Math.Min(MaximumScrollOffset, logical.Right - viewportRight);
            RecalculateVisibleBounds();
        }

        private int MaximumScrollOffset
        {
            get
            {
                if (!_showOverflow || _items.Count == 0) return 0;
                var logicalRight = _items[_items.Count - 1].LogicalBounds.Right + LeftMargin;
                return Math.Max(0, logicalRight - OverflowBounds.Left);
            }
        }

        private Rectangle OverflowBounds => new Rectangle(
            Math.Max(0, ClientSize.Width - OverflowAreaWidth + 3),
            Math.Max(3, TopMargin - 2),
            Math.Max(1, OverflowAreaWidth - 9),
            Math.Max(1, ClientSize.Height - TopMargin - 1));

        private void SetToolTipIndex(int index)
        {
            if (_toolTipIndex == index) return;
            _toolTipIndex = index;
            _toolTip.SetToolTip(this, index >= 0 && index < _items.Count
                ? _items[index].ToolTipText
                : string.Empty);
        }

        private void RaiseSelectedTabChanged()
        {
            SelectedTabChanged?.Invoke(this, new TerminalTabEventArgs(SelectedKey));
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
