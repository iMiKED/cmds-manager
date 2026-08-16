using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;

namespace CmdsManager.Presentation.Controls
{
    public sealed class ConsoleTabsControl : UserControl
    {
        private const int MaxCharactersPerTab = 200000;
        private const int TrimToCharacters = 150000;
        private const int MaxEventsPerTick = 50000;

        private enum ConsoleEventKind
        {
            Started,
            Output,
            Exited
        }

        private sealed class ConsoleEvent
        {
            internal ConsoleEventKind Kind { get; set; }
            internal ScriptInstanceEventArgs Instance { get; set; }
            internal ScriptOutputEventArgs Output { get; set; }
        }

        private sealed class ConsoleSession
        {
            internal Guid ScriptId { get; set; }
            internal string ScriptName { get; set; }
            internal int ProcessId { get; set; }
            internal int? ExitCode { get; set; }
            internal DateTime StartedAt { get; set; }
            internal TabPage Page { get; set; }
            internal RichTextBox Output { get; set; }
        }

        private readonly LocalizationService _text;
        private readonly Func<ApplicationSettings> _settings;
        private readonly ConcurrentQueue<ConsoleEvent> _events = new ConcurrentQueue<ConsoleEvent>();
        private readonly Dictionary<int, ConsoleSession> _sessions = new Dictionary<int, ConsoleSession>();
        private readonly HashSet<int> _suppressedProcesses = new HashSet<int>();
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill, ShowToolTips = true };
        private readonly Label _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(28, 28, 28),
            ForeColor = Color.Silver
        };
        private readonly Timer _flushTimer = new Timer { Interval = 50 };
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _clearItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _closeItem = new ToolStripMenuItem();
        private Font _consoleFont;

        public ConsoleTabsControl(LocalizationService text, Func<ApplicationSettings> settings)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            BackColor = Color.FromArgb(28, 28, 28);
            _menu.Items.AddRange(new ToolStripItem[] { _clearItem, _closeItem });
            _menu.Opening += (sender, args) =>
            {
                var session = SelectedSession;
                _clearItem.Enabled = session != null && session.Output.TextLength > 0;
                _closeItem.Enabled = session != null;
                _closeItem.Text = session != null && !session.ExitCode.HasValue
                    ? _text["Console.CloseAndStop"]
                    : _text["Console.CloseTab"];
            };
            _clearItem.Click += (sender, args) => SelectedSession?.Output.Clear();
            _closeItem.Click += (sender, args) => CloseSelectedTab();
            _tabs.ContextMenuStrip = _menu;
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.Padding = new Point(15, 4);
            _tabs.DrawItem += DrawTab;
            _tabs.MouseDown += HandleTabMouseDown;

            Controls.Add(_tabs);
            Controls.Add(_empty);
            _empty.BringToFront();

            _flushTimer.Tick += (sender, args) => FlushPendingOutput();
            _flushTimer.Start();
            _text.Changed += HandleLocalizationChanged;
            ApplySettings();
            ApplyLocalization();
            UpdateEmptyState();
        }

        public event EventHandler<ConsoleTabCloseRequestedEventArgs> CloseRequested;

        public void EnqueueStarted(ScriptInstanceEventArgs args)
        {
            if (args != null && args.CapturesOutput)
            {
                _events.Enqueue(new ConsoleEvent { Kind = ConsoleEventKind.Started, Instance = args });
            }
        }

        public void EnqueueOutput(ScriptOutputEventArgs args)
        {
            if (args != null)
            {
                _events.Enqueue(new ConsoleEvent { Kind = ConsoleEventKind.Output, Output = args });
            }
        }

        public void EnqueueExited(ScriptInstanceEventArgs args)
        {
            if (args != null && args.CapturesOutput)
            {
                _events.Enqueue(new ConsoleEvent { Kind = ConsoleEventKind.Exited, Instance = args });
            }
        }

        public void ApplySettings()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)ApplySettings);
                return;
            }

            var settings = _settings() ?? new ApplicationSettings();
            Font replacement;
            try
            {
                replacement = new Font(settings.ConsoleFontName, settings.ConsoleFontSize, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                replacement = new Font(FontFamily.GenericMonospace, 10f, FontStyle.Regular, GraphicsUnit.Point);
            }

            foreach (var session in _sessions.Values)
            {
                session.Output.Font = replacement;
            }

            var previous = _consoleFont;
            _consoleFont = replacement;
            previous?.Dispose();
        }

        public void SelectScript(Guid scriptId)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SelectScript(scriptId)));
                return;
            }

            FlushPendingOutput();
            var session = _sessions.Values.Where(item => item.ScriptId == scriptId)
                .OrderBy(item => item.ExitCode.HasValue)
                .ThenByDescending(item => item.StartedAt)
                .FirstOrDefault();
            if (session != null) _tabs.SelectedTab = session.Page;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _text.Changed -= HandleLocalizationChanged;
                _flushTimer.Stop();
                _flushTimer.Dispose();
                _menu.Dispose();
                _consoleFont?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void FlushPendingOutput()
        {
            if (IsDisposed)
            {
                return;
            }

            var batches = new Dictionary<int, StringBuilder>();
            var processed = 0;
            ConsoleEvent item;
            while (processed < MaxEventsPerTick && _events.TryDequeue(out item))
            {
                processed++;
                if (item.Kind == ConsoleEventKind.Started)
                {
                    _suppressedProcesses.Remove(item.Instance.ProcessId);
                    var started = EnsureSession(item.Instance.ProcessId, item.Instance.ScriptId, item.Instance.ScriptName, item.Instance.StartedAt);
                    _tabs.SelectedTab = started.Page;
                    continue;
                }

                if (item.Kind == ConsoleEventKind.Exited)
                {
                    ConsoleSession exited;
                    if (_sessions.TryGetValue(item.Instance.ProcessId, out exited))
                    {
                        exited.ExitCode = item.Instance.ExitCode;
                        UpdateTabTitle(exited);
                    }

                    continue;
                }

                if (_suppressedProcesses.Contains(item.Output.ProcessId))
                {
                    continue;
                }

                var session = EnsureSession(item.Output.ProcessId, item.Output.ScriptId, string.Empty, null);
                StringBuilder builder;
                if (!batches.TryGetValue(item.Output.ProcessId, out builder))
                {
                    builder = new StringBuilder();
                    batches[item.Output.ProcessId] = builder;
                }

                builder.AppendLine(item.Output.Line);
            }

            foreach (var batch in batches)
            {
                ConsoleSession session;
                if (!_sessions.TryGetValue(batch.Key, out session))
                {
                    continue;
                }

                AppendBatch(session.Output, batch.Value.ToString());
            }

            UpdateEmptyState();
        }

        private ConsoleSession EnsureSession(int processId, Guid scriptId, string scriptName, DateTime? startedAt)
        {
            ConsoleSession existing;
            if (_sessions.TryGetValue(processId, out existing))
            {
                if (!string.IsNullOrWhiteSpace(scriptName))
                {
                    existing.ScriptName = scriptName;
                    if (startedAt.HasValue) existing.StartedAt = startedAt.Value;
                    UpdateTabTitle(existing);
                }

                return existing;
            }

            var output = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None,
                DetectUrls = true,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                Font = _consoleFont ?? new Font(FontFamily.GenericMonospace, 10f)
            };
            var page = new TabPage { BackColor = output.BackColor, Padding = new Padding(2) };
            page.Controls.Add(output);
            var session = new ConsoleSession
            {
                ScriptId = scriptId,
                ScriptName = string.IsNullOrWhiteSpace(scriptName) ? "PID " + processId : scriptName,
                ProcessId = processId,
                StartedAt = startedAt ?? DateTime.Now,
                Page = page,
                Output = output
            };
            _sessions[processId] = session;
            UpdateTabTitle(session);
            _tabs.TabPages.Add(page);
            return session;
        }

        private static void AppendBatch(RichTextBox output, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            var wasAtEnd = output.SelectionStart >= output.TextLength - 1;
            output.AppendText(text);
            if (output.TextLength > MaxCharactersPerTab)
            {
                var remove = output.TextLength - TrimToCharacters;
                output.ReadOnly = false;
                output.Select(0, remove);
                output.SelectedText = string.Empty;
                output.ReadOnly = true;
                wasAtEnd = true;
            }

            if (wasAtEnd)
            {
                output.SelectionStart = output.TextLength;
                output.SelectionLength = 0;
                output.ScrollToCaret();
            }
        }

        private void CloseSelectedTab()
        {
            var session = SelectedSession;
            if (session == null)
            {
                return;
            }

            CloseSession(session);
        }

        private void CloseSession(ConsoleSession session)
        {
            var isRunning = !session.ExitCode.HasValue;
            _suppressedProcesses.Add(session.ProcessId);
            _sessions.Remove(session.ProcessId);
            _tabs.TabPages.Remove(session.Page);
            session.Page.Dispose();
            UpdateEmptyState();
            CloseRequested?.Invoke(this, new ConsoleTabCloseRequestedEventArgs(session.ScriptId, session.ProcessId, isRunning));
        }

        private void DrawTab(object sender, DrawItemEventArgs args)
        {
            if (args.Index < 0 || args.Index >= _tabs.TabPages.Count) return;
            var bounds = _tabs.GetTabRect(args.Index);
            var selected = args.Index == _tabs.SelectedIndex;
            using (var background = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control))
                args.Graphics.FillRectangle(background, bounds);

            var closeBounds = CloseBounds(bounds);
            var textBounds = new Rectangle(bounds.Left + 7, bounds.Top + 2,
                Math.Max(0, closeBounds.Left - bounds.Left - 9), bounds.Height - 4);
            TextRenderer.DrawText(args.Graphics, _tabs.TabPages[args.Index].Text, _tabs.Font, textBounds,
                SystemColors.ControlText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(args.Graphics, "×", _tabs.Font, closeBounds, Color.Firebrick,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (selected) ControlPaint.DrawBorder(args.Graphics, bounds, SystemColors.ControlDark, ButtonBorderStyle.Solid);
        }

        private void HandleTabMouseDown(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            for (var index = 0; index < _tabs.TabPages.Count; index++)
            {
                if (!CloseBounds(_tabs.GetTabRect(index)).Contains(args.Location)) continue;
                _tabs.SelectedIndex = index;
                var session = SelectedSession;
                if (session != null) CloseSession(session);
                return;
            }
        }

        private static Rectangle CloseBounds(Rectangle tabBounds)
        {
            return new Rectangle(tabBounds.Right - 19, tabBounds.Top + Math.Max(1, (tabBounds.Height - 16) / 2), 16, 16);
        }

        private ConsoleSession SelectedSession
        {
            get
            {
                var page = _tabs.SelectedTab;
                if (page == null)
                {
                    return null;
                }

                foreach (var session in _sessions.Values)
                {
                    if (session.Page == page)
                    {
                        return session;
                    }
                }

                return null;
            }
        }

        private void HandleLocalizationChanged(object sender, EventArgs args)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)ApplyLocalization);
            }
            else
            {
                ApplyLocalization();
            }
        }

        private void ApplyLocalization()
        {
            _empty.Text = _text["Console.Empty"];
            _clearItem.Text = _text["Console.Clear"];
            _closeItem.Text = _text["Console.CloseTab"];
            foreach (var session in _sessions.Values)
            {
                UpdateTabTitle(session);
            }
        }

        private void UpdateTabTitle(ConsoleSession session)
        {
            var name = session.ScriptName ?? string.Empty;
            if (name.Length > 28)
            {
                name = name.Substring(0, 27) + "…";
            }

            var status = session.ExitCode.HasValue
                ? _text.Get("Console.Exited", session.ExitCode.Value)
                : _text["Console.Running"];
            session.Page.Text = (session.ExitCode.HasValue ? "○ " : "● ") +
                name + " [" + session.ProcessId + "] · " + status;
            session.Page.ToolTipText = (session.ScriptName ?? string.Empty) +
                " [" + session.ProcessId + "] · " + status;
        }

        private void UpdateEmptyState()
        {
            _empty.Visible = _tabs.TabPages.Count == 0;
            if (_empty.Visible)
            {
                _empty.BringToFront();
            }
        }
    }
}
