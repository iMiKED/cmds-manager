using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CmdsManager.Application;

namespace CmdsManager.Presentation.Forms
{
    public sealed class AboutForm : Form
    {
        public AboutForm(LocalizationService text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            Text = text["About.Title"];
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(390, 165);
            Icon = SystemIcons.Application;

            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "-";
            var title = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), Text = "CmdsManager" };
            var description = new Label { AutoSize = true, MaximumSize = new Size(350, 0), Text = text["About.Description"] };
            var versionLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = text.Get("About.Version", version) };
            var close = new Button { Text = text["Common.Close"], DialogResult = DialogResult.OK, AutoSize = true };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            buttons.Controls.Add(close);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            title.Margin = new Padding(0, 0, 0, 6);
            description.Margin = new Padding(1, 0, 0, 5);
            versionLabel.Margin = new Padding(1, 0, 0, 0);
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(description, 0, 1);
            layout.Controls.Add(versionLabel, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
