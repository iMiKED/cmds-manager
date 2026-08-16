using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CmdsManager.Application;

namespace CmdsManager.Presentation.Forms
{
    public sealed class AboutForm : Form
    {
        private const string AuthorText = "iMiKED from 4PDA — https://github.com/iMiKED";
        private const string AuthorLinkText = "https://github.com/iMiKED";
        private const string AuthorUrl = "https://github.com/iMiKED";
        private readonly Bitmap _iconBitmap;

        public AboutForm(LocalizationService text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            Text = text["About.Title"];
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(390, 180);
            Icon = ApplicationResources.Icon;

            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "-";
            var title = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), Text = "CmdsManager" };
            var description = new Label { AutoSize = true, MaximumSize = new Size(350, 0), Text = text["About.Description"] };
            var versionLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = text.Get("About.Version", version) };
            var author = new LinkLabel { AutoSize = true, Text = AuthorText, LinkBehavior = LinkBehavior.HoverUnderline };
            author.Links.Add(AuthorText.IndexOf(AuthorLinkText, StringComparison.Ordinal), AuthorLinkText.Length, AuthorUrl);
            author.LinkClicked += OpenAuthorLink;
            _iconBitmap = ApplicationResources.Icon.ToBitmap();
            var icon = new PictureBox
            {
                Image = _iconBitmap,
                Size = new Size(56, 56),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 2, 10, 0)
            };
            var close = new Button { Text = text["Common.Close"], DialogResult = DialogResult.OK, AutoSize = true };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            buttons.Controls.Add(close);
            var information = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            information.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            information.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            information.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            information.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            title.Margin = new Padding(0, 0, 0, 6);
            description.Margin = new Padding(1, 0, 0, 5);
            versionLabel.Margin = new Padding(1, 0, 0, 3);
            author.Margin = new Padding(1, 0, 0, 0);
            information.Controls.Add(title, 0, 0);
            information.Controls.Add(description, 0, 1);
            information.Controls.Add(versionLabel, 0, 2);
            information.Controls.Add(author, 0, 3);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(icon, 0, 0);
            layout.Controls.Add(information, 1, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);
            AcceptButton = close;
            CancelButton = close;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _iconBitmap?.Dispose();
            base.Dispose(disposing);
        }

        private void OpenAuthorLink(object sender, LinkLabelLinkClickedEventArgs args)
        {
            try { Process.Start(new ProcessStartInfo(AuthorUrl) { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }
}
