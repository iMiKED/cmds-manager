using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace CmdsManager.Presentation.Forms
{
    public sealed class AboutForm : Form
    {
        private const string RepositoryUrl = "";

        public AboutForm(string configPath)
        {
            Text = "О программе CmdsManager";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 330);
            Icon = SystemIcons.Application;

            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "-";
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;

            var title = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
                Text = "CmdsManager",
                Location = new Point(24, 22)
            };
            var description = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                Text = "Portable-менеджер CMD, BAT, PowerShell и VBS-скриптов для Windows.",
                Location = new Point(26, 62)
            };
            var details = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                Text = "Версия: " + informational + Environment.NewLine +
                       ".NET Framework runtime: " + Environment.Version + Environment.NewLine +
                       "Windows: " + Environment.OSVersion.VersionString + Environment.NewLine +
                       "Конфигурация: " + configPath + Environment.NewLine +
                       "Git: " + (RepositoryUrl.Length == 0 ? "локальный репозиторий, remote не настроен" : RepositoryUrl),
                Location = new Point(26, 108)
            };
            var repository = new LinkLabel
            {
                AutoSize = true,
                Text = "Открыть репозиторий",
                Location = new Point(26, 224),
                Enabled = RepositoryUrl.Length > 0
            };
            repository.LinkClicked += (sender, args) =>
            {
                if (RepositoryUrl.Length > 0)
                {
                    Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
                }
            };

            var copy = new Button
            {
                Text = "Копировать диагностику",
                AutoSize = true,
                Location = new Point(26, 268)
            };
            copy.Click += (sender, args) =>
            {
                var diagnostics = new StringBuilder()
                    .AppendLine("CmdsManager " + informational)
                    .AppendLine(".NET Framework runtime: " + Environment.Version)
                    .AppendLine("Windows: " + Environment.OSVersion.VersionString)
                    .AppendLine("Configuration: " + configPath)
                    .ToString();
                Clipboard.SetText(diagnostics);
            };

            var close = new Button
            {
                Text = "Закрыть",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(414, 268)
            };

            Controls.AddRange(new Control[] { title, description, details, repository, copy, close });
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
