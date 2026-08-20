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
using CmdsManager.Infrastructure.Logging;
using CmdsManager.Infrastructure.Windows;
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
            internal bool ScrollLock { get; set; }
            internal Font CustomFont { get; set; }
            internal RichTextBox Output { get; set; }
            internal DetachedConsoleForm DetachedWindow { get; set; }
            internal ConsoleFindForm FindWindow { get; set; }
            internal ConsoleLogRecorder Recorder { get; set; }
            internal bool RecordingFailed { get; set; }
            internal bool AutomaticRecordingSuppressed { get; set; }
            internal bool ReturnToMainAfterFullScreen { get; set; }
        }

        private readonly LocalizationService _text;
        private readonly Func<ApplicationSettings> _settings;
        private readonly Func<HotkeySettings> _hotkeys;
        private readonly Func<Guid, bool> _wordWrapForScript;
        private readonly string _consoleLogDirectory;
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
        private readonly ToolStripMenuItem _findItem = new ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl+F" };
        private readonly ToolStripMenuItem _saveSelectionItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _saveAllItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _fontItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _encodingItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _wordWrapItem = new ToolStripMenuItem { CheckOnClick = false };
        private readonly ToolStripMenuItem _scrollLockItem = new ToolStripMenuItem
            { CheckOnClick = false, ShortcutKeyDisplayString = "Scroll Lock" };
        private readonly ToolStripMenuItem _startRecordingItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _pauseRecordingItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _stopRecordingItem = new ToolStripMenuItem();
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
            Func<Guid, bool> wordWrapForScript = null, string consoleLogDirectory = null)
            : this(text, settings, () => settings()?.Hotkeys ?? new HotkeySettings(),
                wordWrapForScript, consoleLogDirectory)
        {
        }

        public ConsoleTabsControl(LocalizationService text, Func<ApplicationSettings> settings,
            Func<HotkeySettings> hotkeys, Func<Guid, bool> wordWrapForScript, string consoleLogDirectory)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
            _wordWrapForScript = wordWrapForScript ?? (scriptId => false);
            _consoleLogDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(consoleLogDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "console")
                : consoleLogDirectory);

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
                _findItem,
                _saveSelectionItem,
                _saveAllItem,
                new ToolStripSeparator(),
                _fontItem,
                _encodingItem,
                _wordWrapItem,
                _scrollLockItem,
                new ToolStripSeparator(),
                _startRecordingItem,
                _pauseRecordingItem,
                _stopRecordingItem,
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
            _findItem.Click += (sender, args) => ShowFind(MenuSession);
            _saveSelectionItem.Click += (sender, args) => SaveConsoleText(true);
            _saveAllItem.Click += (sender, args) => SaveConsoleText(false);
            _fontItem.Click += (sender, args) => ChooseFont();
            _wordWrapItem.Click += (sender, args) => ToggleWordWrap();
            _scrollLockItem.Click += (sender, args) => ToggleScrollLock(MenuSession);
            _startRecordingItem.Click += (sender, args) => StartRecording(MenuSession, true, true);
            _pauseRecordingItem.Click += (sender, args) => ToggleRecordingPause(MenuSession);
            _stopRecordingItem.Click += (sender, args) => StopRecording(MenuSession, true, true, true);
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
                if (session.FindWindow != null)
                    session.FindWindow.ApplyApplicationTheme(theme);
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
            var hotkeys = _hotkeys() ?? new HotkeySettings();
            _copyItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.CopyConsoleSelection);
            _findItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.FindConsole);
            _saveAllItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.SaveConsole);
            _wordWrapItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.ToggleWordWrap);
            _scrollLockItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.ToggleScrollLock);
            _detachItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.ToggleConsoleDetach);
            _fullScreenItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.ToggleConsoleFullScreen);
            _maximizePaneItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.ToggleConsolePane);
            _clearItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.ClearConsole);
            _closeItem.ShortcutKeyDisplayString = ShortcutText(hotkeys, HotkeyAction.CloseConsoleTab);
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
                if (TrimHistory(session, BufferUnits(settings))) RenderSession(session);
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

        public bool SelectAdjacentTab(int offset)
        {
            if (_tabStrip.TabCount == 0) return false;
            var current = _tabStrip.SelectedIndex < 0 ? 0 : _tabStrip.SelectedIndex;
            var next = (current + offset) % _tabStrip.TabCount;
            if (next < 0) next += _tabStrip.TabCount;
            return _tabStrip.SelectIndex(next);
        }

        public bool CloseActiveTab()
        {
            var session = SelectedSession;
            if (session == null) return false;
            CloseSession(session);
            return true;
        }

        public bool ToggleActiveTabDetached()
        {
            var session = SelectedSession;
            if (session == null) return false;
            ToggleDetached(session);
            return true;
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
                    if (session.FindWindow != null)
                    {
                        session.FindWindow.Close();
                        session.FindWindow.Dispose();
                        session.FindWindow = null;
                    }
                    session.Recorder?.Dispose();
                    session.Recorder = null;
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
            var recordingBatches = new Dictionary<int, StringBuilder>();
            var redraw = new HashSet<int>();
            var exitedRecordings = new HashSet<int>();
            var bufferUnits = BufferUnits(_settings() ?? new ApplicationSettings());
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
                        exitedRecordings.Add(exited.ProcessId);
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
                var decodedLine = DecodeLine(session, historyLine);
                session.History.Enqueue(historyLine);
                session.HistoryUnits += HistoryUnits(historyLine);
                if (TrimHistory(session, bufferUnits)) redraw.Add(session.ProcessId);

                if (session.Recorder != null && session.Recorder.State == ConsoleRecordingState.Recording)
                {
                    StringBuilder recordingBuilder;
                    if (!recordingBatches.TryGetValue(item.Output.ProcessId, out recordingBuilder))
                    {
                        recordingBuilder = new StringBuilder();
                        recordingBatches[item.Output.ProcessId] = recordingBuilder;
                    }
                    recordingBuilder.AppendLine(decodedLine);
                }

                if (redraw.Contains(session.ProcessId)) continue;
                StringBuilder builder;
                if (!batches.TryGetValue(item.Output.ProcessId, out builder))
                {
                    builder = new StringBuilder();
                    batches[item.Output.ProcessId] = builder;
                }
                builder.AppendLine(decodedLine);
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
                if (_sessions.TryGetValue(batch.Key, out session)) AppendBatch(session, batch.Value.ToString());
            }
            foreach (var batch in recordingBatches)
            {
                ConsoleSession session;
                if (!_sessions.TryGetValue(batch.Key, out session) || session.Recorder == null) continue;
                if (!session.Recorder.Write(batch.Value.ToString())) UpdateTabTitle(session);
            }
            foreach (var processId in exitedRecordings)
            {
                ConsoleSession session;
                if (_sessions.TryGetValue(processId, out session)) StopRecording(session, true);
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
                TryStartAutomaticRecording(existing);
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
                ShortcutsEnabled = false,
                ScrollBars = wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both,
                Font = _consoleFont ?? new Font(FontFamily.GenericMonospace, 10f),
                ContextMenuStrip = _menu,
                Tag = processId,
                Visible = false
            };
            output.KeyDown += HandleOutputKeyDown;
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
            TryStartAutomaticRecording(session);
            return session;
        }

        private static int HistoryUnits(ConsoleHistoryLine line)
        {
            return (line.RawBytes == null ? (line.OriginalText ?? string.Empty).Length : line.RawBytes.Length) + 2;
        }

        private static int BufferUnits(ApplicationSettings settings)
        {
            var kilobytes = Math.Max(64, Math.Min(1048576,
                settings?.ConsoleBufferSizeKb ?? new ApplicationSettings().ConsoleBufferSizeKb));
            return checked(kilobytes * 1024);
        }

        private static bool TrimHistory(ConsoleSession session, int maximumUnits)
        {
            if (session.HistoryUnits <= maximumUnits) return false;
            var trimToUnits = Math.Max(1, maximumUnits * 3 / 4);
            while (session.History.Count > 1 && session.HistoryUnits > trimToUnits)
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

        private void RenderSession(ConsoleSession session)
        {
            var maximumUnits = BufferUnits(_settings() ?? new ApplicationSettings());
            var output = session.Output;
            var preserveScroll = session.ScrollLock && output.IsHandleCreated;
            var firstVisibleLine = preserveScroll ? FirstVisibleLine(output) : 0;
            var selectionStart = output.SelectionStart;
            var selectionLength = output.SelectionLength;
            var builder = new StringBuilder(Math.Min(maximumUnits, Math.Max(0, session.HistoryUnits)));
            foreach (var line in session.History) builder.AppendLine(DecodeLine(session, line));
            var text = builder.ToString();
            if (text.Length > maximumUnits)
                text = text.Substring(text.Length - Math.Max(1, maximumUnits * 3 / 4));
            output.Text = text;
            if (preserveScroll) RestoreScroll(output, firstVisibleLine, selectionStart, selectionLength);
            else
            {
                output.SelectionStart = output.TextLength;
                output.SelectionLength = 0;
                output.ScrollToCaret();
            }
        }

        private static void AppendBatch(ConsoleSession session, string text)
        {
            if (text.Length == 0) return;
            var output = session.Output;
            var wasAtEnd = output.SelectionStart >= output.TextLength - 1;
            var preserveScroll = session.ScrollLock && output.IsHandleCreated;
            var firstVisibleLine = preserveScroll ? FirstVisibleLine(output) : 0;
            var selectionStart = output.SelectionStart;
            var selectionLength = output.SelectionLength;
            output.AppendText(text);
            if (preserveScroll)
            {
                RestoreScroll(output, firstVisibleLine, selectionStart, selectionLength);
            }
            else if (wasAtEnd)
            {
                output.SelectionStart = output.TextLength;
                output.SelectionLength = 0;
                output.ScrollToCaret();
            }
        }

        private static int FirstVisibleLine(RichTextBox output)
        {
            return output == null || !output.IsHandleCreated ? 0 :
                NativeMethods.SendMessage(output.Handle, NativeMethods.EmGetFirstVisibleLine,
                    IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        private static void RestoreScroll(RichTextBox output, int firstVisibleLine,
            int selectionStart, int selectionLength)
        {
            if (output == null || !output.IsHandleCreated) return;
            output.SelectionStart = Math.Max(0, Math.Min(selectionStart, output.TextLength));
            output.SelectionLength = Math.Max(0,
                Math.Min(selectionLength, output.TextLength - output.SelectionStart));
            var currentLine = FirstVisibleLine(output);
            var difference = firstVisibleLine - currentLine;
            if (difference != 0)
                NativeMethods.SendMessage(output.Handle, NativeMethods.EmLineScroll,
                    IntPtr.Zero, new IntPtr(difference));
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
            _findItem.Enabled = session.Output.TextLength > 0;
            _saveSelectionItem.Enabled = session.Output.SelectionLength > 0;
            _saveAllItem.Enabled = session.Output.TextLength > 0;
            _clearItem.Enabled = session.Output.TextLength > 0;
            _wordWrapItem.Checked = session.WordWrap;
            _scrollLockItem.Checked = session.ScrollLock;
            var recordingState = session.Recorder?.State ?? ConsoleRecordingState.Stopped;
            _startRecordingItem.Enabled = !session.ExitCode.HasValue &&
                (recordingState == ConsoleRecordingState.Stopped ||
                    recordingState == ConsoleRecordingState.LimitReached);
            _pauseRecordingItem.Enabled = recordingState == ConsoleRecordingState.Recording ||
                recordingState == ConsoleRecordingState.Paused;
            _pauseRecordingItem.Text = _text[recordingState == ConsoleRecordingState.Paused
                ? "Console.ResumeRecording" : "Console.PauseRecording"];
            _stopRecordingItem.Enabled = recordingState == ConsoleRecordingState.Recording ||
                recordingState == ConsoleRecordingState.Paused;
            foreach (var pair in _encodingItems) pair.Value.Checked = pair.Key == session.OutputEncoding;
            _detachItem.Text = _text[session.DetachedWindow == null ? "Console.Detach" : "Console.Reattach"];
            _fullScreenItem.Checked = session.DetachedWindow != null && session.DetachedWindow.IsFullScreen;
            _maximizePaneItem.Visible = session.DetachedWindow == null;
            _maximizePaneItem.Text = _text[PaneMaximized ? "Console.RestorePane" : "Console.MaximizePane"];
            _closeItem.Text = session.ExitCode.HasValue ? _text["Console.CloseTab"] : _text["Console.CloseAndStop"];
        }

        private void CopySelection()
        {
            CopySelection(MenuSession);
        }

        private void CopySelection(ConsoleSession session)
        {
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
            SaveConsoleText(MenuSession, selectionOnly);
        }

        private void SaveConsoleText(ConsoleSession session, bool selectionOnly)
        {
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

        private static void ChangeFontSize(ConsoleSession session, float delta)
        {
            if (session == null) return;
            var size = Math.Max(6f, Math.Min(48f, session.Output.Font.SizeInPoints + delta));
            if (Math.Abs(size - session.Output.Font.SizeInPoints) < 0.01f) return;
            Font replacement;
            try
            {
                replacement = new Font(session.Output.Font.FontFamily, size,
                    session.Output.Font.Style, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                return;
            }
            var previous = session.CustomFont;
            session.CustomFont = replacement;
            session.Output.Font = replacement;
            previous?.Dispose();
        }

        private void ResetFont(ConsoleSession session)
        {
            if (session == null || _consoleFont == null) return;
            var previous = session.CustomFont;
            session.CustomFont = null;
            session.Output.Font = _consoleFont;
            previous?.Dispose();
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
            ToggleWordWrap(MenuSession);
        }

        private void ToggleWordWrap(ConsoleSession session)
        {
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

        private void ShowFind(ConsoleSession session)
        {
            if (session == null) return;
            FlushPendingOutput();
            if (session.FindWindow == null || session.FindWindow.IsDisposed)
            {
                var window = new ConsoleFindForm(_text, session.Output, _applicationTheme);
                session.FindWindow = window;
                window.FormClosed += (sender, args) =>
                {
                    if (ReferenceEquals(session.FindWindow, window)) session.FindWindow = null;
                };
                if (session.Output.SelectionLength > 0)
                    window.SearchText = session.Output.SelectedText.Replace("\r", string.Empty).Replace("\n", string.Empty);
            }

            var owner = session.Output.FindForm();
            if (!session.FindWindow.Visible)
            {
                if (owner == null) session.FindWindow.Show();
                else session.FindWindow.Show(owner);
            }
            else session.FindWindow.Activate();
        }

        private void ToggleScrollLock(ConsoleSession session)
        {
            if (session == null) return;
            session.ScrollLock = !session.ScrollLock;
            if (!session.ScrollLock)
            {
                session.Output.SelectionStart = session.Output.TextLength;
                session.Output.SelectionLength = 0;
                session.Output.ScrollToCaret();
            }
        }

        private void TryStartAutomaticRecording(ConsoleSession session)
        {
            var settings = _settings() ?? new ApplicationSettings();
            if (session == null || session.ExitCode.HasValue || !settings.ConsoleAutoRecord || session.Recorder != null ||
                session.RecordingFailed || session.AutomaticRecordingSuppressed)
                return;
            StartRecording(session, false, false);
        }

        private void StartRecording(ConsoleSession session, bool includeExistingText, bool showErrors)
        {
            if (session == null || session.ExitCode.HasValue) return;
            if (includeExistingText) FlushPendingOutput();
            try
            {
                session.Recorder?.Dispose();
                session.Recorder = null;
                var settings = _settings() ?? new ApplicationSettings();
                ConsoleLogRecorder.DeleteExpiredLogs(_consoleLogDirectory, settings.LogRetentionDays);
                session.Recorder = new ConsoleLogRecorder(_consoleLogDirectory, session.ScriptName,
                    session.ProcessId, DateTime.Now,
                    checked((long)settings.ConsoleLogMaxSizeMb * 1024L * 1024L));
                session.RecordingFailed = false;
                session.AutomaticRecordingSuppressed = false;
                if (includeExistingText && session.Output.TextLength > 0)
                    session.Recorder.Write(session.Output.Text);
                UpdateTabTitle(session);
            }
            catch (Exception exception)
            {
                session.Recorder?.Dispose();
                session.Recorder = null;
                session.RecordingFailed = true;
                UpdateTabTitle(session);
                if (showErrors)
                {
                    MessageBox.Show(this, exception.Message, _text["Console.RecordFailed"],
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ToggleRecordingPause(ConsoleSession session)
        {
            if (session?.Recorder == null) return;
            FlushPendingOutput();
            if (session.Recorder.State == ConsoleRecordingState.Paused) session.Recorder.Resume();
            else if (session.Recorder.State == ConsoleRecordingState.Recording) session.Recorder.Pause();
            UpdateTabTitle(session);
        }

        private void StopRecording(ConsoleSession session, bool updateTitle, bool flushPending = false,
            bool suppressAutomaticRestart = false)
        {
            if (session == null) return;
            if (flushPending) FlushPendingOutput();
            session.Recorder?.Dispose();
            session.Recorder = null;
            session.RecordingFailed = false;
            if (suppressAutomaticRestart) session.AutomaticRecordingSuppressed = true;
            if (updateTitle) UpdateTabTitle(session);
        }

        private void HandleOutputKeyDown(object sender, KeyEventArgs args)
        {
            var session = SessionFromControl(sender as Control);
            if (session == null) return;
            if (TryHandleSessionHotkey(session, args.KeyData))
            {
                args.Handled = true;
                args.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (SelectedSession != null && TryHandleSessionHotkey(SelectedSession, keyData)) return true;
            return base.ProcessCmdKey(ref message, keyData);
        }

        private bool TryHandleSessionHotkey(ConsoleSession session, Keys keyData)
        {
            if (session == null) return false;
            if (MatchesHotkey(HotkeyAction.FindConsole, keyData)) { ShowFind(session); return true; }
            if (MatchesHotkey(HotkeyAction.FindNext, keyData))
            {
                if (session.FindWindow == null) ShowFind(session);
                else session.FindWindow.FindNext();
                return true;
            }
            if (MatchesHotkey(HotkeyAction.FindPrevious, keyData))
            {
                if (session.FindWindow == null) ShowFind(session);
                else session.FindWindow.FindPrevious();
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ToggleScrollLock, keyData))
            {
                ToggleScrollLock(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ToggleConsoleFullScreen, keyData))
            {
                ToggleFullScreen(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ToggleWordWrap, keyData))
            {
                ToggleWordWrap(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ClearConsole, keyData))
            {
                ClearSession(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.CopyConsoleSelection, keyData))
            {
                CopySelection(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.SelectAllConsole, keyData))
            {
                session.Output.SelectAll();
                return true;
            }
            if (MatchesHotkey(HotkeyAction.SaveConsole, keyData))
            {
                SaveConsoleText(session, false);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.IncreaseConsoleFont, keyData))
            {
                ChangeFontSize(session, 1f);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.DecreaseConsoleFont, keyData))
            {
                ChangeFontSize(session, -1f);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ResetConsoleFont, keyData))
            {
                ResetFont(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.CloseConsoleTab, keyData))
            {
                CloseSession(session);
                return true;
            }
            if (MatchesHotkey(HotkeyAction.ToggleConsoleDetach, keyData))
            {
                ToggleDetached(session);
                return true;
            }
            return false;
        }

        private bool MatchesHotkey(HotkeyAction action, Keys keyData)
        {
            var settings = _hotkeys() ?? new HotkeySettings();
            var binding = settings[action];
            if (!binding.Enabled) return false;
            ShowAppHotkeyGesture gesture;
            return ShowAppHotkeyGesture.TryParse(binding.Gesture, false, out gesture) && gesture.Matches(keyData);
        }

        private void ClearSelectedTab()
        {
            ClearSession(MenuSession);
        }

        private static void ClearSession(ConsoleSession session)
        {
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
            ToggleDetached(MenuSession);
        }

        private void ToggleDetached(ConsoleSession session)
        {
            if (session == null) return;
            if (session.DetachedWindow == null) DetachSession(session, false);
            else AttachSession(session);
        }

        private void ToggleFullScreen()
        {
            ToggleFullScreen(MenuSession);
        }

        private void ToggleFullScreen(ConsoleSession session)
        {
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

            CloseFindWindow(session);

            _contentHost.Controls.Remove(session.Output);
            _tabStrip.RemoveTab(session.ProcessId);
            session.Output.Visible = true;
            var window = new DetachedConsoleForm(DetachedWindowTitle(session), session.Output,
                keyData => TryHandleSessionHotkey(session, keyData))
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
            CloseFindWindow(session);
            StopRecording(session, false);
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

        private static void CloseFindWindow(ConsoleSession session)
        {
            var window = session?.FindWindow;
            if (window == null) return;
            session.FindWindow = null;
            if (!window.IsDisposed) window.Close();
            window.Dispose();
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

        private static string ShortcutText(HotkeySettings settings, HotkeyAction action)
        {
            var binding = settings[action];
            return binding.Enabled ? binding.Gesture ?? string.Empty : string.Empty;
        }

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
            _findItem.Text = _text["Console.Find"];
            _saveSelectionItem.Text = _text["Console.SaveSelection"];
            _saveAllItem.Text = _text["Console.SaveAll"];
            _fontItem.Text = _text["Console.SelectFont"];
            _encodingItem.Text = _text["Console.Encoding"];
            _wordWrapItem.Text = _text["Console.WordWrap"];
            _scrollLockItem.Text = _text["Console.ScrollLock"];
            _startRecordingItem.Text = _text["Console.StartRecording"];
            _pauseRecordingItem.Text = _text["Console.PauseRecording"];
            _stopRecordingItem.Text = _text["Console.StopRecording"];
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
            foreach (var session in _sessions.Values)
            {
                session.FindWindow?.ApplyLocalization();
                UpdateTabTitle(session);
            }
        }

        private void UpdateTabTitle(ConsoleSession session)
        {
            var name = session.ScriptName ?? string.Empty;
            if (name.Length > 28) name = name.Substring(0, 27) + "…";
            var status = session.ExitCode.HasValue
                ? _text.Get("Console.Exited", session.ExitCode.Value)
                : _text["Console.Running"];
            var recordingStatus = RecordingStatus(session);
            if (recordingStatus.Length > 0) status += " · " + recordingStatus;
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
            var result = _text.Get("Console.DetachedTitle", session.ScriptName ?? string.Empty, session.ProcessId,
                ApplicationResources.WindowTitle);
            var recordingStatus = RecordingStatus(session);
            return recordingStatus.Length == 0 ? result : result + " · " + recordingStatus;
        }

        private string RecordingStatus(ConsoleSession session)
        {
            if (session == null) return string.Empty;
            if (session.RecordingFailed) return _text["Console.RecordingFailed"];
            if (session.Recorder == null) return string.Empty;
            switch (session.Recorder.State)
            {
                case ConsoleRecordingState.Recording:
                    return _text["Console.Recording"];
                case ConsoleRecordingState.Paused:
                    return _text["Console.RecordingPaused"];
                case ConsoleRecordingState.LimitReached:
                    return _text["Console.RecordingLimit"];
                default:
                    return string.Empty;
            }
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
