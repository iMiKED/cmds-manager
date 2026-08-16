using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CmdsManager.Presentation.Theming
{
    internal interface IFluentThemedControl
    {
        void ApplyPalette(AppThemePalette palette);
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

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            FlatAppearance.BorderSize = 0;
            Padding = new Padding(7, 2, 7, 2);
            if (!UseAssignedColors)
            {
                BackColor = Primary ? _palette.Accent : _palette.Surface;
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
            args.Graphics.Clear(canvas);
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

    internal sealed class FluentTextBox : TextBox, IFluentThemedControl
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentTextBox()
        {
            BorderStyle = BorderStyle.FixedSingle;
        }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Input;
            ForeColor = _palette.Text;
            BorderStyle = BorderStyle.FixedSingle;
            FluentGeometry.ApplyRoundedRegion(this, 5f);
            Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawBorder();
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

        private void DrawBorder()
        {
            using (var graphics = Graphics.FromHwnd(Handle))
            using (var path = FluentGeometry.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), 5f))
            using (var pen = new Pen(Focused ? _palette.Accent : _hot ? _palette.MutedText : _palette.Border, 1f)
            {
                Alignment = PenAlignment.Inset
            })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawPath(pen, path);
            }
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

    internal sealed class FluentNumericUpDown : NumericUpDown, IFluentThemedControl
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private AppThemePalette _palette = AppThemePalette.Light();
        private bool _hot;

        internal FluentNumericUpDown()
        {
            BorderStyle = BorderStyle.FixedSingle;
            AttachButtonPainter();
        }

        public void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Input;
            ForeColor = _palette.Text;
            BorderStyle = BorderStyle.FixedSingle;
            foreach (Control child in Controls)
            {
                child.BackColor = _palette.Input;
                child.ForeColor = _palette.Text;
                var editor = child as TextBoxBase;
                if (editor != null) editor.BorderStyle = BorderStyle.None;
            }
            FluentGeometry.ApplyRoundedRegion(this, 5f);
            Invalidate(true);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawBorder();
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

        protected override void OnEnter(EventArgs args)
        {
            base.OnEnter(args);
            Invalidate();
        }

        protected override void OnLeave(EventArgs args)
        {
            base.OnLeave(args);
            Invalidate();
        }

        private void DrawBorder()
        {
            using (var graphics = Graphics.FromHwnd(Handle))
            using (var path = FluentGeometry.RoundedRectangle(
                new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), 5f))
            using (var pen = new Pen(Focused ? _palette.Accent : _hot ? _palette.MutedText : _palette.Border, 1f)
            {
                Alignment = PenAlignment.Inset
            })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawPath(pen, path);
            }
        }

        private void AttachButtonPainter()
        {
            var buttons = Controls.Cast<Control>().FirstOrDefault(control =>
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
