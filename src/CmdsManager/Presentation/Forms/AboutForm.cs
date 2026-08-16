using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    public sealed class AboutForm : Form
    {
        private const string AuthorText = "iMiKED from 4PDA — https://github.com/iMiKED";
        private const string AuthorUrl = "https://github.com/iMiKED";
        private readonly Bitmap _iconBitmap;

        public AboutForm(LocalizationService text, ApplicationTheme theme = ApplicationTheme.System)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            Text = text["About.Title"];
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(570, 245);
            Icon = ApplicationResources.Icon;

            var title = InformationLabel(ApplicationResources.DisplayName);
            title.Font = new Font(Font.FontFamily, 18f, FontStyle.Bold, GraphicsUnit.Point);
            var versionLabel = InformationLabel(text.Get("About.Version", ApplicationResources.Version));
            versionLabel.Tag = AppThemeManager.MutedTextTag;
            var description = InformationLabel(text["About.Description"]);
            description.MaximumSize = new Size(390, 0);

            var authorText = text["About.Author"] + " " + AuthorText;
            var author = new LinkLabel
            {
                AutoSize = true,
                Text = authorText,
                TextAlign = ContentAlignment.MiddleLeft,
                LinkBehavior = LinkBehavior.HoverUnderline,
                LinkColor = Color.FromArgb(35, 102, 176),
                ActiveLinkColor = Color.FromArgb(22, 73, 133)
            };
            author.Links.Add(authorText.IndexOf(AuthorUrl, StringComparison.Ordinal), AuthorUrl.Length, AuthorUrl);
            author.LinkClicked += OpenAuthorLink;

            const int rowGap = 9;
            title.Margin = new Padding(0, 0, 0, rowGap);
            versionLabel.Margin = new Padding(0, 0, 0, rowGap);
            description.Margin = new Padding(0, 0, 0, rowGap);
            author.Margin = Padding.Empty;
            var information = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(2, 3, 0, 0)
            };
            for (var row = 0; row < 4; row++) information.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            information.Controls.Add(title, 0, 0);
            information.Controls.Add(versionLabel, 0, 1);
            information.Controls.Add(description, 0, 2);
            information.Controls.Add(author, 0, 3);

            _iconBitmap = ApplicationResources.CreateIconBitmap(128);
            var icon = new PictureBox
            {
                Image = _iconBitmap,
                Size = new Size(128, 128),
                SizeMode = PictureBoxSizeMode.Normal,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 1, 18, 0)
            };
            var close = FluentDialogButtons.Primary(text["Common.Close"], DialogResult.OK);
            close.TransparentCanvas = true;
            var buttons = FluentDialogButtons.Footer(close);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(icon, 0, 0);
            layout.Controls.Add(information, 1, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.SetColumnSpan(buttons, 2);

            var background = new FadeGradientPanel { Dock = DockStyle.Fill };
            background.Controls.Add(layout);
            Controls.Add(background);
            AcceptButton = close;
            CancelButton = close;

            var palette = AppThemeManager.Resolve(theme);
            AppThemeManager.ApplyWindow(this, theme);
            layout.BackColor = Color.Transparent;
            information.BackColor = Color.Transparent;
            buttons.BackColor = Color.Transparent;
            background.ApplyPalette(palette);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _iconBitmap?.Dispose();
            base.Dispose(disposing);
        }

        private static Label InformationLabel(string value)
        {
            return new Label
            {
                AutoSize = true,
                Text = value ?? string.Empty,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void OpenAuthorLink(object sender, LinkLabelLinkClickedEventArgs args)
        {
            try { Process.Start(new ProcessStartInfo(AuthorUrl) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        private sealed class FadeGradientPanel : Panel
        {
            private AppThemePalette _palette = AppThemePalette.Light();

            internal FadeGradientPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            internal void ApplyPalette(AppThemePalette palette)
            {
                _palette = palette ?? AppThemePalette.Light();
                Invalidate();
            }

            protected override void OnPaintBackground(PaintEventArgs args)
            {
                if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
                var start = _palette.IsDark ? Color.FromArgb(36, 50, 71) : Color.FromArgb(215, 231, 251);
                var middle = _palette.IsDark ? Color.FromArgb(28, 36, 47) : Color.FromArgb(237, 245, 254);
                var end = _palette.IsDark ? Color.FromArgb(23, 28, 35) : Color.FromArgb(254, 254, 255);
                using (var brush = new LinearGradientBrush(ClientRectangle, start, end, 0f))
                {
                    brush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { start, middle, end },
                        Positions = new[] { 0f, 0.43f, 1f }
                    };
                    args.Graphics.FillRectangle(brush, ClientRectangle);
                }
                using (var accent = new SolidBrush(_palette.Accent))
                    args.Graphics.FillRectangle(accent, 0, 0, 5, ClientRectangle.Height);
            }
        }
    }
}
