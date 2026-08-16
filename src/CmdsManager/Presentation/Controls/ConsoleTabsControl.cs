using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Execution;

namespace CmdsManager.Presentation.Controls
{
    public sealed class ConsoleTabsControl : UserControl
    {
        private const int MaxCharactersPerTab = 200000;
        private const int TrimToCharacters = 150000;
        private const int MaxEventsPerTick = 50000;
        private static readonly Color ConsoleBackground = Color.FromArgb(28, 28, 28);
        private static readonly Color TabStripBackground = Color.FromArgb(236, 238, 241);
        private static readonly Color InactiveTabBackground = Color.FromArgb(248, 249, 251);
        private static readonly Color HoverTabBackground = Color.FromArgb(222, 229, 238);

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

        private sealed class ConsoleHistoryLine
        {
            internal byte[] RawBytes { get; set; }
            internal string OriginalText { get; set; }
            internal bool IsError { get; set; }
        }

        private sealed class ConsoleSession
        {
            internal Guid ScriptId { get; set; }
            internal string ScriptName { get; set; }
            internal int ProcessId { get; set; }
            internal int? ExitCode { get; set; }
            internal DateTime StartedAt { get; set; }
            internal ScriptOutputEncoding OutputEncoding { get; set; }
            internal Queue<ConsoleHistoryLine> History { get; } = new Queue<ConsoleHistoryLine>();
            internal int HistoryUnits { get; set; }
            internal bool WordWrap { get; set; }
            internal Font CustomFont { get; set; }
            internal TabPage Page { get; set; }
            internal RichTextBox Output { get; set; }
        }

        private sealed class TerminalTabControl : TabControl
        {
            public override Rectangle DisplayRectangle
            {
                get
                {
                    if (TabCount == 0) return base.DisplayRectangle;
                    var headerBottom = GetTabRect(0).Bottom;
                    return headerBottom <= 0
                        ? base.DisplayRectangle
                        : new Rectangle(0, headerBottom, ClientSize.Width,
                            Math.Max(0, ClientSize.Height - headerBottom));
                }
            }
        }

        private readonly LocalizationService _text;
        private readonly Func<ApplicationSettings> _settings;
        private readonly ConcurrentQueue<ConsoleEvent> _events = new ConcurrentQueue<ConsoleEvent>();
        private readonly Dictionary<int, ConsoleSession> _sessions = new Dictionary<int, ConsoleSession>();
        private readonly HashSet<int> _suppressedProcesses = new HashSet<int>();
        private readonly TabControl _tabs = new TerminalTabControl
        {
            Dock = DockStyle.Fill,
            ShowToolTips = true,
            BackColor = TabStripBackground
        };
        private readonly Label _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = ConsoleBackground,
            ForeColor = Color.Silver
        };
        private readonly Timer _flushTimer = new Timer { Interval = 50 };
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _copyItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _saveSelectionItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _saveAllItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _fontItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _encodingItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _wordWrapItem = new ToolStripMenuItem { CheckOnClick = false };
        private readonly ToolStripMenuItem _clearItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _closeItem = new ToolStripMenuItem();
        private readonly Dictionary<ScriptOutputEncoding, ToolStripMenuItem> _encodingItems =
            new Dictionary<ScriptOutputEncoding, ToolStripMenuItem>();
        private readonly Font _tabFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        private Font _consoleFont;
        private int _hotTabIndex = -1;
        private int _hotCloseIndex = -1;

        public ConsoleTabsControl(LocalizationService text, Func<ApplicationSettings> settings)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            BackColor = ConsoleBackground;
            AddEncodingItem(ScriptOutputEncoding.Auto);
            AddEncodingItem(ScriptOutputEncoding.Utf8);
            AddEncodingItem(ScriptOutputEncoding.Windows1251);
            AddEncodingItem(ScriptOutputEncoding.Oem);
            AddEncodingItem(ScriptOutputEncoding.Utf16LittleEndian);
            _menu.Items.AddRange(new ToolStripItem[]
            {
                _copyItem,
                _saveSelectionItem,
                _saveAllItem,
                new ToolStripSeparator(),
                _fontItem,
                _encodingItem,
                _wordWrapItem,
                new ToolStripSeparator(),
                _clearItem,
                _closeItem
            });
            _menu.Opening += PrepareContextMenu;
            _copyItem.Click += (sender, args) => CopySelection();
            _saveSelectionItem.Click += (sender, args) => SaveConsoleText(true);
            _saveAllItem.Click += (sender, args) => SaveConsoleText(false);
            _fontItem.Click += (sender, args) => ChooseFont();
            _wordWrapItem.Click += (sender, args) => ToggleWordWrap();
            _clearItem.Click += (sender, args) => ClearSelectedTab();
            _closeItem.Click += (sender, args) => CloseSelectedTab();

            _tabs.ContextMenuStrip = _menu;
            _tabs.Appearance = TabAppearance.FlatButtons;
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.Padding = new Point(20, 6);
            _tabs.ItemSize = new Size(0, 36);
            _tabs.Font = _tabFont;
            _tabs.DrawItem += DrawTab;
            _tabs.MouseDown += HandleTabMouseDown;
            _tabs.MouseMove += HandleTabMouseMove;
            _tabs.MouseLeave += (sender, args) => SetHotTab(-1, -1);

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
                _events.Enqueue(new ConsoleEvent { Kind = ConsoleEventKind.Started, Instance = args });
        }

        public void EnqueueOutput(ScriptOutputEventArgs args)
        {
            if (args != null) _events.Enqueue(new ConsoleEvent { Kind = ConsoleEventKind.Output, Output = args });
        }

        public void EnqueueExited(ScriptInstanceEventArgs args)
        {
            if (args != null && args.CapturesOutput)
                _events.Enqueue(new ConsoleEvent { Kind = ConsoleEventKind.Exited, Instance = args });
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
                replacement = new Font(settings.ConsoleFontName, settings.ConsoleFontSize,
                    FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                replacement = new Font(FontFamily.GenericMonospace, 10f, FontStyle.Regular, GraphicsUnit.Point);
            }

            foreach (var session in _sessions.Values.Where(item => item.CustomFont == null))
                session.Output.Font = replacement;

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
                foreach (var session in _sessions.Values) session.CustomFont?.Dispose();
                _menu.Dispose();
                _consoleFont?.Dispose();
                _tabFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private void FlushPendingOutput()
        {
            if (IsDisposed) return;

            var batches = new Dictionary<int, StringBuilder>();
            var redraw = new HashSet<int>();
            var processed = 0;
            ConsoleEvent item;
            while (processed < MaxEventsPerTick && _events.TryDequeue(out item))
            {
                processed++;
                if (item.Kind == ConsoleEventKind.Started)
                {
                    _suppressedProcesses.Remove(item.Instance.ProcessId);
                    var started = EnsureSession(item.Instance.ProcessId, item.Instance.ScriptId,
                        item.Instance.ScriptName, item.Instance.StartedAt, item.Instance.OutputEncoding);
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

                if (_suppressedProcesses.Contains(item.Output.ProcessId)) continue;
                var session = EnsureSession(item.Output.ProcessId, item.Output.ScriptId,
                    string.Empty, null, ScriptOutputEncoding.Auto);
                var historyLine = new ConsoleHistoryLine
                {
                    RawBytes = item.Output.RawBytes,
                    OriginalText = item.Output.Line,
                    IsError = item.Output.IsError
                };
                session.History.Enqueue(historyLine);
                session.HistoryUnits += HistoryUnits(historyLine);
                if (TrimHistory(session)) redraw.Add(session.ProcessId);

                if (redraw.Contains(session.ProcessId)) continue;
                StringBuilder builder;
                if (!batches.TryGetValue(item.Output.ProcessId, out builder))
                {
                    builder = new StringBuilder();
                    batches[item.Output.ProcessId] = builder;
                }
                builder.AppendLine(DecodeLine(session, historyLine));
            }

            foreach (var processId in redraw)
            {
                ConsoleSession session;
                if (_sessions.TryGetValue(processId, out session)) RenderSession(session);
            }
            foreach (var batch in batches)
            {
                if (redraw.Contains(batch.Key)) continue;
                ConsoleSession session;
                if (_sessions.TryGetValue(batch.Key, out session)) AppendBatch(session.Output, batch.Value.ToString());
            }

            UpdateEmptyState();
        }

        private ConsoleSession EnsureSession(int processId, Guid scriptId, string scriptName,
            DateTime? startedAt, ScriptOutputEncoding outputEncoding)
        {
            ConsoleSession existing;
            if (_sessions.TryGetValue(processId, out existing))
            {
                if (!string.IsNullOrWhiteSpace(scriptName))
                {
                    existing.ScriptName = scriptName;
                    existing.OutputEncoding = outputEncoding;
                    if (startedAt.HasValue) existing.StartedAt = startedAt.Value;
                    UpdateTabTitle(existing);
                    if (existing.History.Count > 0) RenderSession(existing);
                }
                return existing;
            }

            var output = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                BackColor = ConsoleBackground,
                ForeColor = Color.Gainsboro,
                BorderStyle = BorderStyle.None,
                DetectUrls = true,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                Font = _consoleFont ?? new Font(FontFamily.GenericMonospace, 10f),
                ContextMenuStrip = _menu
            };
            var page = new TabPage { BackColor = output.BackColor, Padding = Padding.Empty };
            page.Controls.Add(output);
            var session = new ConsoleSession
            {
                ScriptId = scriptId,
                ScriptName = string.IsNullOrWhiteSpace(scriptName) ? "PID " + processId : scriptName,
                ProcessId = processId,
                StartedAt = startedAt ?? DateTime.Now,
                OutputEncoding = outputEncoding,
                Page = page,
                Output = output
            };
            _sessions[processId] = session;
            UpdateTabTitle(session);
            _tabs.TabPages.Add(page);
            return session;
        }

        private static int HistoryUnits(ConsoleHistoryLine line)
        {
            return (line.RawBytes == null ? (line.OriginalText ?? string.Empty).Length : line.RawBytes.Length) + 2;
        }

        private static bool TrimHistory(ConsoleSession session)
        {
            if (session.HistoryUnits <= MaxCharactersPerTab) return false;
            while (session.History.Count > 1 && session.HistoryUnits > TrimToCharacters)
            {
                var removed = session.History.Dequeue();
                session.HistoryUnits -= HistoryUnits(removed);
            }
            return true;
        }

        private static string DecodeLine(ConsoleSession session, ConsoleHistoryLine line)
        {
            return line.RawBytes == null
                ? line.OriginalText ?? string.Empty
                : OutputEncodingDecoder.Decode(line.RawBytes, session.OutputEncoding);
        }

        private static void RenderSession(ConsoleSession session)
        {
            var builder = new StringBuilder(Math.Min(MaxCharactersPerTab, session.HistoryUnits));
            foreach (var line in session.History) builder.AppendLine(DecodeLine(session, line));
            var text = builder.ToString();
            if (text.Length > MaxCharactersPerTab)
                text = text.Substring(text.Length - TrimToCharacters);
            session.Output.Text = text;
            session.Output.SelectionStart = session.Output.TextLength;
            session.Output.SelectionLength = 0;
            session.Output.ScrollToCaret();
        }

        private static void AppendBatch(RichTextBox output, string text)
        {
            if (text.Length == 0) return;
            var wasAtEnd = output.SelectionStart >= output.TextLength - 1;
            output.AppendText(text);
            if (wasAtEnd)
            {
                output.SelectionStart = output.TextLength;
                output.SelectionLength = 0;
                output.ScrollToCaret();
            }
        }

        private void AddEncodingItem(ScriptOutputEncoding encoding)
        {
            var item = new ToolStripMenuItem { Tag = encoding, CheckOnClick = false };
            item.Click += ChooseEncoding;
            _encodingItems.Add(encoding, item);
            _encodingItem.DropDownItems.Add(item);
        }

        private void PrepareContextMenu(object sender, CancelEventArgs args)
        {
            var session = SelectedSession;
            if (session == null)
            {
                args.Cancel = true;
                return;
            }

            _copyItem.Enabled = session.Output.SelectionLength > 0;
            _saveSelectionItem.Enabled = session.Output.SelectionLength > 0;
            _saveAllItem.Enabled = session.Output.TextLength > 0;
            _clearItem.Enabled = session.Output.TextLength > 0;
            _wordWrapItem.Checked = session.WordWrap;
            foreach (var pair in _encodingItems) pair.Value.Checked = pair.Key == session.OutputEncoding;
            _closeItem.Text = session.ExitCode.HasValue ? _text["Console.CloseTab"] : _text["Console.CloseAndStop"];
        }

        private void CopySelection()
        {
            var session = SelectedSession;
            if (session == null || session.Output.SelectionLength == 0) return;
            try { session.Output.Copy(); }
            catch (ExternalException exception)
            {
                MessageBox.Show(this, exception.Message, _text["Console.CopyFailed"],
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveConsoleText(bool selectionOnly)
        {
            var session = SelectedSession;
            if (session == null) return;
            var content = selectionOnly ? session.Output.SelectedText : session.Output.Text;
            if (string.IsNullOrEmpty(content)) return;

            using (var dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = "txt",
                Filter = _text["Console.TextFileFilter"],
                FileName = SafeFileName(session.ScriptName) +
                    (selectionOnly ? "-selection-" : "-console-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt",
                OverwritePrompt = true,
                RestoreDirectory = true,
                Title = _text[selectionOnly ? "Console.SaveSelectionTitle" : "Console.SaveAllTitle"]
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true)); }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, _text["Console.SaveFailed"],
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string SafeFileName(string value)
        {
            var result = string.IsNullOrWhiteSpace(value) ? "console" : value.Trim();
            foreach (var character in Path.GetInvalidFileNameChars()) result = result.Replace(character, '_');
            return result.Length > 60 ? result.Substring(0, 60) : result;
        }

        private void ChooseFont()
        {
            var session = SelectedSession;
            if (session == null) return;
            using (var dialog = new FontDialog
            {
                Font = session.Output.Font,
                FixedPitchOnly = false,
                ShowEffects = true,
                MinSize = 6,
                MaxSize = 48
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var replacement = (Font)dialog.Font.Clone();
                var previous = session.CustomFont;
                session.CustomFont = replacement;
                session.Output.Font = replacement;
                previous?.Dispose();
            }
        }

        private void ChooseEncoding(object sender, EventArgs args)
        {
            var session = SelectedSession;
            var item = sender as ToolStripMenuItem;
            if (session == null || item == null || !(item.Tag is ScriptOutputEncoding)) return;
            session.OutputEncoding = (ScriptOutputEncoding)item.Tag;
            RenderSession(session);
        }

        private void ToggleWordWrap()
        {
            var session = SelectedSession;
            if (session == null) return;
            session.WordWrap = !session.WordWrap;
            session.Output.WordWrap = session.WordWrap;
            session.Output.ScrollBars = session.WordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
        }

        private void ClearSelectedTab()
        {
            var session = SelectedSession;
            if (session == null) return;
            session.History.Clear();
            session.HistoryUnits = 0;
            session.Output.Clear();
        }

        private void CloseSelectedTab()
        {
            var session = SelectedSession;
            if (session != null) CloseSession(session);
        }

        private void CloseSession(ConsoleSession session)
        {
            var isRunning = !session.ExitCode.HasValue;
            _suppressedProcesses.Add(session.ProcessId);
            _sessions.Remove(session.ProcessId);
            _tabs.TabPages.Remove(session.Page);
            session.Page.Dispose();
            session.CustomFont?.Dispose();
            UpdateEmptyState();
            CloseRequested?.Invoke(this,
                new ConsoleTabCloseRequestedEventArgs(session.ScriptId, session.ProcessId, isRunning));
        }

        private void DrawTab(object sender, DrawItemEventArgs args)
        {
            if (args.Index < 0 || args.Index >= _tabs.TabPages.Count) return;
            var tabBounds = _tabs.GetTabRect(args.Index);
            if (args.Index == 0)
            {
                using (var strip = new SolidBrush(TabStripBackground))
                    args.Graphics.FillRectangle(strip, 0, 0, _tabs.ClientSize.Width, tabBounds.Bottom + 2);
            }

            var bounds = Rectangle.FromLTRB(tabBounds.Left + 1, tabBounds.Top + 2,
                tabBounds.Right - 1, tabBounds.Bottom);
            var selected = args.Index == _tabs.SelectedIndex;
            var hot = args.Index == _hotTabIndex;
            var background = selected
                ? ConsoleBackground
                : hot ? HoverTabBackground : InactiveTabBackground;
            var border = hot ? Color.FromArgb(177, 190, 207) : Color.FromArgb(210, 215, 222);

            var previousSmoothing = args.Graphics.SmoothingMode;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = TerminalTabPath(bounds, selected ? 10 : 8, selected ? 7 : 5))
            using (var brush = new SolidBrush(background))
            {
                args.Graphics.FillPath(brush, path);
                if (!selected && hot)
                {
                    using (var pen = new Pen(border)) args.Graphics.DrawPath(pen, path);
                }
            }
            if (!selected && !hot)
            {
                using (var separator = new Pen(Color.FromArgb(205, 209, 215)))
                    args.Graphics.DrawLine(separator, bounds.Right - 2, bounds.Top + 8,
                        bounds.Right - 2, bounds.Bottom - 7);
            }

            var pageText = _tabs.TabPages[args.Index].Text ?? string.Empty;
            var running = pageText.StartsWith("● ", StringComparison.Ordinal);
            var displayText = pageText.Length > 2 && (running || pageText.StartsWith("○ ", StringComparison.Ordinal))
                ? pageText.Substring(2)
                : pageText;
            using (var status = new SolidBrush(running
                ? Color.FromArgb(42, 190, 107)
                : selected ? Color.FromArgb(180, 188, 198) : Color.FromArgb(125, 135, 145)))
                args.Graphics.FillEllipse(status, bounds.Left + 12, bounds.Top + (bounds.Height - 8) / 2, 8, 8);

            var closeBounds = CloseBounds(tabBounds);
            if (args.Index == _hotCloseIndex)
            {
                using (var closeBackground = new SolidBrush(selected
                    ? Color.FromArgb(64, 255, 255, 255)
                    : Color.FromArgb(255, 225, 225)))
                    args.Graphics.FillEllipse(closeBackground, closeBounds);
            }
            var closeColor = args.Index == _hotCloseIndex
                ? selected ? Color.White : Color.Firebrick
                : selected ? Color.FromArgb(225, 230, 236) : Color.FromArgb(105, 110, 118);
            using (var closePen = new Pen(closeColor, 1.5f))
            {
                args.Graphics.DrawLine(closePen, closeBounds.Left + 5, closeBounds.Top + 5,
                    closeBounds.Right - 5, closeBounds.Bottom - 5);
                args.Graphics.DrawLine(closePen, closeBounds.Right - 5, closeBounds.Top + 5,
                    closeBounds.Left + 5, closeBounds.Bottom - 5);
            }
            args.Graphics.SmoothingMode = previousSmoothing;

            var textBounds = new Rectangle(bounds.Left + 27, bounds.Top + 1,
                Math.Max(0, closeBounds.Left - bounds.Left - 30), bounds.Height - 2);
            TextRenderer.DrawText(args.Graphics, displayText, _tabFont, textBounds,
                selected ? Color.FromArgb(245, 247, 250) : Color.FromArgb(42, 48, 56),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void HandleTabMouseDown(object sender, MouseEventArgs args)
        {
            var index = TabIndexAt(args.Location);
            if (index < 0) return;
            _tabs.SelectedIndex = index;
            if (args.Button == MouseButtons.Left && CloseBounds(_tabs.GetTabRect(index)).Contains(args.Location))
            {
                var session = SelectedSession;
                if (session != null) CloseSession(session);
            }
        }

        private void HandleTabMouseMove(object sender, MouseEventArgs args)
        {
            var index = TabIndexAt(args.Location);
            var closeIndex = index >= 0 && CloseBounds(_tabs.GetTabRect(index)).Contains(args.Location) ? index : -1;
            SetHotTab(index, closeIndex);
        }

        private int TabIndexAt(Point location)
        {
            for (var index = 0; index < _tabs.TabPages.Count; index++)
                if (_tabs.GetTabRect(index).Contains(location)) return index;
            return -1;
        }

        private void SetHotTab(int tabIndex, int closeIndex)
        {
            if (_hotTabIndex == tabIndex && _hotCloseIndex == closeIndex) return;
            _hotTabIndex = tabIndex;
            _hotCloseIndex = closeIndex;
            _tabs.Invalidate();
        }

        private static Rectangle CloseBounds(Rectangle tabBounds)
        {
            return new Rectangle(tabBounds.Right - 22, tabBounds.Top + Math.Max(2, (tabBounds.Height - 18) / 2), 18, 18);
        }

        private static GraphicsPath TerminalTabPath(Rectangle bounds, int radius, int shoulder)
        {
            var left = bounds.Left;
            var top = bounds.Top;
            var right = bounds.Right;
            var bottom = bounds.Bottom;
            var bodyLeft = left + shoulder;
            var bodyRight = right - shoulder;
            var path = new GraphicsPath();
            path.StartFigure();
            path.AddLine(bodyLeft + radius, top, bodyRight - radius, top);
            path.AddBezier(bodyRight - radius, top, bodyRight - 4, top,
                bodyRight, top + 4, bodyRight, top + radius);
            path.AddLine(bodyRight, top + radius, bodyRight, bottom - shoulder);
            path.AddBezier(bodyRight, bottom - shoulder, bodyRight, bottom - 2,
                right - 3, bottom, right, bottom);
            path.AddLine(right, bottom, left, bottom);
            path.AddBezier(left, bottom, left + 3, bottom,
                bodyLeft, bottom - 2, bodyLeft, bottom - shoulder);
            path.AddLine(bodyLeft, bottom - shoulder, bodyLeft, top + radius);
            path.AddBezier(bodyLeft, top + radius, bodyLeft, top + 4,
                bodyLeft + 4, top, bodyLeft + radius, top);
            path.CloseFigure();
            return path;
        }

        private ConsoleSession SelectedSession
        {
            get
            {
                var page = _tabs.SelectedTab;
                return page == null ? null : _sessions.Values.FirstOrDefault(item => item.Page == page);
            }
        }

        private void HandleLocalizationChanged(object sender, EventArgs args)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke((Action)ApplyLocalization);
            else ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            _empty.Text = _text["Console.Empty"];
            _copyItem.Text = _text["Console.CopySelection"];
            _saveSelectionItem.Text = _text["Console.SaveSelection"];
            _saveAllItem.Text = _text["Console.SaveAll"];
            _fontItem.Text = _text["Console.SelectFont"];
            _encodingItem.Text = _text["Console.Encoding"];
            _wordWrapItem.Text = _text["Console.WordWrap"];
            _clearItem.Text = _text["Console.Clear"];
            _closeItem.Text = _text["Console.CloseTab"];
            _encodingItems[ScriptOutputEncoding.Auto].Text = _text["Script.Encoding.Auto"];
            _encodingItems[ScriptOutputEncoding.Utf8].Text = _text["Script.Encoding.Utf8"];
            _encodingItems[ScriptOutputEncoding.Windows1251].Text = _text["Script.Encoding.Windows1251"];
            _encodingItems[ScriptOutputEncoding.Oem].Text = _text["Script.Encoding.Oem"];
            _encodingItems[ScriptOutputEncoding.Utf16LittleEndian].Text = _text["Script.Encoding.Utf16"];
            foreach (var session in _sessions.Values) UpdateTabTitle(session);
        }

        private void UpdateTabTitle(ConsoleSession session)
        {
            var name = session.ScriptName ?? string.Empty;
            if (name.Length > 28) name = name.Substring(0, 27) + "…";
            var status = session.ExitCode.HasValue
                ? _text.Get("Console.Exited", session.ExitCode.Value)
                : _text["Console.Running"];
            session.Page.Text = (session.ExitCode.HasValue ? "○ " : "● ") +
                name + " [" + session.ProcessId + "] · " + status;
            session.Page.ToolTipText = (session.ScriptName ?? string.Empty) +
                " [" + session.ProcessId + "] · " + status;
            _tabs.Invalidate();
        }

        private void UpdateEmptyState()
        {
            _empty.Visible = _tabs.TabPages.Count == 0;
            if (_empty.Visible) _empty.BringToFront();
        }
    }
}
