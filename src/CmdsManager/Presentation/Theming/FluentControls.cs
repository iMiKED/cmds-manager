using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Infrastructure.Windows;

namespace CmdsManager.Presentation.Theming
{
    internal interface IFluentThemedControl
    {
        void ApplyPalette(AppThemePalette palette);
    }

    internal static class FluentDialogButtons
    {
        internal static FluentButton Primary(string text, DialogResult dialogResult = DialogResult.None)
        {
            return new FluentButton
            {
                Text = text,
                DialogResult = dialogResult,
                AutoSize = true,
                Primary = true
            };
        }

        internal static FluentButton Secondary(string text, DialogResult dialogResult = DialogResult.None)
        {
            return new FluentButton
            {
                Text = text,
                DialogResult = dialogResult,
                AutoSize = true
            };
        }

        internal static FlowLayoutPanel Footer(params FluentButton[] buttons)
        {
            var footer = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(0, 4, 6, 5)
            };
            footer.Controls.AddRange(buttons ?? new FluentButton[0]);
            return footer;
        }
    }

    internal static class FluentGeometry
    {
        internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            radius = Math.Max(1f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
            var diameter = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        internal static void ApplyRoundedRegion(Control control, float radius)
        {
            if (control == null || control.Width <= 1 || control.Height <= 1) return;
            using (var path = RoundedRectangle(new RectangleF(0f, 0f, control.Width, control.Height), radius))
            {
                var replacement = new Region(path);
                var previous = control.Region;
                control.Region = replacement;
                previous?.Dispose();
            }
        }

        internal static Color Blend(Color first, Color second, int secondPercent)
        {
            secondPercent = Math.Max(0, Math.Min(100, secondPercent));
            var firstPercent = 100 - secondPercent;
            return Color.FromArgb(
                (first.A * firstPercent + second.A * secondPercent) / 100,
                (first.R * firstPercent + second.R * secondPercent) / 100,
                (first.G * firstPercent + second.G * secondPercent) / 100,
                (first.B * firstPercent + second.B * secondPercent) / 100);
        }
    }

    internal sealed class FluentButton : Button, IFluentThemedControl
    {
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;
        private bool _pressed;

        internal FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Padding = new Padding(7, 2, 7, 2);
            MinimumSize = new Size(0, 28);
        }

        internal bool Primary { get; set; }
        internal bool UseAssignedColors { get; set; }
        internal bool TransparentCanvas { get; set; }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            FlatAppearance.BorderSize = 0;
            Padding = new Padding(7, 2, 7, 2);
            if (!UseAssignedColors)
            {
                BackColor = TransparentCanvas
                    ? Color.Transparent
                    : Primary ? _palette.Accent : _palette.Surface;
                ForeColor = Primary ? Color.White : _palette.Text;
            }
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var measured = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return new Size(Math.Max(68, measured.Width + Padding.Horizontal + 8),
                Math.Max(28, measured.Height + Padding.Vertical + 6));
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var canvas = Parent?.BackColor ?? _palette.Window;
            if (TransparentCanvas) base.OnPaintBackground(args);
            else args.Graphics.Clear(canvas);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var fill = UseAssignedColors ? BackColor : Primary ? _palette.Accent : _palette.Surface;
            var border = UseAssignedColors ? FluentGeometry.Blend(fill, ForeColor, 45) :
                Primary ? _palette.Accent : _palette.Border;
            if (!Enabled)
            {
                fill = FluentGeometry.Blend(canvas, fill, 45);
                border = _palette.Border;
            }
            else if (_pressed)
            {
                fill = Primary ? _palette.AccentHover : _palette.Pressed;
            }
            else if (_hot)
            {
                fill = Primary ? _palette.AccentHover :
                    UseAssignedColors ? FluentGeometry.Blend(fill, Color.White, 10) : _palette.Hover;
            }

            var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using (var path = FluentGeometry.RoundedRectangle(bounds, 6f))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused && Enabled ? _palette.Accent : border, 1f)
            {
                Alignment = PenAlignment.Inset
            })
            {
                args.Graphics.FillPath(brush, path);
                args.Graphics.DrawPath(pen, path);
            }

            var textColor = !Enabled ? _palette.DisabledText :
                UseAssignedColors ? ForeColor : Primary ? Color.White : _palette.Text;
            TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            base.OnMouseEnter(args);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            _hot = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            if (args.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            base.OnMouseUp(args);
            _pressed = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs args)
        {
            base.OnGotFocus(args);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs args)
        {
            base.OnLostFocus(args);
            _pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs args)
        {
            base.OnEnabledChanged(args);
            Invalidate();
        }
    }

    internal sealed class FluentCheckBox : CheckBox, IFluentThemedControl
    {
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = true;
            BackColor = Color.Transparent;
        }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            ForeColor = _palette.Text;
            BackColor = Color.Transparent;
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return new Size(text.Width + 25, Math.Max(21, text.Height + 5));
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(Parent?.BackColor ?? _palette.Window);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            const int boxSize = 16;
            var box = new RectangleF(0.5f, Math.Max(0.5f, (Height - boxSize) / 2f), boxSize, boxSize);
            var border = _hot && Enabled ? _palette.Accent : _palette.Border;
            var fill = Checked ? _palette.Accent : _palette.Input;
            if (!Enabled) fill = FluentGeometry.Blend(_palette.Window, fill, 50);
            using (var path = FluentGeometry.RoundedRectangle(box, 3.5f))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Checked ? _palette.Accent : border, 1f))
            {
                args.Graphics.FillPath(brush, path);
                args.Graphics.DrawPath(pen, path);
            }

            if (CheckState == CheckState.Checked)
            {
                using (var pen = new Pen(Color.White, 1.8f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                })
                {
                    args.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(4.2f, box.Top + 8.1f),
                        new PointF(7.0f, box.Top + 11.0f),
                        new PointF(12.7f, box.Top + 5.1f)
                    });
                }
            }
            else if (CheckState == CheckState.Indeterminate)
            {
                using (var pen = new Pen(Color.White, 1.8f))
                    args.Graphics.DrawLine(pen, 4f, box.Top + 8f, 13f, box.Top + 8f);
            }

            var textBounds = new Rectangle(24, 0, Math.Max(0, Width - 24), Height);
            TextRenderer.DrawText(args.Graphics, Text, Font, textBounds,
                Enabled ? _palette.Text : _palette.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                var focus = textBounds;
                focus.Inflate(-1, -2);
                ControlPaint.DrawFocusRectangle(args.Graphics, focus, _palette.Text, Color.Transparent);
            }
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            base.OnMouseEnter(args);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            _hot = false;
            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs args)
        {
            base.OnCheckedChanged(args);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs args)
        {
            base.OnEnabledChanged(args);
            Invalidate();
        }
    }

    internal sealed class FluentTextBox : UserControl, IFluentThemedControl
    {
        private const int ControlHeight = 29;
        private const int CornerRadius = 6;
        private const int HorizontalTextMargin = 8;
        private readonly TextBox _editor;
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            MinimumSize = new Size(0, ControlHeight);
            Size = new Size(100, ControlHeight);
            Height = ControlHeight;

            _editor = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Tag = AppThemeManager.PreserveColorsTag
            };
            Controls.Add(_editor);
            _editor.TextChanged += (sender, args) =>
            {
                if (!string.Equals(base.Text, _editor.Text, StringComparison.Ordinal)) base.Text = _editor.Text;
            };
            _editor.Enter += (sender, args) => Invalidate();
            _editor.Leave += (sender, args) => Invalidate();
            TrackHover(this);
            LayoutEditor();
        }

        public override string Text
        {
            get { return _editor == null ? base.Text : _editor.Text; }
            set
            {
                base.Text = value ?? string.Empty;
                if (_editor != null && !string.Equals(_editor.Text, base.Text, StringComparison.Ordinal))
                    _editor.Text = base.Text;
            }
        }

        internal bool ReadOnly { get { return _editor.ReadOnly; } set { _editor.ReadOnly = value; } }

        internal void SelectAll()
        {
            _editor.Focus();
            _editor.SelectAll();
        }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Input;
            ForeColor = _palette.Text;
            _editor.BackColor = _palette.Input;
            _editor.ForeColor = _palette.Text;
            _editor.BorderStyle = BorderStyle.None;
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
            Invalidate(true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var preferred = base.GetPreferredSize(proposedSize);
            return new Size(preferred.Width, Math.Max(ControlHeight, _editor.PreferredHeight + 10));
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            LayoutEditor();
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            using (var path = FluentGeometry.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), CornerRadius))
            using (var pen = new Pen(_editor.Focused ? _palette.Accent : _hot ? _palette.MutedText : _palette.Border, 1f)
            {
                Alignment = PenAlignment.Inset
            })
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnClick(EventArgs args)
        {
            base.OnClick(args);
            _editor.Focus();
        }

        private void LayoutEditor()
        {
            if (_editor == null) return;
            var editorHeight = _editor.PreferredHeight;
            _editor.SetBounds(HorizontalTextMargin, Math.Max(0, (Height - editorHeight) / 2),
                Math.Max(1, Width - HorizontalTextMargin * 2), editorHeight);
        }

        private void TrackHover(Control control)
        {
            control.MouseEnter += (sender, args) => SetHot(true);
            control.MouseLeave += (sender, args) => SetHot(ClientRectangle.Contains(PointToClient(MousePosition)));
            foreach (Control child in control.Controls) TrackHover(child);
        }

        private void SetHot(bool value)
        {
            if (_hot == value) return;
            _hot = value;
            Invalidate();
        }
    }

    internal sealed class FluentHotkeyBox : UserControl, IFluentThemedControl
    {
        private const int ControlHeight = 29;
        private const int CornerRadius = 6;
        private const int HorizontalTextMargin = 8;
        private readonly TextBox _editor;
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentHotkeyBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            MinimumSize = new Size(120, ControlHeight);
            Size = new Size(170, ControlHeight);
            Height = ControlHeight;
            TabStop = false;

            _editor = new TextBox
            {
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ShortcutsEnabled = false,
                TabStop = true,
                Tag = AppThemeManager.PreserveColorsTag
            };
            Controls.Add(_editor);
            _editor.KeyDown += HandleEditorKeyDown;
            _editor.PreviewKeyDown += (sender, args) => args.IsInputKey = true;
            _editor.Enter += (sender, args) => Invalidate();
            _editor.Leave += (sender, args) => Invalidate();
            TrackHover(this);
            LayoutEditor();
        }

        internal string Gesture
        {
            get { return _editor.Text; }
            set
            {
                ShowAppHotkeyGesture parsed;
                _editor.Text = ShowAppHotkeyGesture.TryParse(value, out parsed)
                    ? parsed.ToString()
                    : (value ?? string.Empty).Trim();
            }
        }

        internal event EventHandler GestureChanged;

        internal void ClearGesture()
        {
            if (_editor.Text.Length == 0) return;
            _editor.Clear();
            GestureChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Input;
            ForeColor = Enabled ? _palette.Text : _palette.DisabledText;
            _editor.BackColor = _palette.Input;
            _editor.ForeColor = Enabled ? _palette.Text : _palette.DisabledText;
            _editor.BorderStyle = BorderStyle.None;
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
            Invalidate(true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var preferred = base.GetPreferredSize(proposedSize);
            return new Size(preferred.Width, Math.Max(ControlHeight, _editor.PreferredHeight + 10));
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            LayoutEditor();
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            using (var path = FluentGeometry.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), CornerRadius))
            using (var pen = new Pen(_editor.Focused ? _palette.Accent : _hot ? _palette.MutedText : _palette.Border, 1f)
            {
                Alignment = PenAlignment.Inset
            })
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnClick(EventArgs args)
        {
            base.OnClick(args);
            _editor.Focus();
            _editor.SelectAll();
        }

        protected override void OnEnabledChanged(EventArgs args)
        {
            base.OnEnabledChanged(args);
            _editor.ForeColor = Enabled ? _palette.Text : _palette.DisabledText;
            Invalidate(true);
        }

        private void HandleEditorKeyDown(object sender, KeyEventArgs args)
        {
            if (IsModifierKey(args.KeyCode))
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            args.Handled = true;
            args.SuppressKeyPress = true;

            if (args.KeyCode == Keys.Back || args.KeyCode == Keys.Delete || args.KeyCode == Keys.Escape)
            {
                ClearGesture();
                return;
            }

            var modifiers = ShowAppHotkeyModifiers.None;
            if (args.Control) modifiers |= ShowAppHotkeyModifiers.Control;
            if (args.Alt) modifiers |= ShowAppHotkeyModifiers.Alt;
            if (args.Shift) modifiers |= ShowAppHotkeyModifiers.Shift;
            if (IsPressed(Keys.LWin) || IsPressed(Keys.RWin)) modifiers |= ShowAppHotkeyModifiers.Win;

            ShowAppHotkeyGesture gesture;
            if (!ShowAppHotkeyGesture.TryCreate(args.KeyCode, modifiers, out gesture)) return;
            if (string.Equals(_editor.Text, gesture.ToString(), StringComparison.Ordinal)) return;
            _editor.Text = gesture.ToString();
            _editor.SelectAll();
            GestureChanged?.Invoke(this, EventArgs.Empty);
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.LWin || key == Keys.RWin;
        }

        private static bool IsPressed(Keys key)
        {
            return (NativeMethods.GetKeyState((int)key) & 0x8000) != 0;
        }

        private void LayoutEditor()
        {
            if (_editor == null) return;
            var editorHeight = _editor.PreferredHeight;
            _editor.SetBounds(HorizontalTextMargin, Math.Max(0, (Height - editorHeight) / 2),
                Math.Max(1, Width - HorizontalTextMargin * 2), editorHeight);
        }

        private void TrackHover(Control control)
        {
            control.MouseEnter += (sender, args) => SetHot(true);
            control.MouseLeave += (sender, args) => SetHot(ClientRectangle.Contains(PointToClient(MousePosition)));
            foreach (Control child in control.Controls) TrackHover(child);
        }

        private void SetHot(bool value)
        {
            if (_hot == value) return;
            _hot = value;
            Invalidate();
        }
    }

    internal sealed class FluentComboBox : ComboBox, IFluentThemedControl
    {
        private const int WmPaint = 0x000F;
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 20;
            IntegralHeight = false;
        }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Input;
            ForeColor = _palette.Text;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Math.Max(20, Font.Height + 5);
            FluentGeometry.ApplyRoundedRegion(this, 5f);
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs args)
        {
            if (args.Index < 0) return;
            var selected = (args.State & DrawItemState.Selected) == DrawItemState.Selected;
            var background = selected ? _palette.Selection : _palette.Input;
            var foreground = selected ? _palette.SelectionText : _palette.Text;
            using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
            var textBounds = new Rectangle(args.Bounds.Left + 7, args.Bounds.Top,
                Math.Max(0, args.Bounds.Width - 10), args.Bounds.Height);
            TextRenderer.DrawText(args.Graphics, GetItemText(Items[args.Index]), Font, textBounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmPaint && IsHandleCreated) DrawChrome();
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            FluentGeometry.ApplyRoundedRegion(this, 5f);
        }

        protected override void OnMouseEnter(EventArgs args)
        {
            base.OnMouseEnter(args);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            _hot = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs args)
        {
            base.OnGotFocus(args);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs args)
        {
            base.OnLostFocus(args);
            Invalidate();
        }

        protected override void OnDropDown(EventArgs args)
        {
            base.OnDropDown(args);
            Invalidate();
        }

        protected override void OnDropDownClosed(EventArgs args)
        {
            base.OnDropDownClosed(args);
            Invalidate();
        }

        private void DrawChrome()
        {
            using (var graphics = Graphics.FromHwnd(Handle))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var arrowArea = new Rectangle(Math.Max(1, Width - 27), 1, 26, Math.Max(1, Height - 2));
                using (var brush = new SolidBrush(_palette.Input)) graphics.FillRectangle(brush, arrowArea);
                using (var path = FluentGeometry.RoundedRectangle(
                    new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), 5f))
                using (var pen = new Pen(Focused || DroppedDown ? _palette.Accent :
                    _hot ? _palette.MutedText : _palette.Border, 1f) { Alignment = PenAlignment.Inset })
                {
                    graphics.DrawPath(pen, path);
                }

                var centerX = Width - 13f;
                var centerY = Height / 2f;
                using (var pen = new Pen(Enabled ? _palette.MutedText : _palette.DisabledText, 1.5f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                })
                {
                    graphics.DrawLine(pen, centerX - 3.5f, centerY - 1.5f, centerX, centerY + 2f);
                    graphics.DrawLine(pen, centerX, centerY + 2f, centerX + 3.5f, centerY - 1.5f);
                }
            }
        }
    }

    internal sealed class FluentNumericUpDown : UserControl, IFluentThemedControl
    {
        private const int ControlHeight = 29;
        private const int CornerRadius = 6;
        private readonly NumericUpDown _valueControl;
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentNumericUpDown()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            MinimumSize = new Size(0, ControlHeight);
            Size = new Size(72, ControlHeight);
            Height = ControlHeight;

            _valueControl = new NumericUpDown
            {
                AutoSize = true,
                BorderStyle = BorderStyle.None,
                Tag = AppThemeManager.PreserveColorsTag,
                TextAlign = HorizontalAlignment.Right
            };
            Controls.Add(_valueControl);
            _valueControl.Enter += (sender, args) => Invalidate();
            _valueControl.Leave += (sender, args) => Invalidate();
            TrackHover(this);
            AttachButtonPainter();
            LayoutEditor();
        }

        internal decimal Minimum { get { return _valueControl.Minimum; } set { _valueControl.Minimum = value; } }
        internal decimal Maximum { get { return _valueControl.Maximum; } set { _valueControl.Maximum = value; } }
        internal decimal Value { get { return _valueControl.Value; } set { _valueControl.Value = value; } }
        internal decimal Increment { get { return _valueControl.Increment; } set { _valueControl.Increment = value; } }
        internal int DecimalPlaces { get { return _valueControl.DecimalPlaces; } set { _valueControl.DecimalPlaces = value; } }
        internal HorizontalAlignment TextAlign { get { return _valueControl.TextAlign; } set { _valueControl.TextAlign = value; } }
        internal bool ThousandsSeparator { get { return _valueControl.ThousandsSeparator; } set { _valueControl.ThousandsSeparator = value; } }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Input;
            ForeColor = _palette.Text;
            _valueControl.BackColor = _palette.Input;
            _valueControl.ForeColor = _palette.Text;
            _valueControl.BorderStyle = BorderStyle.None;
            foreach (Control child in _valueControl.Controls)
            {
                child.BackColor = _palette.Input;
                child.ForeColor = _palette.Text;
                var editor = child as TextBoxBase;
                if (editor != null) editor.BorderStyle = BorderStyle.None;
            }
            AttachButtonPainter();
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
            Invalidate(true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var preferred = base.GetPreferredSize(proposedSize);
            return new Size(preferred.Width, Math.Max(ControlHeight, _valueControl.PreferredHeight + 10));
        }

        protected override void OnResize(EventArgs args)
        {
            base.OnResize(args);
            LayoutEditor();
            FluentGeometry.ApplyRoundedRegion(this, CornerRadius);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            using (var path = FluentGeometry.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), CornerRadius))
            using (var pen = new Pen(_valueControl.Focused ? _palette.Accent : _hot ? _palette.MutedText : _palette.Border, 1f)
            {
                Alignment = PenAlignment.Inset
            })
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnClick(EventArgs args)
        {
            base.OnClick(args);
            _valueControl.Focus();
        }

        private void LayoutEditor()
        {
            if (_valueControl == null) return;
            var editorHeight = _valueControl.PreferredHeight;
            _valueControl.SetBounds(2, Math.Max(0, (Height - editorHeight) / 2),
                Math.Max(1, Width - 4), editorHeight);
        }

        private void TrackHover(Control control)
        {
            control.MouseEnter += (sender, args) => SetHot(true);
            control.MouseLeave += (sender, args) => SetHot(ClientRectangle.Contains(PointToClient(MousePosition)));
            foreach (Control child in control.Controls) TrackHover(child);
        }

        private void SetHot(bool value)
        {
            if (_hot == value) return;
            _hot = value;
            Invalidate();
        }

        private void AttachButtonPainter()
        {
            var buttons = _valueControl.Controls.Cast<Control>().FirstOrDefault(control =>
                control.GetType().Name.IndexOf("UpDownButtons", StringComparison.OrdinalIgnoreCase) >= 0);
            if (buttons == null) return;
            buttons.Paint -= DrawButtons;
            buttons.Paint += DrawButtons;
        }

        private void DrawButtons(object sender, PaintEventArgs args)
        {
            var buttons = sender as Control;
            if (buttons == null) return;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(_palette.Input))
                args.Graphics.FillRectangle(brush, buttons.ClientRectangle);
            using (var divider = new Pen(_palette.Border))
            {
                args.Graphics.DrawLine(divider, 0f, buttons.Height / 2f,
                    buttons.Width, buttons.Height / 2f);
                args.Graphics.DrawLine(divider, 0f, 1f, 0f, Math.Max(1f, buttons.Height - 2f));
            }

            var centerX = buttons.Width / 2f;
            var quarter = buttons.Height / 4f;
            using (var pen = new Pen(Enabled ? _palette.MutedText : _palette.DisabledText, 1.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                args.Graphics.DrawLine(pen, centerX - 2.5f, quarter + 1f, centerX, quarter - 1.5f);
                args.Graphics.DrawLine(pen, centerX, quarter - 1.5f, centerX + 2.5f, quarter + 1f);
                var lower = quarter * 3f;
                args.Graphics.DrawLine(pen, centerX - 2.5f, lower - 1f, centerX, lower + 1.5f);
                args.Graphics.DrawLine(pen, centerX, lower + 1.5f, centerX + 2.5f, lower - 1f);
            }
        }
    }
}
