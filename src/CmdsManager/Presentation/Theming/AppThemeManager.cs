using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CmdsManager.Domain;
using Microsoft.Win32;

namespace CmdsManager.Presentation.Theming
{
    internal enum FluentToolRole
    {
        Normal,
        Primary,
        Danger
    }

    internal sealed class AppThemePalette
    {
        internal bool IsDark { get; private set; }
        internal Color Window { get; private set; }
        internal Color Surface { get; private set; }
        internal Color SurfaceAlternate { get; private set; }
        internal Color Header { get; private set; }
        internal Color Input { get; private set; }
        internal Color Text { get; private set; }
        internal Color MutedText { get; private set; }
        internal Color DisabledText { get; private set; }
        internal Color Border { get; private set; }
        internal Color GridLine { get; private set; }
        internal Color Hover { get; private set; }
        internal Color Pressed { get; private set; }
        internal Color Selection { get; private set; }
        internal Color SelectionText { get; private set; }
        internal Color Accent { get; private set; }
        internal Color AccentHover { get; private set; }
        internal Color Danger { get; private set; }
        internal Color RunningBackground { get; private set; }
        internal Color StartingBackground { get; private set; }
        internal Color StoppingBackground { get; private set; }
        internal Color FailedBackground { get; private set; }

        internal static AppThemePalette Light()
        {
            return new AppThemePalette
            {
                Window = Color.FromArgb(243, 245, 248),
                Surface = Color.White,
                SurfaceAlternate = Color.FromArgb(249, 250, 252),
                Header = Color.FromArgb(245, 247, 250),
                Input = Color.White,
                Text = Color.FromArgb(24, 32, 43),
                MutedText = Color.FromArgb(94, 106, 122),
                DisabledText = Color.FromArgb(154, 165, 177),
                Border = Color.FromArgb(218, 224, 232),
                GridLine = Color.FromArgb(235, 239, 244),
                Hover = Color.FromArgb(235, 240, 247),
                Pressed = Color.FromArgb(220, 230, 244),
                Selection = Color.FromArgb(232, 241, 255),
                SelectionText = Color.FromArgb(24, 32, 43),
                Accent = Color.FromArgb(37, 99, 235),
                AccentHover = Color.FromArgb(29, 78, 216),
                Danger = Color.FromArgb(180, 35, 24),
                RunningBackground = Color.FromArgb(234, 248, 239),
                StartingBackground = Color.FromArgb(255, 250, 225),
                StoppingBackground = Color.FromArgb(255, 243, 224),
                FailedBackground = Color.FromArgb(253, 235, 236)
            };
        }

        internal static AppThemePalette Dark()
        {
            return new AppThemePalette
            {
                IsDark = true,
                Window = Color.FromArgb(23, 28, 35),
                Surface = Color.FromArgb(27, 34, 44),
                SurfaceAlternate = Color.FromArgb(24, 30, 39),
                Header = Color.FromArgb(31, 39, 50),
                Input = Color.FromArgb(17, 22, 29),
                Text = Color.FromArgb(232, 238, 246),
                MutedText = Color.FromArgb(174, 185, 199),
                DisabledText = Color.FromArgb(110, 123, 140),
                Border = Color.FromArgb(52, 63, 77),
                GridLine = Color.FromArgb(42, 51, 63),
                Hover = Color.FromArgb(44, 54, 67),
                Pressed = Color.FromArgb(53, 66, 83),
                Selection = Color.FromArgb(24, 54, 91),
                SelectionText = Color.White,
                Accent = Color.FromArgb(59, 130, 246),
                AccentHover = Color.FromArgb(96, 165, 250),
                Danger = Color.FromArgb(252, 165, 165),
                RunningBackground = Color.FromArgb(24, 57, 42),
                StartingBackground = Color.FromArgb(67, 56, 25),
                StoppingBackground = Color.FromArgb(70, 47, 24),
                FailedBackground = Color.FromArgb(70, 32, 37)
            };
        }

        internal static AppThemePalette HighContrast()
        {
            return new AppThemePalette
            {
                IsDark = SystemColors.Window.GetBrightness() < 0.5f,
                Window = SystemColors.Control,
                Surface = SystemColors.Window,
                SurfaceAlternate = SystemColors.Window,
                Header = SystemColors.Control,
                Input = SystemColors.Window,
                Text = SystemColors.WindowText,
                MutedText = SystemColors.GrayText,
                DisabledText = SystemColors.GrayText,
                Border = SystemColors.WindowText,
                GridLine = SystemColors.WindowText,
                Hover = SystemColors.Highlight,
                Pressed = SystemColors.Highlight,
                Selection = SystemColors.Highlight,
                SelectionText = SystemColors.HighlightText,
                Accent = SystemColors.Highlight,
                AccentHover = SystemColors.HotTrack,
                Danger = SystemColors.WindowText,
                RunningBackground = SystemColors.Window,
                StartingBackground = SystemColors.Window,
                StoppingBackground = SystemColors.Window,
                FailedBackground = SystemColors.Window
            };
        }
    }

    internal static class AppThemeManager
    {
        internal const string PreserveColorsTag = "CmdsManager.Theme.PreserveColors";
        internal const string MutedTextTag = "CmdsManager.Theme.MutedText";

        internal static AppThemePalette Resolve(ApplicationTheme mode)
        {
            if (mode == ApplicationTheme.System && SystemInformation.HighContrast)
                return AppThemePalette.HighContrast();
            if (mode == ApplicationTheme.Dark || mode == ApplicationTheme.System && SystemUsesDarkTheme())
                return AppThemePalette.Dark();
            return AppThemePalette.Light();
        }

        internal static void ApplyWindow(Form form, ApplicationTheme mode)
        {
            if (form == null) return;
            var palette = Resolve(mode);
            form.SuspendLayout();
            try
            {
                ApplyControlTree(form, palette);
                ApplyTitleBar(form, palette.IsDark);
            }
            finally
            {
                form.ResumeLayout(true);
            }
        }

        internal static void ApplyControlTree(Control control, AppThemePalette palette)
        {
            if (control == null || palette == null) return;
            if (Equals(control.Tag, PreserveColorsTag))
            {
                var preservedFluent = control as IFluentThemedControl;
                preservedFluent?.ApplyPalette(palette);
                return;
            }

            control.ForeColor = palette.Text;
            if (Equals(control.Tag, MutedTextTag)) control.ForeColor = palette.MutedText;
            if (control is Form || control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel || control is UserControl)
                control.BackColor = palette.Window;

            var textBox = control as TextBoxBase;
            if (textBox != null)
            {
                textBox.BackColor = palette.Input;
                textBox.ForeColor = palette.Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }

            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.BackColor = palette.Input;
                combo.ForeColor = palette.Text;
                combo.FlatStyle = FlatStyle.Flat;
            }

            var numeric = control as NumericUpDown;
            if (numeric != null)
            {
                numeric.BackColor = palette.Input;
                numeric.ForeColor = palette.Text;
                numeric.BorderStyle = BorderStyle.FixedSingle;
            }

            var button = control as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = palette.Border;
                button.FlatAppearance.MouseOverBackColor = palette.Hover;
                button.FlatAppearance.MouseDownBackColor = palette.Pressed;
                button.BackColor = palette.Surface;
                button.ForeColor = palette.Text;
                button.Padding = new Padding(5, 1, 5, 1);
            }

            var fluent = control as IFluentThemedControl;
            fluent?.ApplyPalette(palette);

            var link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = palette.Accent;
                link.ActiveLinkColor = palette.AccentHover;
                link.VisitedLinkColor = palette.Accent;
            }

            var tab = control as FluentTabControl;
            if (tab != null) tab.ApplyPalette(palette);

            var page = control as TabPage;
            if (page != null) page.BackColor = palette.Window;

            var strip = control as ToolStrip;
            if (strip != null) ApplyToolStrip(strip, palette);

            var grid = control as DataGridView;
            if (grid != null) ApplyGrid(grid, palette);

            if (!(control is NumericUpDown))
                foreach (Control child in control.Controls) ApplyControlTree(child, palette);
        }

        internal static void ApplyToolStrip(ToolStrip strip, AppThemePalette palette)
        {
            if (strip == null || palette == null) return;
            strip.BackColor = palette.Surface;
            strip.ForeColor = palette.Text;
            strip.Renderer = new FluentToolStripRenderer(palette);
            foreach (ToolStripItem item in strip.Items)
            {
                item.ForeColor = RoleColor(item, palette);
                var host = item as ToolStripControlHost;
                if (host != null && host.Control != null)
                {
                    item.BackColor = palette.Input;
                    host.Control.BackColor = palette.Input;
                    host.Control.ForeColor = palette.Text;
                }
                else item.BackColor = Color.Transparent;
            }
        }

        internal static void ApplyGrid(DataGridView grid, AppThemePalette palette)
        {
            if (grid == null || palette == null) return;
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = palette.Surface;
            grid.GridColor = palette.GridLine;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.MutedText;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Header;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            grid.DefaultCellStyle.BackColor = palette.Surface;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.SurfaceAlternate;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
        }

        internal static Color RoleColor(ToolStripItem item, AppThemePalette palette)
        {
            if (!item.Enabled) return palette.DisabledText;
            var role = item.Tag is FluentToolRole ? (FluentToolRole)item.Tag : FluentToolRole.Normal;
            if (role == FluentToolRole.Danger) return palette.Danger;
            if (role == FluentToolRole.Primary) return Color.White;
            return palette.Text;
        }

        internal static bool SystemUsesDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme");
                    return value != null && Convert.ToInt32(value) == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void ApplyTitleBar(Form form, bool dark)
        {
            if (form == null || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                var enabled = dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        internal static void ApplyWindowCorners(Form form)
        {
            if (form == null || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            try
            {
                const int round = 2;
                var preference = round;
                DwmSetWindowAttribute(form.Handle, 33, ref preference, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    internal sealed class FluentToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly AppThemePalette _palette;

        internal FluentToolStripRenderer(AppThemePalette palette)
            : base(new FluentColorTable(palette))
        {
            _palette = palette ?? throw new ArgumentNullException(nameof(palette));
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs args)
        {
            args.Graphics.Clear(_palette.Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs args)
        {
            using (var pen = new Pen(_palette.Border))
                args.Graphics.DrawLine(pen, 0, args.ToolStrip.Height - 1, args.ToolStrip.Width, args.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs args)
        {
            var button = args.Item as ToolStripButton;
            if (button == null) { base.OnRenderButtonBackground(args); return; }
            var role = button.Tag is FluentToolRole ? (FluentToolRole)button.Tag : FluentToolRole.Normal;
            Color fill;
            if (!button.Enabled)
            {
                if (role != FluentToolRole.Primary) return;
                fill = Color.FromArgb(80, _palette.Accent);
            }
            else if (role == FluentToolRole.Primary)
            {
                fill = button.Pressed ? _palette.AccentHover : _palette.Accent;
            }
            else if (button.Pressed || button.Checked)
            {
                fill = _palette.Pressed;
            }
            else if (button.Selected)
            {
                fill = _palette.Hover;
            }
            else
            {
                return;
            }

            var bounds = new RectangleF(1.5f, 1.5f,
                Math.Max(1f, button.Width - 3.5f), Math.Max(1f, button.Height - 3.5f));
            var smoothing = args.Graphics.SmoothingMode;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRectangle(bounds, 6f))
            using (var brush = new SolidBrush(fill))
            {
                args.Graphics.FillPath(brush, path);
            }
            args.Graphics.SmoothingMode = smoothing;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs args)
        {
            if (!args.Item.Selected && !args.Item.Pressed) return;
            var bounds = new Rectangle(2, 1, Math.Max(1, args.Item.Width - 4), Math.Max(1, args.Item.Height - 2));
            using (var path = RoundedRectangle(bounds, 5))
            using (var brush = new SolidBrush(args.Item.Pressed ? _palette.Pressed : _palette.Hover))
                args.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
        {
            args.TextColor = AppThemeManager.RoleColor(args.Item, _palette);
            base.OnRenderItemText(args);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs args)
        {
            using (var pen = new Pen(_palette.Border))
            {
                if (args.Vertical)
                {
                    var x = args.Item.Width / 2;
                    args.Graphics.DrawLine(pen, x, 8, x, Math.Max(8, args.Item.Height - 8));
                }
                else
                {
                    var y = args.Item.Height / 2;
                    args.Graphics.DrawLine(pen, 6, y, Math.Max(6, args.Item.Width - 6), y);
                }
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs args)
        {
            args.ArrowColor = args.Item.Enabled ? _palette.Text : _palette.DisabledText;
            base.OnRenderArrow(args);
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            radius = Math.Max(1f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class FluentColorTable : ProfessionalColorTable
        {
            private readonly AppThemePalette _palette;

            internal FluentColorTable(AppThemePalette palette) { _palette = palette; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground => _palette.Surface;
            public override Color ImageMarginGradientBegin => _palette.Surface;
            public override Color ImageMarginGradientMiddle => _palette.Surface;
            public override Color ImageMarginGradientEnd => _palette.Surface;
            public override Color MenuBorder => _palette.Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => _palette.Hover;
            public override Color SeparatorDark => _palette.Border;
            public override Color SeparatorLight => _palette.Border;
        }
    }

    internal sealed class FluentTabControl : TabControl
    {
        private AppThemePalette _palette = AppThemePalette.Light();
        private int _hotIndex = -1;

        internal FluentTabControl()
        {
            SizeMode = TabSizeMode.Normal;
            ItemSize = new Size(90, 27);
            Padding = new Point(12, 4);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal void ApplyPalette(AppThemePalette palette)
        {
            _palette = palette ?? AppThemePalette.Light();
            BackColor = _palette.Window;
            ForeColor = _palette.Text;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            args.Graphics.Clear(_palette.Window);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(_palette.Window);
            var headerBottom = TabCount == 0 ? 30 : GetTabRect(0).Bottom + 2;
            using (var header = new SolidBrush(_palette.Header))
                args.Graphics.FillRectangle(header, 0, 0, ClientSize.Width, headerBottom);
            using (var border = new Pen(_palette.Border))
                args.Graphics.DrawLine(border, 0, headerBottom - 1, ClientSize.Width, headerBottom - 1);

            for (var index = 0; index < TabCount; index++)
            {
                var selected = SelectedIndex == index;
                var bounds = GetTabRect(index);
                bounds = new Rectangle(bounds.Left + 1, 2, Math.Max(1, bounds.Width - 2), Math.Max(1, headerBottom - 3));
                var fill = selected ? _palette.Surface : index == _hotIndex ? _palette.Hover : _palette.Header;
                using (var brush = new SolidBrush(fill)) args.Graphics.FillRectangle(brush, bounds);
                if (selected)
                {
                    using (var accent = new SolidBrush(_palette.Accent))
                        args.Graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - 2, bounds.Width, 2);
                }
                TextRenderer.DrawText(args.Graphics, TabPages[index].Text, Font, bounds,
                    selected ? _palette.Text : _palette.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            var next = -1;
            for (var index = 0; index < TabCount; index++)
            {
                if (GetTabRect(index).Contains(args.Location)) { next = index; break; }
            }
            if (_hotIndex == next) return;
            _hotIndex = next;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs args)
        {
            base.OnMouseLeave(args);
            if (_hotIndex < 0) return;
            _hotIndex = -1;
            Invalidate();
        }

        protected override void OnSelectedIndexChanged(EventArgs args)
        {
            base.OnSelectedIndexChanged(args);
            Invalidate();
        }
    }
}
