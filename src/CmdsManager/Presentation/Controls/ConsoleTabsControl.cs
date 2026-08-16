using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CmdsManager.Application;
using CmdsManager.Domain;
using CmdsManager.Infrastructure.Execution;
using CmdsManager.Presentation.Forms;
using CmdsManager.Presentation.Theming;

namespace CmdsManager.Presentation.Controls
{
    public sealed class ConsoleWordWrapChangedEventArgs : EventArgs
    {
        public ConsoleWordWrapChangedEventArgs(Guid scriptId, bool wordWrap)
        {
            ScriptId = scriptId;
            WordWrap = wordWrap;
        }

        public Guid ScriptId { get; }
        public bool WordWrap { get; }
    }

    public sealed class ConsoleTabsControl : UserControl
    {
        private const int MaxCharactersPerTab = 200000;
        private const int TrimToCharacters = 150000;
        private const int MaxEventsPerTick = 50000;
        private static readonly Color DefaultConsoleBackground = Color.FromArgb(28, 28, 28);

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
            internal RichTextBox Output { get; set; }
            internal DetachedConsoleForm DetachedWindow { get; set; }
            internal bool ReturnToMainAfterFullScreen { get; set; }
        }

        private readonly LocalizationService _text;
        private readonly Func<ApplicationSettings> _settings;
        private readonly Func<Guid, bool> _wordWrapForScript;
        private readonly ConcurrentQueue<ConsoleEvent> _events = new ConcurrentQueue<ConsoleEvent>();
        private readonly Dictionary<int, ConsoleSession> _sessions = new Dictionary<int, ConsoleSession>();
        private readonly HashSet<int> _suppressedProcesses = new HashSet<int>();
        private readonly TerminalTabStrip _tabStrip = new TerminalTabStrip
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ActiveTabColor = DefaultConsoleBackground
        };
        private readonly Panel _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = DefaultConsoleBackground
        };
        private readonly TableLayoutPanel _tabLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = DefaultConsoleBackground,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        private readonly Label _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = DefaultConsoleBackground,
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
        private readonly ToolStripMenuItem _detachItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _fullScreenItem = new ToolStripMenuItem { ShortcutKeyDisplayString = "F11" };
        private readonly ToolStripMenuItem _maximizePaneItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _clearItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _closeItem = new ToolStripMenuItem();
        private readonly Dictionary<ScriptOutputEncoding, ToolStripMenuItem> _encodingItems =
            new Dictionary<ScriptOutputEncoding, ToolStripMenuItem>();
        private Font _consoleFont;
        private ConsoleSession _contextSession;
        private Color _consoleForeground = Color.Gainsboro;
        private Color _consoleBackground = DefaultConsoleBackground;
        private ApplicationTheme _applicationTheme = ApplicationTheme.System;

        public ConsoleTabsControl(LocalizationService text, Func<ApplicationSettings> settings,
            Func<Guid, bool> wordWrapForScript = null)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _wordWrapForScript = wordWrapForScript ?? (scriptId => false);

            Tag = AppThemeManager.PreserveColorsTag;
            BackColor = DefaultConsoleBackground;
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
                _detachItem,
                _fullScreenItem,
                _maximizePaneItem,
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
            _detachItem.Click += (sender, args) => ToggleDetached();
            _fullScreenItem.Click += (sender, args) => ToggleFullScreen();
            _maximizePaneItem.Click += (sender, args) => PaneMaximizeRequested?.Invoke(this, EventArgs.Empty);
            _clearItem.Click += (sender, args) => ClearSelectedTab();
            _closeItem.Click += (sender, args) => CloseSelectedTab();

            _tabStrip.ContextMenuStrip = _menu;
            _tabStrip.SelectedTabChanged += HandleSelectedTabChanged;
            _tabStrip.CloseRequested += HandleTabCloseRequested;
            _tabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _tabLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            _tabLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _tabLayout.Controls.Add(_tabStrip, 0, 0);
            _tabLayout.Controls.Add(_contentHost, 0, 1);

            Controls.Add(_tabLayout);
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
        public event EventHandler PaneMaximizeRequested;
        public event EventHandler<ConsoleWordWrapChangedEventArgs> WordWrapChanged;

        internal void ApplyWordWrap(IEnumerable<Guid> scriptIds, bool wordWrap)
        {
            var identifiers = new HashSet<Guid>(scriptIds ?? Enumerable.Empty<Guid>());
            foreach (var session in _sessions.Values.Where(item => identifiers.Contains(item.ScriptId)))
                SetWordWrap(session, wordWrap);
        }

        internal void ApplyApplicationTheme(ApplicationTheme theme)
        {
            _applicationTheme = theme;
            var palette = AppThemeManager.Resolve(theme);
            _tabStrip.BackColor = palette.Header;
            _tabStrip.Invalidate();
            AppThemeManager.ApplyToolStrip(_menu, palette);
            foreach (var session in _sessions.Values)
            {
                if (session.DetachedWindow != null)
                    session.DetachedWindow.ApplyApplicationTheme(theme);
            }
        }

        public bool PaneMaximized { get; private set; }
        public int DetachedTabCount => _sessions.Values.Count(item => item.DetachedWindow != null);

        public void SetPaneMaximized(bool maximized)
        {
            PaneMaximized = maximized;
            _maximizePaneItem.Text = _text[maximized ? "Console.RestorePane" : "Console.MaximizePane"];
        }

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

            var foreground = ConsoleAppearance.ParseColor(settings.ConsoleForegroundColor, Color.Gainsboro);
            var configuredBackground = ConsoleAppearance.ParseColor(settings.ConsoleBackgroundColor, DefaultConsoleBackground);
            var background = ConsoleAppearance.Composite(configuredBackground, SystemColors.Control,
                settings.ConsoleBackgroundOpacity);
            _consoleForeground = foreground;
            _consoleBackground = background;
            BackColor = background;
            _contentHost.BackColor = background;
            _tabLayout.BackColor = background;
            _empty.BackColor = background;
            _empty.ForeColor = foreground;
            _tabStrip.InactiveTextColor = ConsoleAppearance.ParseColor(settings.ConsoleTabForegroundColor,
                Color.FromArgb(38, 43, 50));
            _tabStrip.ActiveTextColor = ConsoleAppearance.ParseColor(settings.ConsoleActiveTabForegroundColor,
                Color.FromArgb(245, 247, 250));
            var inactiveTab = ConsoleAppearance.ParseColor(settings.ConsoleTabBackgroundColor,
                Color.FromArgb(252, 252, 253));
            var activeTab = ConsoleAppearance.ParseColor(settings.ConsoleActiveTabBackgroundColor,
                DefaultConsoleBackground);
            _tabStrip.InactiveTabColor = ConsoleAppearance.WithOpacity(inactiveTab,
                settings.ConsoleTabBackgroundOpacity);
            _tabStrip.ActiveTabColor = ConsoleAppearance.WithOpacity(activeTab,
                settings.ConsoleActiveTabBackgroundOpacity);
            _tabStrip.HoverTabColor = ConsoleAppearance.WithOpacity(
                ConsoleAppearance.Composite(activeTab, inactiveTab, 45),
                Math.Max(settings.ConsoleTabBackgroundOpacity, settings.ConsoleActiveTabBackgroundOpacity));
            _tabStrip.Invalidate();

            foreach (var session in _sessions.Values)
            {
                session.Output.ForeColor = foreground;
                session.Output.BackColor = background;
                if (session.DetachedWindow != null) session.DetachedWindow.BackColor = background;
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
            if (session == null) return;
            if (session.DetachedWindow != null)
            {
                session.DetachedWindow.Show();
                session.DetachedWindow.Activate();
            }
            else _tabStrip.SelectTab(session.ProcessId);
        }

        public bool DetachSelectedTab()
        {
            var session = SelectedSession;
            if (session == null) return false;
            DetachSession(session, false);
            return true;
        }

        public bool ToggleSelectedTabFullScreen()
        {
            var session = SelectedSession;
            if (session == null) return false;
            session.ReturnToMainAfterFullScreen = true;
            DetachSession(session, true);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _text.Changed -= HandleLocalizationChanged;
                _flushTimer.Stop();
                _flushTimer.Dispose();
                foreach (var session in _sessions.Values)
                {
                    if (session.DetachedWindow != null)
                    {
                        session.DetachedWindow.ReleaseContent();
                        session.DetachedWindow.ClosePermanently();
                        session.Output.Dispose();
                    }
                    session.CustomFont?.Dispose();
                }
                _menu.Dispose();
                _consoleFont?.Dispose();
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
                    _tabStrip.SelectTab(started.ProcessId);
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

            var wordWrap = _wordWrapForScript(scriptId);
            var output = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = wordWrap,
                BackColor = _consoleBackground,
                ForeColor = _consoleForeground,
                BorderStyle = BorderStyle.None,
                DetectUrls = true,
                HideSelection = false,
                ScrollBars = wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both,
                Font = _consoleFont ?? new Font(FontFamily.GenericMonospace, 10f),
                ContextMenuStrip = _menu,
                Tag = processId,
                Visible = false
            };
            var session = new ConsoleSession
            {
                ScriptId = scriptId,
                ScriptName = string.IsNullOrWhiteSpace(scriptName) ? "PID " + processId : scriptName,
                ProcessId = processId,
                StartedAt = startedAt ?? DateTime.Now,
                OutputEncoding = outputEncoding,
                WordWrap = wordWrap,
                Output = output
            };
            _sessions[processId] = session;
            _contentHost.Controls.Add(output);
            _tabStrip.AddTab(processId, string.Empty, string.Empty, true);
            UpdateTabTitle(session);
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
            _contextSession = SessionFromControl(_menu.SourceControl) ?? SelectedSession;
            var session = _contextSession;
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
            _detachItem.Text = _text[session.DetachedWindow == null ? "Console.Detach" : "Console.Reattach"];
            _fullScreenItem.Checked = session.DetachedWindow != null && session.DetachedWindow.IsFullScreen;
            _maximizePaneItem.Visible = session.DetachedWindow == null;
            _maximizePaneItem.Text = _text[PaneMaximized ? "Console.RestorePane" : "Console.MaximizePane"];
            _closeItem.Text = session.ExitCode.HasValue ? _text["Console.CloseTab"] : _text["Console.CloseAndStop"];
        }

        private void CopySelection()
        {
            var session = MenuSession;
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
            var session = MenuSession;
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
            var session = MenuSession;
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
            var session = MenuSession;
            var item = sender as ToolStripMenuItem;
            if (session == null || item == null || !(item.Tag is ScriptOutputEncoding)) return;
            session.OutputEncoding = (ScriptOutputEncoding)item.Tag;
            RenderSession(session);
        }

        private void ToggleWordWrap()
        {
            var session = MenuSession;
            if (session == null) return;
            var wordWrap = !session.WordWrap;
            foreach (var related in _sessions.Values.Where(item => item.ScriptId == session.ScriptId))
                SetWordWrap(related, wordWrap);
            WordWrapChanged?.Invoke(this, new ConsoleWordWrapChangedEventArgs(session.ScriptId, wordWrap));
        }

        private static void SetWordWrap(ConsoleSession session, bool wordWrap)
        {
            session.WordWrap = wordWrap;
            session.Output.WordWrap = wordWrap;
            session.Output.ScrollBars = wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
        }

        private void ClearSelectedTab()
        {
            var session = MenuSession;
            if (session == null) return;
            session.History.Clear();
            session.HistoryUnits = 0;
            session.Output.Clear();
        }

        private void CloseSelectedTab()
        {
            var session = MenuSession;
            if (session != null) CloseSession(session);
        }

        private void ToggleDetached()
        {
            var session = MenuSession;
            if (session == null) return;
            if (session.DetachedWindow == null) DetachSession(session, false);
            else AttachSession(session);
        }

        private void ToggleFullScreen()
        {
            var session = MenuSession;
            if (session == null) return;
            if (session.DetachedWindow == null)
            {
                session.ReturnToMainAfterFullScreen = true;
                DetachSession(session, true);
                return;
            }
            session.DetachedWindow.ToggleFullScreen();
        }

        private void DetachSession(ConsoleSession session, bool fullScreen)
        {
            if (session.DetachedWindow != null)
            {
                session.DetachedWindow.Show();
                session.DetachedWindow.Activate();
                if (fullScreen) session.DetachedWindow.SetFullScreen(true);
                return;
            }

            _contentHost.Controls.Remove(session.Output);
            _tabStrip.RemoveTab(session.ProcessId);
            session.Output.Visible = true;
            var window = new DetachedConsoleForm(DetachedWindowTitle(session), session.Output)
            {
                BackColor = _consoleBackground
            };
            window.ApplyApplicationTheme(_applicationTheme);
            session.DetachedWindow = window;
            window.ReattachRequested += (sender, args) => AttachSession(session);
            window.FullScreenChanged += (sender, args) => HandleDetachedFullScreenChanged(session);
            window.Show();
            if (fullScreen) window.SetFullScreen(true);
            window.Activate();
            UpdateEmptyState();
        }

        private void AttachSession(ConsoleSession session)
        {
            var window = session.DetachedWindow;
            if (window == null) return;
            session.ReturnToMainAfterFullScreen = false;
            window.ReleaseContent();
            session.DetachedWindow = null;
            window.ClosePermanently();
            window.Dispose();
            _contentHost.Controls.Add(session.Output);
            _tabStrip.AddTab(session.ProcessId, string.Empty, string.Empty, !session.ExitCode.HasValue);
            UpdateTabTitle(session);
            _tabStrip.SelectTab(session.ProcessId);
            UpdateEmptyState();
        }

        private void HandleDetachedFullScreenChanged(ConsoleSession session)
        {
            if (session.DetachedWindow != null && !session.DetachedWindow.IsFullScreen &&
                session.ReturnToMainAfterFullScreen)
            {
                AttachSession(session);
            }
        }

        private void CloseSession(ConsoleSession session)
        {
            var isRunning = !session.ExitCode.HasValue;
            _suppressedProcesses.Add(session.ProcessId);
            _sessions.Remove(session.ProcessId);
            if (session.DetachedWindow != null)
            {
                session.DetachedWindow.ReleaseContent();
                session.DetachedWindow.ClosePermanently();
                session.DetachedWindow.Dispose();
                session.DetachedWindow = null;
            }
            else
            {
                _contentHost.Controls.Remove(session.Output);
                _tabStrip.RemoveTab(session.ProcessId);
            }
            session.Output.Dispose();
            session.CustomFont?.Dispose();
            UpdateEmptyState();
            CloseRequested?.Invoke(this,
                new ConsoleTabCloseRequestedEventArgs(session.ScriptId, session.ProcessId, isRunning));
        }

        private void HandleSelectedTabChanged(object sender, TerminalTabEventArgs args)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.DetachedWindow != null) continue;
                var selected = session.ProcessId == args.Key;
                session.Output.Visible = selected;
                if (selected) session.Output.BringToFront();
            }
        }

        private void HandleTabCloseRequested(object sender, TerminalTabEventArgs args)
        {
            ConsoleSession session;
            if (_sessions.TryGetValue(args.Key, out session)) CloseSession(session);
        }

        private ConsoleSession SelectedSession
        {
            get
            {
                ConsoleSession session;
                return _sessions.TryGetValue(_tabStrip.SelectedKey, out session) ? session : null;
            }
        }

        private ConsoleSession MenuSession => _contextSession ?? SelectedSession;

        private ConsoleSession SessionFromControl(Control control)
        {
            if (control != null && control.Tag is int)
            {
                ConsoleSession session;
                if (_sessions.TryGetValue((int)control.Tag, out session)) return session;
            }
            return null;
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
            _detachItem.Text = _text["Console.Detach"];
            _fullScreenItem.Text = _text["Console.FullScreen"];
            _maximizePaneItem.Text = _text[PaneMaximized ? "Console.RestorePane" : "Console.MaximizePane"];
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
            if (session.DetachedWindow != null)
            {
                session.DetachedWindow.Text = DetachedWindowTitle(session);
            }
            else
            {
                _tabStrip.UpdateTab(session.ProcessId,
                    name + " [" + session.ProcessId + "] · " + status,
                    (session.ScriptName ?? string.Empty) + " [" + session.ProcessId + "] · " + status,
                    !session.ExitCode.HasValue);
            }
        }

        private string DetachedWindowTitle(ConsoleSession session)
        {
            return _text.Get("Console.DetachedTitle", session.ScriptName ?? string.Empty, session.ProcessId,
                ApplicationResources.WindowTitle);
        }

        private void UpdateEmptyState()
        {
            _empty.Visible = _tabStrip.TabCount == 0;
            _tabLayout.Visible = !_empty.Visible;
            if (_empty.Visible) _empty.BringToFront();
            else _tabLayout.BringToFront();
        }
    }
}
