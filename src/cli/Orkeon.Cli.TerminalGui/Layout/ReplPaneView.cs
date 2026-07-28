using System.Drawing;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// REPL pane: command history (read-only) on top, prompt prefix label + a soft-wrapping, auto-growing
/// multi-line input field at the bottom.
/// <see cref="ReadLineAsync"/> bridges the event-driven UI to the synchronous-blocking
/// <see cref="System.IO.TextReader.ReadLine"/> contract used by <see cref="Orkeon.Cli.Abstractions.Console.IConsoleAdapter"/>.
/// </summary>
public sealed class ReplPaneView : FrameView
{
    private readonly TextView _history;
    private readonly Label _promptLabel;
    private readonly TextView _input;
    private readonly IUiDispatcher _dispatcher;

    // Cap on how many rows the multi-line input may grow to before it scrolls internally,
    // so a long paste / many Shift+Enter lines never swallow the whole REPL pane.
    private const int MaxInputRows = 8;
    private int _inputRows = 1;
    private readonly Lock _gate = new();
    private TaskCompletionSource<string?>? _pendingRead;
    private CancellationTokenRegistration _pendingReadCancellation;

    // Cap 2 — submitted-line history. Newest at the end. _historyCursor walks it:
    // [0.._lineHistory.Count) selects an entry, == Count means "the in-progress draft".
    private readonly List<string> _lineHistory = new();
    private int _historyCursor;
    private string _draft = string.Empty;

    // Double-Escape clears the draft. Armed by the first Escape, fired (and reset) by a second
    // consecutive Escape; any other key disarms it. A single Escape is swallowed so it never quits.
    private bool _escapeArmed;

    // Caps 3/4 — completion source for "/cmd" and "@path" tokens. Null ⇒ Tab is inert.
    private IReplInputAssist? _assist;

    public ReplPaneView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal ReplPaneView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        Title = options.ReplPaneTitle;
        SetScheme(SchemeFactory.Pane());

        _history = new MouseClipboardTextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            Multiline = true,
            // Display-only soft-wrap when enabled: Terminal.Gui keeps the original unwrapped model
            // (WordWrapManager.Model) and re-maps the selection back to it, so copy/paste returns the
            // source text with no injected CR. The horizontal scrollbar is auto-hidden while wrapping.
            WordWrap = options.ReplWordWrap,
            // Read-only display only — kept out of the focus cycle so Tab and the initial
            // focus go straight to the prompt input field. Mouse selection still works
            // (Terminal.Gui v2 raises OnMouseEvent on non-focusable views), and auto-copy
            // hooks via MouseClipboardTextView regardless of focus state.
            CanFocus = false,
            ScrollBars = true,
        };
        _history.SetScheme(SchemeFactory.Pane());
        _promptLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Height = 1,
            Text = string.Empty,
        };
        _promptLabel.SetScheme(SchemeFactory.Pane());
        _input = new TextView
        {
            X = Pos.Right(_promptLabel),
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
            // Multi-line prompt: Ctrl+O (or Shift+Enter where the terminal reports it) adds a line
            // (see OnInputKeyDown); plain Enter submits. Terminal.Gui inversely couples
            // EnterKeyAddsLine and Multiline — setting EnterKeyAddsLine=false flips Multiline off (and
            // it is also incompatible with WordWrap, which needs Multiline). So we keep Multiline=true
            // and stop Enter from inserting a line by intercepting it in OnInputKeyDown
            // (SubmitLine + key.Handled = true) rather than via EnterKeyAddsLine.
            Multiline = true,
            // Soft-wrap long input instead of scrolling horizontally: a long line flows onto extra
            // display rows (and the box grows — see SyncInputHeight). Wrapping is display-only; the
            // logical model (and therefore the submitted text) keeps no injected newline. Must be set
            // after Multiline=true, otherwise the setter no-ops.
            WordWrap = true,
            ScrollBars = false,
        };
        _input.SetScheme(SchemeFactory.Editable());
        _input.KeyDown += OnInputKeyDown;
        // Grow / shrink the input box (and the history above it) whenever the line count changes
        // — Shift+Enter, paste, history recall, or a backspace that merges two lines.
        _input.ContentsChanged += (_, _) => SyncInputHeight();

        Add(_history, _promptLabel, _input);

        // Force initial focus on the input field once the view is laid out, otherwise
        // Terminal.Gui defaults to the first focusable child (which would skip past
        // the input if any sibling pane is focusable too).
        Initialized += (_, _) => _input.SetFocus();
    }

    /// <summary>Emitted on every key down on the input field. Used by the console adapter's <c>ReadKey</c>.</summary>
    public event EventHandler<KeyCapturedEventArgs>? KeyCaptured;

    /// <summary>
    /// The editable multi-line input field. Exposed so the global right-click
    /// paste handler (<see cref="MouseClipboardBehavior.AttachRightClickPaste"/>)
    /// can target the only writable surface in the split-pane.
    /// </summary>
    public TextView Input => _input;

    /// <summary>
    /// Provides the Tab-completion source for <c>/command</c> and <c>@path</c> tokens. Called once
    /// by the host after the view is built; null-safe (Tab stays inert until a source is set).
    /// </summary>
    public void ConfigureCompletion(IReplInputAssist assist)
    {
        ArgumentNullException.ThrowIfNull(assist);
        _assist = assist;
    }

    public void AppendOutput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _dispatcher.Invoke(() =>
        {
            _history.Text += text;
            _history.MoveEnd();
        });
    }

    public void AppendOutputLine(string text) => AppendOutput((text ?? string.Empty) + "\n");

    public void SetPromptPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        _dispatcher.Invoke(() => _promptLabel.Text = prefix);
    }

    public void Clear()
    {
        _dispatcher.Invoke(() => _history.Text = string.Empty);
    }

    public void FocusPrompt()
    {
        _dispatcher.Invoke(() => _input.SetFocus());
    }

    /// <summary>
    /// Toggles word wrap on the history pane: enabling it hides the horizontal scrollbar and wraps
    /// long lines (display-only, copy/paste preserved); disabling it restores the horizontal scrollbar.
    /// </summary>
    public void ToggleWrap() => _dispatcher.Invoke(() => _history.WordWrap = !_history.WordWrap);

    /// <summary>Current wrap state (true ⇒ wrapping, no horizontal scrollbar).</summary>
    public bool IsWordWrapEnabled => _history.WordWrap;

    /// <summary>
    /// Forcibly completes any pending <see cref="ReadLineAsync"/> with <c>null</c>.
    /// Called by <see cref="Orkeon.Cli.TerminalGui.Hosting.TerminalGuiHost"/> when the
    /// UI loop is stopping (Ctrl+Q, parent cancellation), so the runner's blocked
    /// <c>Console.ReadLine()</c> returns <c>null</c> and its REPL loop can exit cleanly
    /// instead of hanging forever waiting for input that will never come.
    /// </summary>
    public void CancelPendingRead() => CompletePendingRead(null, null);

    /// <summary>
    /// Returns when the user submits a line (Enter), or null on cancellation.
    /// Only one outstanding ReadLineAsync at a time.
    /// </summary>
    public Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_pendingRead is not null)
                throw new InvalidOperationException("A ReadLineAsync is already in progress on this REPL pane.");
            _pendingRead = tcs;
            _pendingReadCancellation = ct.Register(static state =>
            {
                var (self, source) = ((ReplPaneView, TaskCompletionSource<string?>))state!;
                self.CompletePendingRead(source, null);
            }, (this, tcs));
        }
        return tcs.Task;
    }

    private void OnInputKeyDown(object? sender, Key key)
    {
        // Notify external listeners first (TerminalGuiConsoleAdapter.ReadKey relies on this).
        KeyCaptured?.Invoke(this, new KeyCapturedEventArgs(key));
        if (key.Handled) return;

        var bare = PaneClipboard.BareKey(key);

        // Escape never quits the app from the prompt: a single Escape is swallowed, a second
        // consecutive Escape clears the current draft (Esc-Esc). Any other key disarms the sequence.
        if (bare == KeyCode.Esc)
        {
            if (_escapeArmed)
                ClearInput();
            _escapeArmed = !_escapeArmed;
            key.Handled = true;
            return;
        }
        _escapeArmed = false;

        // Clipboard shortcuts on the active input field. Ctrl+C is normally swallowed earlier by the
        // host's global handler (copy-on-selection / cancel-command); it is kept here so the input
        // stays self-contained outside runner mode. Ctrl+V is the only paste surface in the split-pane.
        // Ctrl+O adds a newline — the reliable multi-line trigger across terminals (Enter submits),
        // mirroring the plain-mode LineEditor. Terminals fold Ctrl+J / Shift+Enter into a plain Enter.
        if (key.IsCtrl && TryHandleCtrlShortcut(bare))
        {
            key.Handled = true;
            return;
        }

        // Enter submits. Shift+Enter adds a newline where the terminal reports it as distinct
        // (Ctrl+O is the portable fallback). Alt+Enter is left untouched for the terminal/OS.
        if (bare == KeyCode.Enter && !key.IsAlt)
        {
            if (key.IsShift)
                _input.InsertText("\n");
            else
                SubmitLine();
            key.Handled = true;
            return;
        }

        HandleNavigationKey(key);
    }

    /// <summary>
    /// Handles the Ctrl-modified editing shortcuts (select-all, copy, paste, insert newline) on the
    /// input field. Returns true when the key was one of the handled shortcuts (the caller then marks
    /// it handled and stops), false for any other Ctrl key so the caller can fall through.
    /// </summary>
    private bool TryHandleCtrlShortcut(KeyCode bare)
    {
        switch (bare)
        {
            case KeyCode.A:
                _input.SelectAll();
                return true;
            case KeyCode.C:
                PaneClipboard.CopySelection(_input);
                return true;
            case KeyCode.V:
                PaneClipboard.PasteInto(_input);
                return true;
            case KeyCode.O:
                _input.InsertText("\n");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Handles the history-recall (↑/↓ at the text boundaries) and Tab-completion keys. ↑/↓ in the
    /// middle of a multi-line draft are left unhandled so the native TextView moves the caret.
    /// </summary>
    private void HandleNavigationKey(Key key)
    {
        switch (key.KeyCode)
        {
            // ↑/↓ recall command history only at the text boundaries; in the middle of a multi-line
            // draft they move the caret between lines (native TextView behaviour — left unhandled).
            case KeyCode.CursorUp:
                if (_input.CurrentRow == 0)
                {
                    NavigateHistory(-1);
                    key.Handled = true;
                }
                return;
            case KeyCode.CursorDown:
                if (_input.CurrentRow >= _input.Lines - 1)
                {
                    NavigateHistory(+1);
                    key.Handled = true;
                }
                return;
            case KeyCode.Tab:
                TryComplete();
                key.Handled = true;
                return;
        }
    }

    /// <summary>
    /// Resizes the input box to fit its <em>visual</em> row count (1..<see cref="MaxInputRows"/>) and
    /// shrinks the history pane above it to match, so a multi-line or soft-wrapped draft is fully
    /// visible while it is being typed.
    /// </summary>
    private void SyncInputHeight()
    {
        var rows = Math.Clamp(VisualRowCount(), 1, MaxInputRows);
        if (rows == _inputRows) return;
        _inputRows = rows;

        _history.Height = Dim.Fill(rows);
        _promptLabel.Y = Pos.AnchorEnd(rows);
        _input.Y = Pos.AnchorEnd(rows);
        _input.Height = rows;
        SetNeedsLayout();
    }

    /// <summary>
    /// Number of display rows the current draft occupies once soft-wrap is applied. Because
    /// Terminal.Gui only re-wraps inside a live draw cycle (the WordWrapManager reports the logical
    /// line count outside one), this takes the largest of three sources so the box never clips text:
    /// the logical line count, Terminal.Gui's reported content height, and a deterministic width-based
    /// estimate (<see cref="CountWrappedRows"/>). Over-counting only ever leaves a blank row; the
    /// estimate falls back to the logical line count when the viewport width is not yet known (tests).
    /// </summary>
    private int VisualRowCount()
    {
        var logical = Math.Max(1, _input.Lines);
        var reported = _input.GetContentHeight();
        var estimate = CountWrappedRows(_input.Text, _input.Viewport.Width);
        return Math.Max(logical, Math.Max(reported, estimate));
    }

    /// <summary>
    /// Deterministic estimate of how many display rows <paramref name="text"/> spans when hard-wrapped
    /// at <paramref name="width"/> columns: each logical line contributes <c>ceil(len / width)</c> rows
    /// (at least one). With <paramref name="width"/> &lt;= 0 (not laid out yet) the wrap is unknown, so
    /// it returns the logical line count. This under-counts only when word-wrap keeps a word whole on
    /// the next row; <see cref="VisualRowCount"/> covers that via Terminal.Gui's reported height.
    /// </summary>
    internal static int CountWrappedRows(string? text, int width)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (width <= 0) return Math.Max(1, lines.Length);
        var rows = 0;
        foreach (var line in lines)
            rows += Math.Max(1, (line.Length + width - 1) / width);
        return Math.Max(1, rows);
    }

    /// <summary>
    /// Esc-Esc: discards the current (possibly multi-line) draft and returns to a fresh prompt,
    /// without completing the pending read. The <see cref="TextView.ContentsChanged"/> hook collapses
    /// the input back to a single row.
    /// </summary>
    private void ClearInput()
    {
        _input.Text = string.Empty;
        _draft = string.Empty;
        _historyCursor = _lineHistory.Count;
    }

    /// <summary>
    /// Enter: record the (possibly multi-line) entry in history and complete the pending read.
    /// Line endings are normalised to <c>\n</c> so downstream consumers never see Terminal.Gui's
    /// internal <c>\r\n</c>.
    /// </summary>
    private void SubmitLine()
    {
        var text = (_input.Text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        _input.Text = string.Empty;

        var trimmed = text.Trim();
        if (trimmed.Length > 0)
        {
            EchoSubmittedLine(text);
            if (_lineHistory.Count == 0 || !string.Equals(_lineHistory[^1], text, StringComparison.Ordinal))
                _lineHistory.Add(text);
        }
        _historyCursor = _lineHistory.Count;
        _draft = string.Empty;

        CompletePendingRead(_pendingRead, text);
    }

    /// <summary>
    /// Echoes the just-submitted entry into the read-only history pane above the prompt, the way a
    /// terminal keeps typed commands in its scrollback. The text is echoed verbatim (no prompt
    /// prefix), each logical line on its own row.
    /// </summary>
    private void EchoSubmittedLine(string text)
    {
        foreach (var line in text.Split('\n'))
            AppendOutputLine(line);
    }

    /// <summary>↑/↓ (cap 2): walk submitted-line history, preserving the in-progress draft slot.</summary>
    private void NavigateHistory(int direction)
    {
        if (_lineHistory.Count == 0) return;

        // Entering navigation from the live draft: stash it so ↓ can restore it.
        if (_historyCursor == _lineHistory.Count)
            _draft = _input.Text ?? string.Empty;

        var next = Math.Clamp(_historyCursor + direction, 0, _lineHistory.Count);
        _historyCursor = next;

        SetInputText(next == _lineHistory.Count ? _draft : _lineHistory[next]);
    }

    /// <summary>
    /// Tab (caps 3/4): complete the token at the cursor. A leading <c>/</c> on the first token
    /// completes command names; any <c>@</c> token completes virtual paths. Inert without a source.
    /// </summary>
    private void TryComplete()
    {
        if (_assist is null) return;

        // Completion is scoped to the caret's current line so it behaves the same on a single-line
        // command and inside a multi-line draft.
        var lines = (_input.Text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var row = Math.Clamp(_input.CurrentRow, 0, lines.Length - 1);
        var line = lines[row];
        var col = Math.Clamp(_input.CurrentColumn, 0, line.Length);
        var left = line[..col];
        var rest = line[col..];

        if (ReplCompletion.CompleteLeft(left, _assist) is not { } outcome)
            return;

        // Surface the choices in the history pane so the user can disambiguate.
        if (outcome.Candidates.Count > 1)
            AppendOutputLine(string.Join("   ", outcome.Candidates));

        lines[row] = outcome.NewLeft + rest;
        _input.Text = string.Join("\n", lines);
        // Leave the cursor at the end of the completed token, not the end of the line
        // (InsertionPoint is a column/row Point: X = column, Y = row).
        _input.InsertionPoint = new Point(outcome.NewLeft.Length, row);
    }

    private void SetInputText(string value)
    {
        _input.Text = value;
        _input.MoveEnd();
    }

    private void CompletePendingRead(TaskCompletionSource<string?>? expected, string? value)
    {
        TaskCompletionSource<string?>? snapshot;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            if (_pendingRead is null) return;
            if (expected is not null && !ReferenceEquals(_pendingRead, expected)) return;
            snapshot = _pendingRead;
            registration = _pendingReadCancellation;
            _pendingRead = null;
            _pendingReadCancellation = default;
        }
        registration.Dispose();
        snapshot!.TrySetResult(value);
    }

    /// <summary>Test-only accessor for the prompt label text.</summary>
    internal string CurrentPromptPrefix => _promptLabel.Text;

    /// <summary>Test-only accessor for the input field text. The setter parks the caret at the end,
    /// mirroring real typing (so Tab-completion of the trailing token resolves as a user would expect).</summary>
    internal string CurrentInput
    {
        // TextView normalizes line endings to "\r\n" on the Text round-trip; tests assert on the
        // logical content (single line or "\n"-joined), so strip the inserted CRs.
        get => _input.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        set
        {
            _input.Text = value;
            _input.MoveEnd();
        }
    }

    /// <summary>Test-only accessor for the history text.</summary>
    // Terminal.Gui's TextView normalizes line endings to "\r\n" on the Text round-trip  —
    // tests assert on the logical content we appended ("\n"), so strip the inserted CRs.
    internal string CurrentHistory => _history.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Test-only: simulate a key press on the input field.</summary>
    internal void RaiseKeyDown(Key key) => OnInputKeyDown(this, key);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes Add()-ed subviews; its
            // IsDisposed guard makes these explicit calls idempotent no-ops, so disposing
            // here keeps the owned fields released even if a future refactor stops Add()-ing them.
            _pendingReadCancellation.Dispose();
            _history.Dispose();
            _promptLabel.Dispose();
            _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
