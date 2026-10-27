using System.Drawing;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// REPL pane of the fidelity layout: borderless transcript (read-only) on top, then the
/// one-row spinner line, the top separator (carrying the recipient task's name), the prompt
/// (marker + soft-wrapping, auto-growing multi-line input) and the bottom separator — the
/// vertical order of the reference captures (PLAN C1/C5/C6/C7).
/// <see cref="ReadLineAsync"/> bridges the event-driven UI to the synchronous-blocking
/// <see cref="System.IO.TextReader.ReadLine"/> contract used by <see cref="Orkeon.Cli.Abstractions.Console.IConsoleAdapter"/>.
/// </summary>
/// <remarks>
/// The prompt PREFIX contract is unchanged: the runner still writes <c>scripted&gt; </c>
/// and the console adapter still routes it here by heuristic — only the RENDERING maps
/// it to the prompt glyph, so the plain (non-TUI) mode stays byte-exact (PLAN §2.1).
/// </remarks>
public sealed class ReplPaneView : View, ITaskListNavigator
{
    private readonly ThreadScreens _screens;
    private readonly AppendCoalescer<TranscriptWrite> _transcriptWrites;

    // A write to a thread's screen; a null Text is a clear of that screen.
    private readonly record struct TranscriptWrite(string? Thread, string? Text, bool Attention = false);
    private readonly Label _promptLabel;
    private readonly Label _placeholder;
    private readonly MouseClipboardTextView _input;
    private readonly StatusLineView _statusLine;
    private readonly RuleChipView _ruleChip;
    private readonly RuleChipView _bottomRule;
    private readonly IUiDispatcher _dispatcher;
    private readonly GlyphSet _glyphs;

    // Rows reserved between the transcript and the prompt: spinner line + top separator.
    private const int BandRows = 2;

    // Rows under the prompt: the bottom separator.
    private const int BelowRows = 1;

    // Cap on how many rows the multi-line input may grow to before it scrolls internally,
    // so a long paste / many Shift+Enter lines never swallow the whole REPL pane.
    private const int MaxInputRows = 8;
    private int _inputRows = 1;
    private readonly Lock _gate = new();
    private TaskCompletionSource<string?>? _pendingRead;
    private CancellationTokenRegistration _pendingReadCancellation;

    // Type-ahead: lines submitted while no ReadLineAsync was outstanding (e.g. typed during
    // the runner's startup script load). Delivered FIFO to subsequent reads — see DeliverOrBuffer.
    private readonly Queue<string> _typeAhead = new();

    // Field / History / List: the submitted entries, the draft and the tasks list's selection
    // and recipient live in the pure machine; this view translates keys and applies its actions.
    private readonly InputNavigationStateMachine _navigation = new();
    private AgentsPaneView? _tasks;
    private TuiIntegration? _integration;

    // True while this view writes the field itself (history recall, restore, clear, submit):
    // ContentsChanged then reports no user edit to the machine.
    private bool _applyingNavigation;
    private string _basePlaceholder = PlaceholderText;

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
        _glyphs = GlyphSet.Resolve(options.Glyphs, Hosting.TerminalUtf8.IsUtf8(options.Terminal));
        // A bare View defaults to CanFocus=false; the FrameView this pane used to be
        // set it to true. Without it the focus chain cannot ENTER the pane, the
        // startup _input.SetFocus() silently no-ops, and nothing typed ever lands —
        // found live on the first user launch (capture_orkeon.png).
        CanFocus = true;
        SetScheme(SchemeFactory.Pane());

        // Line-based, bounded transcript (issue #196): an append wraps only the lines it adds, the
        // head is trimmed past ReplTranscriptCapacity lines, and writes are coalesced into one UI
        // dispatch per burst. Display-only soft-wrap when enabled: the model keeps the source lines,
        // so copy/paste returns the text with no injected line break; the horizontal scrollbar shows
        // only while the wrap is off.
        _screens = new ThreadScreens(options.ReplTranscriptCapacity, screen =>
        {
            screen.X = 0;
            screen.Y = 0;
            screen.Width = Dim.Fill();
            screen.Height = Dim.Fill(1 + BandRows + BelowRows);
            screen.WordWrap = options.ReplWordWrap;
            // Read-only display only — kept out of the focus cycle so Tab and the initial
            // focus go straight to the prompt input field. Mouse selection still works
            // (Terminal.Gui v2 raises OnMouseEvent on non-focusable views), and auto-copy
            // hooks via TranscriptView regardless of focus state.
            screen.CanFocus = false;
            screen.SetScheme(SchemeFactory.Pane());
        });
        _transcriptWrites = new AppendCoalescer<TranscriptWrite>(dispatcher, ApplyTranscriptWrites);

        // The two bands of the interactive cluster, in the captures' order: status
        // line, then the rule carrying the right-aligned context chip, then the prompt.
        _statusLine = new StatusLineView(options, dispatcher, TimeProvider.System)
        {
            X = 0,
            Y = Pos.AnchorEnd(1 + BandRows + BelowRows),
            Width = Dim.Fill(),
        };
        _ruleChip = new RuleChipView(options, dispatcher)
        {
            X = 0,
            Y = Pos.AnchorEnd(2 + BelowRows),
            Width = Dim.Fill(),
        };
        _bottomRule = new RuleChipView(options, dispatcher)
        {
            X = 0,
            Y = Pos.AnchorEnd(BelowRows),
            Width = Dim.Fill(),
        };

        _promptLabel = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1 + BelowRows),
            Height = 1,
            Text = $"{_glyphs.Prompt} ",
        };
        _promptLabel.SetScheme(SchemeFactory.Uniform(SchemeFactory.Attr(PaletteRole.Accent, bold: true)));
        _input = new MouseClipboardTextView
        {
            X = Pos.Right(_promptLabel),
            Y = Pos.AnchorEnd(1 + BelowRows),
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
        // Ctrl+A / Ctrl+C-with-selection are consumed by the view before KeyDown: they still disarm Esc-Esc.
        _input.KeyConsumed += (_, _) => _escapeArmed = false;
        // Grow / shrink the input box (and the history above it) whenever the line count changes
        // — Shift+Enter, paste, history recall, or a backspace that merges two lines.
        _input.ContentsChanged += (_, _) =>
        {
            SyncInputHeight();
            SyncPlaceholder();
            if (_applyingNavigation) return;
            // Text that lands in the field while the list holds the keys (a paste path the host
            // missed, a writer of its own) ends the list first: the field keeps what it now shows.
            if (_navigation.State == InputNavigationState.List) Apply(_navigation.OnFieldFocused());
            _navigation.OnTextEdited(CurrentInput);
        };
        // The list state follows the real focus: the field taking it back by any way the machine did
        // not order (a mouse click, a focus move) leaves the list with the field's text and caret as
        // they are now, so a later ↑ or Esc never restores a stale draft over them.
        _input.HasFocusChanged += (_, _) =>
        {
            if (_input.HasFocus && _navigation.State == InputNavigationState.List)
                Apply(_navigation.OnFieldFocused());
        };

        // Simulated placeholder (Terminal.Gui exposes none on TextView): a dim label
        // OVER the input, shown only while the draft is empty. It is display chrome —
        // it can never leak into ReadLineAsync because it lives in a different view.
        _placeholder = new Label
        {
            X = Pos.Right(_promptLabel),
            Y = Pos.AnchorEnd(1 + BelowRows),
            Height = 1,
            Text = PlaceholderText,
            CanFocus = false,
        };
        _placeholder.SetScheme(SchemeFactory.Uniform(SchemeFactory.Attr(PaletteRole.Dim, italic: true)));

        Add(_screens.Main, _statusLine, _ruleChip, _promptLabel, _input, _placeholder, _bottomRule);

        // Force initial focus on the input field once the view is laid out, otherwise
        // Terminal.Gui defaults to the first focusable child (which would skip past
        // the input if any sibling pane is focusable too).
        Initialized += (_, _) => _input.SetFocus();
    }

    /// <summary>Emitted on every key down on the input field. Used by the console adapter's <c>ReadKey</c>.</summary>
    public event EventHandler<KeyCapturedEventArgs>? KeyCaptured;

    /// <summary>Raised by the second consecutive Esc.</summary>
    public event EventHandler? InterruptRequested;

    /// <summary>
    /// The editable multi-line input field. Exposed so the global right-click
    /// paste handler (<see cref="MouseClipboardBehavior.AttachRightClickPaste(Func{bool})"/>)
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
        _transcriptWrites.Post(new TranscriptWrite(ConsoleThread.Current, text));
    }

    // UI thread: applies a burst of transcript writes in posting order, each to its thread's screen
    // (a null Text is a clear of that screen). A view follows the tail on its own while it sticks to
    // the end; a user who scrolled up stays put.
    private void ApplyTranscriptWrites(IReadOnlyList<TranscriptWrite> writes)
    {
        var pending = new Dictionary<TranscriptView, System.Text.StringBuilder>();
        foreach (var write in writes)
        {
            var screen = _screens.For(write.Thread);
            if (write.Text is null)
            {
                if (pending.TryGetValue(screen, out var cleared)) cleared.Clear();
                screen.Clear();
                continue;
            }
            Enqueue(screen, write.Text);
            if (write.Attention && !ReferenceEquals(screen, _screens.Displayed))
                Enqueue(_screens.Displayed, write.Text);
        }
        foreach (var (screen, builder) in pending)
            if (builder.Length > 0) screen.Append(builder.ToString());

        void Enqueue(TranscriptView target, string text)
        {
            if (!pending.TryGetValue(target, out var builder))
                pending[target] = builder = new System.Text.StringBuilder();
            builder.Append(text);
        }
    }

    public void AppendOutputLine(string text) => AppendOutput((text ?? string.Empty) + "\n");

    /// <summary>Writes <paramref name="text"/> as a line of <paramref name="thread"/>'s screen (null is main).</summary>
    internal void AppendThreadLine(string? thread, string text)
        => _transcriptWrites.Post(new TranscriptWrite(thread, (text ?? string.Empty) + "\n"));

    /// <summary>Writes an attention line on the displayed screen and in <paramref name="thread"/>'s own, once when they are the same.</summary>
    internal void AppendAttentionLine(string? thread, string text)
        => _transcriptWrites.Post(new TranscriptWrite(thread, (text ?? string.Empty) + "\n", Attention: true));

    /// <summary>
    /// Receives the runner's prompt prefix (heuristically routed by the console adapter)
    /// and renders the fidelity marker instead. The RUNNER's string is untouched — plain
    /// mode keeps printing <c>scripted&gt; </c> byte-exact; only this pane draws <c>❯</c>.
    /// </summary>
    public void SetPromptPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        _dispatcher.Invoke(() => _promptLabel.Text = $"{_glyphs.Prompt} ");
    }

    /// <summary>
    /// Placeholder shown while the draft is empty (contextual, e.g. a target hint). A task chosen
    /// as recipient in the tasks list overrides it with <c>Message @task…</c> until <c>main</c> is
    /// selected again.
    /// </summary>
    public void SetPlaceholder(string text)
    {
        _dispatcher.Invoke(() =>
        {
            _basePlaceholder = text ?? string.Empty;
            SyncRecipientDisplay();
        });
    }

    /// <summary>The prompt's key-interpretation state (Field, History, List) and its recipient.</summary>
    internal InputNavigationStateMachine Navigation => _navigation;

    /// <summary>Raised after a key or a rows change moved the state or the recipient (the hint bar refreshes).</summary>
    internal event EventHandler? NavigationChanged;

    /// <summary>Makes <paramref name="tasks"/> the list ↓ reaches from the field; its keys come back here.</summary>
    internal void AttachTaskList(AgentsPaneView tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks = tasks;
        tasks.Navigator = this;
    }

    /// <summary>Gives the view the host's delegates (<see cref="TuiIntegration.SendToTask"/>), read at each Enter.</summary>
    internal void BindIntegration(TuiIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        _integration = integration;
    }

    /// <summary>The placeholder the field shows for the current recipient.</summary>
    internal static string RecipientPlaceholder(string? recipientName, string basePlaceholder, GlyphSet glyphs)
        => recipientName is null ? basePlaceholder : $"Message @{recipientName}{glyphs.Ellipsis}";

    private void SyncRecipientDisplay()
    {
        _placeholder.Text = RecipientPlaceholder(_navigation.RecipientName, _basePlaceholder, _glyphs);
        _ruleChip.SetLabel(_navigation.RecipientName);
        SyncPlaceholder();
        ShowRecipientScreen();
    }

    private void ShowRecipientScreen()
    {
        var previous = _screens.Displayed;
        if (!_screens.Show(_navigation.RecipientTicket)) return;
        Remove(previous);
        var screen = _screens.Displayed;
        screen.Height = Dim.Fill(_inputRows + BandRows + BelowRows);
        Add(screen);
        screen.SetRelativeLayout(GetContentSize());
        screen.MoveEnd();
    }

    private void SyncPlaceholder()
        => _placeholder.Visible = string.IsNullOrEmpty(_input.Text);

    private const string PlaceholderText = "Message… (/ for commands, @ for files)";

    /// <summary>The status line band (bound by the host to the runner + integration).</summary>
    public StatusLineView StatusLine => _statusLine;

    /// <summary>The top separator: it names the recipient task while it is not <c>main</c>.</summary>
    public RuleChipView RuleChip => _ruleChip;

    /// <summary>The bottom separator, under the prompt.</summary>
    internal RuleChipView BottomRule => _bottomRule;

    /// <summary>The glyph set this pane resolved (the transcript lines written about it use the same).</summary>
    internal GlyphSet Glyphs => _glyphs;

    public void Clear()
    {
        // Through the coalescer, so a clear stays ordered with the writes posted around it.
        _transcriptWrites.Post(new TranscriptWrite(ConsoleThread.Current, null));
    }

    public void FocusPrompt()
    {
        _dispatcher.Invoke(() => _input.SetFocus());
    }

    /// <summary>
    /// Toggles word wrap on the history pane: enabling it hides the horizontal scrollbar and wraps
    /// long lines (display-only, copy/paste preserved); disabling it restores the horizontal scrollbar.
    /// </summary>
    public void ToggleWrap() => _dispatcher.Invoke(_screens.ToggleWordWrap);

    /// <summary>Current wrap state (true ⇒ wrapping, no horizontal scrollbar).</summary>
    public bool IsWordWrapEnabled => _screens.WordWrap;

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
    /// Only one outstanding ReadLineAsync at a time. A line submitted while no read was
    /// outstanding (type-ahead) is delivered first, FIFO.
    /// </summary>
    public Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_pendingRead is not null)
                throw new InvalidOperationException("A ReadLineAsync is already in progress on this REPL pane.");
            if (_typeAhead.Count > 0)
                return Task.FromResult<string?>(_typeAhead.Dequeue());
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

        // Escape never quits the app from the prompt: the first Escape clears the draft and arms,
        // a second consecutive Escape requests an interrupt (Esc-Esc). Any other key disarms the sequence.
        if (bare == KeyCode.Esc)
        {
            key.Handled = true;
            if (_escapeArmed)
            {
                _escapeArmed = false;
                InterruptRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            ClearInput();
            _escapeArmed = true;
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
                Navigate(InputNavigationKey.Enter, key);
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
    /// Routes ↑/↓, Home/End and Ctrl+Home/Ctrl+End through the navigation machine, and Tab to
    /// completion. A key the machine leaves (↑/↓ inside a multi-line draft, Home/End) keeps its
    /// native TextView effect.
    /// </summary>
    private void HandleNavigationKey(Key key)
    {
        if (key.KeyCode == KeyCode.Tab)
        {
            TryComplete();
            key.Handled = true;
            return;
        }
        if (MapFieldKey(key.KeyCode) is { } navigationKey)
            Navigate(navigationKey, key);
    }

    private static InputNavigationKey? MapFieldKey(KeyCode code) => code switch
    {
        KeyCode.CursorUp => InputNavigationKey.Up,
        KeyCode.CursorDown => InputNavigationKey.Down,
        KeyCode.Home => InputNavigationKey.Home,
        KeyCode.End => InputNavigationKey.End,
        KeyCode.Home | KeyCode.CtrlMask => InputNavigationKey.CtrlHome,
        KeyCode.End | KeyCode.CtrlMask => InputNavigationKey.CtrlEnd,
        _ => null,
    };

    private void Navigate(InputNavigationKey navigationKey, Key key, string? character = null)
    {
        var action = _navigation.Handle(navigationKey, BuildContext(character));
        if (action.Handled) key.Handled = true;
        Apply(action);
    }

    private InputNavigationContext BuildContext(string? character = null)
    {
        var text = CurrentInput;
        var (first, last) = CaretRowBounds(text, _input.CurrentColumn, _input.CurrentRow, _input.Lines, _input.Viewport.Width);
        return new InputNavigationContext
        {
            Text = text,
            Caret = new InputCaret(_input.CurrentColumn, _input.CurrentRow),
            CaretOnFirstRow = first,
            CaretOnLastRow = last,
            Rows = _tasks?.Rows ?? [],
            ModalFocused = ModalFocus.IsActive(),
            CanSendToTask = _integration?.SendToTask is not null,
            Character = character,
        };
    }

    /// <summary>
    /// Whether the caret sits on the first / last VISUAL row of the soft-wrapped field. When
    /// Terminal.Gui's wrapped model is live (<paramref name="fieldRows"/> exceeds the logical line
    /// count) the caret row is already a visual row; outside a draw cycle the field reports logical
    /// coordinates, so the visual row is estimated from <paramref name="width"/> the way
    /// <see cref="CountWrappedRows"/> counts them.
    /// </summary>
    internal static (bool First, bool Last) CaretRowBounds(string? text, int column, int row, int fieldRows, int width)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (fieldRows > lines.Length)
            return (row <= 0, row >= fieldRows - 1);

        var logicalRow = Math.Clamp(row, 0, lines.Length - 1);
        if (width <= 0)
            return (logicalRow == 0, logicalRow >= lines.Length - 1);

        var before = 0;
        for (var i = 0; i < logicalRow; i++)
            before += Math.Max(1, (lines[i].Length + width - 1) / width);
        var rowsOfLine = Math.Max(1, (lines[logicalRow].Length + width - 1) / width);
        var caret = before + Math.Min(Math.Max(0, column) / width, rowsOfLine - 1);
        var total = CountWrappedRows(text, width);
        return (caret == 0, caret >= total - 1);
    }

    /// <summary>Applies one machine instruction to the field and the tasks list.</summary>
    private void Apply(InputNavigationAction action)
    {
        switch (action.Kind)
        {
            case InputNavigationActionKind.SetText:
                SetInputText(action.Text ?? string.Empty);
                break;
            case InputNavigationActionKind.Submit:
                Deliver(action.Text ?? string.Empty);
                break;
            case InputNavigationActionKind.SendToTask:
                SendToTask(action.Ticket!, action.Text ?? string.Empty);
                break;
            case InputNavigationActionKind.RecipientUnsupported:
                AppendThreadLine(_screens.DisplayedThread, $"Sending to a task is not supported yet: nothing was sent to @{_navigation.RecipientName}, the text stays in the field.");
                break;
            case InputNavigationActionKind.FocusList:
                _tasks?.ShowSelection(action.Ticket);
                break;
            case InputNavigationActionKind.MoveListSelection:
                _tasks?.SelectTicket(action.Ticket);
                break;
            case InputNavigationActionKind.OpenTask:
                _tasks?.ActivateTicket(action.Ticket!);
                break;
            case InputNavigationActionKind.FocusField:
                RestoreField(action);
                break;
        }
        SyncRecipientDisplay();
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreField(InputNavigationAction action)
    {
        _tasks?.HideSelection();
        _input.SetFocus();
        // No text in the action: the field keeps what it shows (the focus came back on its own).
        if (action.Text is { } text && !string.Equals(CurrentInput, text, StringComparison.Ordinal))
            SetInputText(text);
        if (action.Caret is { } caret)
            _input.InsertionPoint = new Point(caret.Column, caret.Row);
        if (!string.IsNullOrEmpty(action.Insert))
            _input.InsertText(action.Insert);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing sender keeps the text in the field, never crashes key dispatch.")]
    private void SendToTask(string ticket, string text)
    {
        bool delivered;
        try
        {
            delivered = _integration?.SendToTask?.Invoke(ticket, text) == true;
        }
        catch
        {
            delivered = false;
        }
        if (!delivered)
        {
            AppendOutputLine($"Could not deliver to @{_navigation.RecipientName}: the text stays in the field.");
            return;
        }
        _navigation.RecordSubmitted(text);
        SetInputText(string.Empty);
        EchoSubmittedLine(text);
    }

    /// <summary>
    /// Pastes the clipboard into the field — the one path of <c>Ctrl+V</c> and the right-click. From
    /// the tasks list it first returns to the field (text and caret restored, as <c>Esc</c> does),
    /// so the paste lands where the caret was and nothing restores a saved text over it afterwards.
    /// </summary>
    /// <returns>True when something was pasted.</returns>
    internal bool PasteClipboard()
    {
        if (_navigation.State == InputNavigationState.List)
            Apply(_navigation.Handle(InputNavigationKey.Paste, BuildContext()));
        return PaneClipboard.PasteInto(_input);
    }

    /// <summary>
    /// Copies the transcript's mouse selection to the clipboard: the transcript takes no focus, so the
    /// host's copy key falls back to it when the focused view has no selection of its own.
    /// </summary>
    /// <returns>True when the transcript had a selection.</returns>
    internal bool CopyTranscriptSelection() => PaneClipboard.CopySelection(_screens.Displayed);

    bool ITaskListNavigator.HandleListKey(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_navigation.State != InputNavigationState.List) return false;
        var bare = PaneClipboard.BareKey(key);
        InputNavigationKey? navigationKey = key.KeyCode switch
        {
            KeyCode.CursorUp => InputNavigationKey.Up,
            KeyCode.CursorDown => InputNavigationKey.Down,
            KeyCode.Home => InputNavigationKey.Home,
            KeyCode.End => InputNavigationKey.End,
            KeyCode.Enter => InputNavigationKey.Enter,
            KeyCode.Tab => InputNavigationKey.Tab,
            _ when bare == KeyCode.Esc => InputNavigationKey.Escape,
            _ => null,
        };
        // The Esc that leaves the list arms the pair, as the field's first Esc does: Esc Esc from
        // the list interrupts the prompt's turn. Any other key of the list disarms it.
        _escapeArmed = navigationKey == InputNavigationKey.Escape;
        string? character = null;
        if (navigationKey is null && PrintableText(key) is { } printable)
        {
            navigationKey = InputNavigationKey.Printable;
            character = printable;
        }
        if (navigationKey is null) return false;
        Navigate(navigationKey.Value, key, character);
        return key.Handled;
    }

    void ITaskListNavigator.EnterList()
    {
        if (_navigation.State == InputNavigationState.List) return;
        _escapeArmed = false;
        Apply(_navigation.Handle(InputNavigationKey.ToggleList, BuildContext()));
    }

    void ITaskListNavigator.SelectRow(string? ticket)
    {
        var action = _navigation.SelectRow(ticket, BuildContext());
        if (action.Kind == InputNavigationActionKind.None) return;
        _escapeArmed = false;
        Apply(action);
    }

    void ITaskListNavigator.LeaveList()
    {
        if (_navigation.State != InputNavigationState.List)
        {
            _tasks?.HideSelection();
            return;
        }
        Apply(_navigation.Handle(InputNavigationKey.Escape, BuildContext()));
    }

    string? ITaskListNavigator.RecipientTicket => _navigation.RecipientTicket;

    void ITaskListNavigator.RowsRefreshed(IReadOnlyList<AgentRowInfo> rows)
    {
        // Polled at 1 Hz: apply (and refresh the hint bar) only when something moved.
        var recipient = _navigation.RecipientTicket;
        var action = _navigation.OnRowsChanged(rows);
        if (action.DepartedRecipient is { } departed)
            AppendOutputLine(DepartedRecipientLine(_glyphs, departed));
        if (action.Kind != InputNavigationActionKind.None || !string.Equals(recipient, _navigation.RecipientTicket, StringComparison.Ordinal))
            Apply(action);
    }

    /// <summary><c>@alpha finished · recipient is main</c>: the recipient task left the list.</summary>
    internal static string DepartedRecipientLine(GlyphSet glyphs, string name)
        => $"@{name} finished {glyphs.Dot} recipient is main";

    /// <summary>The text a key types, or null for a control, modified or non-character key.</summary>
    private static string? PrintableText(Key key)
    {
        if (key.IsCtrl || key.IsAlt) return null;
        var rune = key.AsRune;
        if (rune.Value < 0x20 || System.Text.Rune.IsControl(rune)) return null;
        return rune.ToString();
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

        _screens.Displayed.Height = Dim.Fill(rows + BandRows + BelowRows);
        _statusLine.Y = Pos.AnchorEnd(rows + BandRows + BelowRows);
        _ruleChip.Y = Pos.AnchorEnd(rows + 1 + BelowRows);
        _promptLabel.Y = Pos.AnchorEnd(rows + BelowRows);
        _placeholder.Y = Pos.AnchorEnd(rows + BelowRows);
        _input.Y = Pos.AnchorEnd(rows + BelowRows);
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
        SetInputText(string.Empty);
        _navigation.Reset();
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enter to <c>main</c>: the machine has recorded the (possibly multi-line) entry; clear the
    /// field, echo the entry and complete the pending read. The context carries the text with its
    /// line endings normalised to <c>\n</c>, so downstream consumers never see Terminal.Gui's
    /// internal <c>\r\n</c>.
    /// </summary>
    private void Deliver(string text)
    {
        SetInputText(string.Empty);
        if (text.Trim().Length > 0)
            EchoSubmittedLine(text);
        DeliverOrBuffer(text);
    }

    /// <summary>
    /// Hands the submitted line to the outstanding <see cref="ReadLineAsync"/>, or buffers it
    /// when none is outstanding — e.g. a line typed while the runner is still loading its
    /// scripts, before its first read. The input field is live from the first frame, so
    /// dropping here would silently lose input that was already echoed to the transcript;
    /// buffering gives the TUI the same type-ahead semantics as a plain terminal.
    /// </summary>
    private void DeliverOrBuffer(string text)
    {
        TaskCompletionSource<string?>? snapshot;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            if (_pendingRead is null)
            {
                _typeAhead.Enqueue(text);
                return;
            }
            snapshot = _pendingRead;
            registration = _pendingReadCancellation;
            _pendingRead = null;
            _pendingReadCancellation = default;
        }
        registration.Dispose();
        snapshot.TrySetResult(text);
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

    /// <summary>Writes the field without reporting a user edit to the navigation machine.</summary>
    private void SetInputText(string value)
    {
        _applyingNavigation = true;
        try
        {
            _input.Text = value;
            _input.MoveEnd();
        }
        finally
        {
            _applyingNavigation = false;
        }
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

    /// <summary>Test-only accessor for the placeholder text.</summary>
    internal string CurrentPlaceholder => _placeholder.Text;

    /// <summary>Test-only accessor for the history text.</summary>
    internal string CurrentHistory => _screens.Displayed.TranscriptText;

    /// <summary>Test-only accessor for the text of a thread's screen.</summary>
    internal string ThreadText(string? thread) => _screens.For(thread).TranscriptText;

    /// <summary>The displayed screen's ticket; null for main.</summary>
    internal string? DisplayedThread => _screens.DisplayedThread;

    /// <summary>Test-only accessor for the transcript model (line count, wrap work).</summary>
    internal TranscriptBuffer Transcript => _screens.Displayed.Buffer;

    /// <summary>Test-only accessor for the transcript view (its viewport and follow state).</summary>
    internal TranscriptView HistoryView => _screens.Displayed;

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
            _screens.DisposeScreens();
            _statusLine.Dispose();
            _ruleChip.Dispose();
            _bottomRule.Dispose();
            _promptLabel.Dispose();
            _placeholder.Dispose();
            _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
