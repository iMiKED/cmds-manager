using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmdsManager.Presentation.Forms
{
    internal sealed class DetachedConsoleForm : Form
    {
        private Control _content;
        private bool _closePermanently;
        private Rectangle _restoreBounds;
        private FormBorderStyle _restoreBorderStyle;
        private FormWindowState _restoreWindowState;

        internal DetachedConsoleForm(string title, Control content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            Text = title ?? string.Empty;
            Icon = ApplicationResources.Icon;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(480, 260);
            Size = new Size(900, 560);
            KeyPreview = true;
            _content = content;
            _content.Dock = DockStyle.Fill;
            Controls.Add(_content);
        }

        internal event EventHandler ReattachRequested;
        internal event EventHandler FullScreenChanged;

        internal bool IsFullScreen { get; private set; }

        internal Control ReleaseContent()
        {
            var result = _content;
            if (result != null)
            {
                Controls.Remove(result);
                result.Dock = DockStyle.Fill;
                _content = null;
            }
            return result;
        }

        internal void ClosePermanently()
        {
            if (IsDisposed) return;
            _closePermanently = true;
            ReleaseContent();
            Close();
        }

        internal void ToggleFullScreen()
        {
            SetFullScreen(!IsFullScreen);
        }

        internal void SetFullScreen(bool fullScreen)
        {
            if (IsFullScreen == fullScreen) return;
            if (fullScreen)
            {
                _restoreBounds = Bounds;
                _restoreBorderStyle = FormBorderStyle;
                _restoreWindowState = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
                IsFullScreen = true;
            }
            else
            {
                FormBorderStyle = _restoreBorderStyle;
                Bounds = _restoreBounds;
                WindowState = _restoreWindowState;
                IsFullScreen = false;
            }
            FullScreenChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.F11)
            {
                ToggleFullScreen();
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            if (args.KeyCode == Keys.Escape && IsFullScreen)
            {
                SetFullScreen(false);
                args.Handled = true;
                args.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(args);
        }

        protected override void OnFormClosing(FormClosingEventArgs args)
        {
            if (!_closePermanently && _content != null)
            {
                args.Cancel = true;
                ReattachRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnFormClosing(args);
        }
    }
}
