using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Forms
{
    public sealed class AboutForm : Form
    {
        private const string AuthorText = "iMiKED from 4PDA";
        private const string AuthorUrl = "https://4pda.to/forum/index.php?showuser=1017942";
        private const string LicenseText = "GNU GPL v3.0";
        private const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";
        private const string WebsiteUrl = "https://github.com/iMiKED/cmds-manager";
        private const string DonateUrl = "https://github.com/iMiKED/cmds-manager?tab=readme-ov-file#support-the-project";
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
            ClientSize = new Size(570, 290);
            Icon = ApplicationResources.Icon;

            var title = new OpticallyAlignedTitleLabel { Text = ApplicationResources.DisplayName };
            title.Font = new Font(Font.FontFamily, 18f, FontStyle.Bold, GraphicsUnit.Point);
            var versionLabel = InformationLabel(text.Get("About.Version", ApplicationResources.Version));
            versionLabel.Tag = AppThemeManager.MutedTextTag;
            var buildLabel = InformationLabel(text.Get("About.Build", ApplicationResources.BuildTimestamp));
            buildLabel.Tag = AppThemeManager.MutedTextTag;
            var description = InformationLabel(text["About.Description"]);
            description.MaximumSize = new Size(390, 0);

            var author = InformationLink(text["About.Author"], AuthorText, AuthorUrl);
            var license = InformationLink(text["About.License"], LicenseText, LicenseUrl);
            var website = InformationLink(text["About.Website"], WebsiteUrl, WebsiteUrl);

            var informationRows = new Control[]
            {
                title, versionLabel, buildLabel, description, author, license, website
            };
            var detailRowHeight = Math.Max(21,
                informationRows.Skip(1).Max(control => control.PreferredSize.Height + 4));
            var titleRowHeight = Math.Max(32, title.PreferredSize.Height + 3);
            foreach (var rowControl in informationRows)
            {
                rowControl.AutoSize = false;
                rowControl.Dock = DockStyle.Fill;
                rowControl.Margin = Padding.Empty;
            }
            var information = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = titleRowHeight + detailRowHeight * (informationRows.Length - 1),
                ColumnCount = 1,
                RowCount = informationRows.Length,
                Margin = new Padding(2, 3, 0, 0),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            information.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < informationRows.Length; row++)
            {
                information.RowStyles.Add(new RowStyle(SizeType.Absolute,
                    row == 0 ? titleRowHeight : detailRowHeight));
                information.Controls.Add(informationRows[row], 0, row);
            }

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
            var donate = FluentDialogButtons.Secondary(text["About.Donate"]);
            donate.TransparentCanvas = true;
            donate.AccessibleDescription = DonateUrl;
            donate.Click += (sender, args) => OpenUrl(DonateUrl);
            var buttons = FluentDialogButtons.Footer(close, donate);

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

        private sealed class OpticallyAlignedTitleLabel : Label
        {
            internal OpticallyAlignedTitleLabel()
            {
                TextAlign = ContentAlignment.MiddleLeft;
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                var textBounds = new Rectangle(1, 0, Math.Max(0, ClientSize.Width - 1), ClientSize.Height);
                TextRenderer.DrawText(args.Graphics, Text ?? string.Empty, Font, textBounds, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }

        private static LinkLabel InformationLink(string label, string value, string url)
        {
            var prefix = (label ?? string.Empty).TrimEnd();
            var linkText = prefix.Length == 0 ? value : prefix + " " + value;
            var link = new LinkLabel
            {
                AutoSize = true,
                Text = linkText,
                TextAlign = ContentAlignment.MiddleLeft,
                LinkBehavior = LinkBehavior.HoverUnderline,
                LinkColor = Color.FromArgb(35, 102, 176),
                ActiveLinkColor = Color.FromArgb(22, 73, 133)
            };
            link.Links.Add(linkText.Length - value.Length, value.Length, url);
            link.LinkClicked += OpenInformationLink;
            return link;
        }

        private static void OpenInformationLink(object sender, LinkLabelLinkClickedEventArgs args)
        {
            OpenUrl(Convert.ToString(args.Link.LinkData));
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
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
