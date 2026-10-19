using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// These run live under Terminal.Gui 2.4.4 (the 2.1.0 ModuleInitializer crash that forced
// xUnit skips — TUI-12 — is fixed; verified by un-skipping under 2.4.4).
[Collection(TerminalGuiApplicationCollection.Name)]
public class ReplPaneViewTests
{
    private static readonly string[] ExitCommand = ["exit"];
    private static readonly string[] ProgramPath = ["src/Program.cs"];
    private static readonly string[] CommitCompactCommands = ["commit", "compact"];

    private static ReplPaneView CreatePane()
    {
        var pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        // Give the pane a real frame and lay it out so the soft-wrapping input field has a non-zero
        // viewport width. Without it, Terminal.Gui's WordWrap model is degenerate (column tracking and
        // SelectAll collapse to 0) — an artifact of the detached test, never the running app.
        pane.Frame = new System.Drawing.Rectangle(0, 0, 100, 20);
        pane.Layout();
        return pane;
    }

    [Fact]
    public async Task ReadLineAsync_returns_text_when_Enter_pressed()
    {
        using var pane = CreatePane();
        var task = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "verify 4";
        pane.RaiseKeyDown(new Key(KeyCode.Enter));
        var result = await task;
        Assert.Equal("verify 4", result);
    }

    [Fact]
    public async Task ReadLineAsync_clears_prompt_after_submit()
    {
        using var pane = CreatePane();
        var task = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "list";
        pane.RaiseKeyDown(new Key(KeyCode.Enter));
        await task;
        Assert.Equal(string.Empty, pane.CurrentInput);
    }

    [Fact]
    public async Task A_line_submitted_before_any_read_is_delivered_to_the_next_read()
    {
        // The input field is live from the first frame, but the runner only starts reading
        // after its (slow) startup script load. A line typed in that window used to be
        // echoed to the transcript and then silently DROPPED — the live "what do we do?"
        // incident: nothing ever happened. Type-ahead must buffer it instead.
        using var pane = CreatePane();

        pane.CurrentInput = "que fait-on ?";
        pane.RaiseKeyDown(new Key(KeyCode.Enter)); // no ReadLineAsync outstanding

        var result = await pane.ReadLineAsync(CancellationToken.None);
        Assert.Equal("que fait-on ?", result);
    }

    [Fact]
    public async Task Buffered_type_ahead_lines_are_delivered_in_fifo_order()
    {
        using var pane = CreatePane();

        pane.CurrentInput = "first";
        pane.RaiseKeyDown(new Key(KeyCode.Enter));
        pane.CurrentInput = "second";
        pane.RaiseKeyDown(new Key(KeyCode.Enter));

        Assert.Equal("first", await pane.ReadLineAsync(CancellationToken.None));
        Assert.Equal("second", await pane.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_live_read_still_wins_over_the_type_ahead_buffer_for_new_lines()
    {
        // Once a read is outstanding, a submitted line goes straight to it (no buffering).
        using var pane = CreatePane();
        var task = pane.ReadLineAsync(CancellationToken.None);

        pane.CurrentInput = "direct";
        pane.RaiseKeyDown(new Key(KeyCode.Enter));

        Assert.Equal("direct", await task);
    }

    [Fact]
    public async Task ReadLineAsync_returns_null_when_canceled()
    {
        using var pane = CreatePane();
        using var cts = new CancellationTokenSource();
        var task = pane.ReadLineAsync(cts.Token);
        await cts.CancelAsync();
        var result = await task;
        Assert.Null(result);
    }

    [Fact]
    public void Pane_is_focusable_so_the_input_can_receive_focus()
    {
        // A bare View defaults to CanFocus=false; the focus chain then cannot enter the
        // pane and NOTHING typed lands anywhere — the first live launch shipped exactly
        // that (capture_orkeon.png). FrameView used to set this implicitly.
        using var pane = CreatePane();
        Assert.True(pane.CanFocus);
    }

    [Fact]
    public void SetPromptPrefix_renders_the_fidelity_marker_not_the_raw_prefix()
    {
        // PLAN §2.1: the runner keeps writing "scripted> " (plain mode stays byte-exact);
        // only the TUI rendering maps it to the prompt glyph.
        using var pane = CreatePane();
        pane.SetPromptPrefix("[claim-verifier] > ");
        Assert.Equal("❯ ", pane.CurrentPromptPrefix);
    }

    [Fact]
    public async Task Placeholder_never_leaks_into_ReadLineAsync()
    {
        // The placeholder is a separate overlay label, so the submitted draft cannot
        // contain it — pinned anyway (PLAN R2): Enter on an empty input must yield "",
        // not the placeholder text.
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.RaiseKeyDown(Terminal.Gui.Input.Key.Enter);
        var line = await read;
        Assert.Equal(string.Empty, line);
    }

    [Fact]
    public void AppendOutputLine_appends_with_newline()
    {
        using var pane = CreatePane();
        pane.AppendOutputLine("first");
        pane.AppendOutputLine("second");
        Assert.Equal("first\nsecond\n", pane.CurrentHistory);
    }

    [Fact]
    public void Clear_empties_history()
    {
        using var pane = CreatePane();
        pane.AppendOutputLine("noise");
        pane.Clear();
        Assert.Equal(string.Empty, pane.CurrentHistory);
    }

    // ── Word wrap / horizontal scrollbar toggle ───────────────────────────────────────────

    [Fact]
    public void WordWrap_is_enabled_by_default()
    {
        using var pane = CreatePane();
        Assert.True(pane.IsWordWrapEnabled);
    }

    [Fact]
    public void WordWrap_honors_the_option_when_disabled()
    {
        using var pane = new ReplPaneView(
            new TerminalGuiOptions { ReplWordWrap = false }, InlineDispatcher.Instance);
        Assert.False(pane.IsWordWrapEnabled);
    }

    [Fact]
    public void ToggleWrap_flips_the_wrap_state()
    {
        using var pane = CreatePane();
        var initial = pane.IsWordWrapEnabled;

        pane.ToggleWrap();
        Assert.Equal(!initial, pane.IsWordWrapEnabled);

        pane.ToggleWrap();
        Assert.Equal(initial, pane.IsWordWrapEnabled);
    }

    [Fact]
    public void WordWrap_does_not_mutate_the_stored_history_text()
    {
        // Guarantee the wrap is display-only: enabling it must NOT inject newlines into the
        // underlying model, so copy/paste of the history returns the original text unaltered.
        using var pane = CreatePane(); // wrap on by default
        var longLine = new string('x', 400);
        pane.AppendOutputLine(longLine);

        Assert.True(pane.IsWordWrapEnabled);
        Assert.Equal(longLine + "\n", pane.CurrentHistory);
        Assert.DoesNotContain('\n', pane.CurrentHistory.TrimEnd('\n'));
    }

    [Fact]
    public void KeyCaptured_event_fires_on_each_key_down()
    {
        using var pane = CreatePane();
        var captured = new List<KeyCode>();
        pane.KeyCaptured += (_, e) => captured.Add(e.Key.KeyCode);
        pane.RaiseKeyDown(new Key(KeyCode.A));
        pane.RaiseKeyDown(new Key(KeyCode.B));
        Assert.Equal(new[] { KeyCode.A, KeyCode.B }, captured);
    }

    [Fact]
    public void Concurrent_ReadLineAsync_throws()
    {
        using var pane = CreatePane();
        _ = pane.ReadLineAsync(CancellationToken.None);
        Action act = () => { _ = pane.ReadLineAsync(CancellationToken.None); };
        Assert.Throws<InvalidOperationException>(act);
    }

    // ── Capability 2: ↑/↓ history ─────────────────────────────────────────────────────────

    [Fact]
    public void CursorUp_recalls_last_submitted_line()
    {
        using var pane = CreatePane();
        Submit(pane, "first command");

        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));

        Assert.Equal("first command", pane.CurrentInput);
    }

    [Fact]
    public void CursorUp_then_Down_restores_the_in_progress_draft()
    {
        using var pane = CreatePane();
        Submit(pane, "older");
        pane.CurrentInput = "half-typed";

        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));   // → "older"
        Assert.Equal("older", pane.CurrentInput);
        pane.RaiseKeyDown(new Key(KeyCode.CursorDown)); // → back to the draft

        Assert.Equal("half-typed", pane.CurrentInput);
    }

    [Fact]
    public void CursorUp_walks_multiple_entries_newest_first()
    {
        using var pane = CreatePane();
        Submit(pane, "one");
        Submit(pane, "two");

        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));   // newest
        Assert.Equal("two", pane.CurrentInput);
        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));   // older
        Assert.Equal("one", pane.CurrentInput);
    }

    // ── Echo of submitted entries into the history pane ───────────────────────────────────

    [Fact]
    public void Submitting_echoes_the_entry_verbatim_into_the_history_pane()
    {
        using var pane = CreatePane();
        pane.SetPromptPrefix("scripted> ");

        Submit(pane, "verify 4");

        // Echoed without the prompt prefix — just the typed text.
        Assert.Equal("verify 4\n", pane.CurrentHistory);
    }

    [Fact]
    public void Echo_keeps_each_logical_line_of_a_multiline_entry()
    {
        using var pane = CreatePane();
        pane.SetPromptPrefix("> ");
        pane.CurrentInput = "first";
        pane.RaiseKeyDown(Key.O.WithCtrl);          // newline, no submit
        pane.Input.InsertText("second");
        pane.RaiseKeyDown(new Key(KeyCode.Enter));  // submit

        Assert.Equal("first\nsecond\n", pane.CurrentHistory);
    }

    [Fact]
    public void Blank_submission_is_not_echoed()
    {
        using var pane = CreatePane();
        pane.SetPromptPrefix("scripted> ");

        Submit(pane, "   ");

        Assert.Equal(string.Empty, pane.CurrentHistory);
    }

    // ── Capabilities 3/4: Tab completion ──────────────────────────────────────────────────

    [Fact]
    public void Tab_completes_a_unique_slash_command()
    {
        using var pane = CreatePane();
        pane.ConfigureCompletion(new FakeAssist(commands: ExitCommand));
        pane.CurrentInput = "/ex";

        pane.RaiseKeyDown(new Key(KeyCode.Tab));

        Assert.Equal("/exit", pane.CurrentInput);
    }

    [Fact]
    public void Tab_completes_an_at_path_token()
    {
        using var pane = CreatePane();
        pane.ConfigureCompletion(new FakeAssist(paths: ProgramPath));
        pane.CurrentInput = "explain @src/Pro";

        pane.RaiseKeyDown(new Key(KeyCode.Tab));

        Assert.Equal("explain @src/Program.cs", pane.CurrentInput);
    }

    [Fact]
    public void Tab_on_ambiguous_command_inserts_the_longest_common_prefix()
    {
        using var pane = CreatePane();
        pane.ConfigureCompletion(new FakeAssist(commands: CommitCompactCommands));
        pane.CurrentInput = "/co";

        pane.RaiseKeyDown(new Key(KeyCode.Tab));

        Assert.Equal("/com", pane.CurrentInput);
    }

    [Fact]
    public void Tab_is_inert_without_a_completion_source()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "/ex";

        pane.RaiseKeyDown(new Key(KeyCode.Tab));

        Assert.Equal("/ex", pane.CurrentInput);
    }

    // ── Clipboard shortcuts on the active input field ─────────────────────────────────────

    [Fact]
    public void CtrlA_selects_all_input_text()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "hello world";

        pane.RaiseKeyDown(Key.A.WithCtrl);

        Assert.Equal("hello world", pane.Input.SelectedText);
    }

    [Fact]
    public void CtrlA_marks_the_key_handled()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "data";

        var key = Key.A.WithCtrl;
        pane.RaiseKeyDown(key);

        Assert.True(key.Handled);
    }

    [Fact]
    public void CtrlV_is_handled_and_does_not_submit()
    {
        // The OS clipboard may be empty/unavailable on CI, but the key must be consumed by the
        // input (never bubbles up to a quit/cancel handler) and must not complete a pending read.
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);

        var key = Key.V.WithCtrl;
        pane.RaiseKeyDown(key);

        Assert.True(key.Handled);
        Assert.False(read.IsCompleted);
    }

    [Fact]
    public void CtrlC_is_handled_without_submitting()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "keep me";
        var read = pane.ReadLineAsync(CancellationToken.None);

        var key = Key.C.WithCtrl;
        pane.RaiseKeyDown(key);

        Assert.True(key.Handled);
        Assert.False(read.IsCompleted);
        Assert.Equal("keep me", pane.CurrentInput);
    }

    // ── Multi-line input (Shift+Enter / Alt+Enter) ───────────────────────────────────────

    [Fact]
    public void ShiftEnter_inserts_a_newline_without_submitting()
    {
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "line one";

        pane.RaiseKeyDown(new Key(KeyCode.Enter | KeyCode.ShiftMask));

        Assert.False(read.IsCompleted);
        Assert.Equal(2, pane.Input.Lines);
        Assert.Equal("line one\n", pane.CurrentInput);
    }

    [Fact]
    public void CtrlO_inserts_a_newline_without_submitting()
    {
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "x";

        pane.RaiseKeyDown(Key.O.WithCtrl);

        Assert.False(read.IsCompleted);
        Assert.Equal(2, pane.Input.Lines);
    }

    [Fact]
    public void AltEnter_is_not_overridden()
    {
        // Alt+Enter must pass through to the terminal/OS — the prompt must neither submit nor consume it.
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "draft";

        var key = new Key(KeyCode.Enter | KeyCode.AltMask);
        pane.RaiseKeyDown(key);

        Assert.False(key.Handled);
        Assert.False(read.IsCompleted);
        Assert.Equal("draft", pane.CurrentInput);
    }

    [Fact]
    public async Task Enter_submits_multiline_text_joined_with_newlines()
    {
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "first";

        pane.RaiseKeyDown(Key.O.WithCtrl);        // Ctrl+O newline, no submit
        pane.Input.InsertText("second");
        pane.RaiseKeyDown(new Key(KeyCode.Enter)); // submit

        var result = await read;
        Assert.Equal("first\nsecond", result);
    }

    // ── Double-Escape clears the draft (never quits) ──────────────────────────────────────

    [Fact]
    [Trait("BR", "CH-02")]
    public void DoubleEscape_clears_the_current_draft()
    {
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "half-typed command";

        pane.RaiseKeyDown(new Key(KeyCode.Esc));
        Assert.Equal(string.Empty, pane.CurrentInput);
        pane.RaiseKeyDown(new Key(KeyCode.Esc));

        Assert.Equal(string.Empty, pane.CurrentInput);
        Assert.False(read.IsCompleted); // clearing must not complete the pending read
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void SingleEscape_clears_the_draft_and_raises_no_interrupt()
    {
        using var pane = CreatePane();
        var raised = 0;
        pane.InterruptRequested += (_, _) => raised++;
        pane.CurrentInput = "keep me";

        var key = new Key(KeyCode.Esc);
        pane.RaiseKeyDown(key);

        Assert.True(key.Handled);            // swallowed ⇒ never reaches the app quit binding
        Assert.Equal(string.Empty, pane.CurrentInput);
        Assert.Equal(0, raised);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void First_escape_clears_the_draft_and_the_second_raises_InterruptRequested()
    {
        using var pane = CreatePane();
        var raised = 0;
        pane.InterruptRequested += (_, _) => raised++;
        pane.CurrentInput = "draft";

        pane.RaiseKeyDown(new Key(KeyCode.Esc));
        Assert.Equal(string.Empty, pane.CurrentInput);
        Assert.Equal(0, raised);
        pane.RaiseKeyDown(new Key(KeyCode.Esc));

        Assert.Equal(1, raised);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void Escape_pair_on_an_empty_prompt_raises_InterruptRequested()
    {
        using var pane = CreatePane();
        var raised = 0;
        pane.InterruptRequested += (_, _) => raised++;

        pane.RaiseKeyDown(new Key(KeyCode.Esc));
        pane.RaiseKeyDown(new Key(KeyCode.Esc));

        Assert.Equal(1, raised);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void Escape_sequence_resets_after_another_key()
    {
        using var pane = CreatePane();
        var raised = 0;
        pane.InterruptRequested += (_, _) => raised++;

        pane.RaiseKeyDown(new Key(KeyCode.Esc));      // arm
        pane.RaiseKeyDown(new Key(KeyCode.CursorUp)); // unrelated key disarms
        pane.RaiseKeyDown(new Key(KeyCode.Esc));      // arms again, no interrupt
        Assert.Equal(0, raised);

        pane.RaiseKeyDown(new Key(KeyCode.Esc));      // consecutive pair completes
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CursorUp_in_a_multiline_draft_moves_the_caret_instead_of_recalling_history()
    {
        using var pane = CreatePane();
        Submit(pane, "old command");          // populate history
        pane.CurrentInput = "draft line one";
        pane.RaiseKeyDown(new Key(KeyCode.Enter | KeyCode.ShiftMask));
        pane.Input.InsertText("draft line two"); // caret now on the last row

        pane.RaiseKeyDown(new Key(KeyCode.CursorUp)); // not on first row ⇒ caret move, not recall

        Assert.Equal("draft line one\ndraft line two", pane.CurrentInput);
    }

    // ── Field / History / List navigation (the three-state machine, end to end) ───────────

    private sealed class TaskRows
    {
        public List<AgentRowInfo> Rows { get; } =
        [
            new() { Name = "main", IsActive = true },
            new() { Name = "worker@crew", Ticket = "T-1" },
            new() { Name = "scout@crew", Ticket = "T-2" },
        ];
    }

    private static (SplitPaneToplevel Top, TaskRows Rows) CreateTopWithTasks()
    {
        var rows = new TaskRows();
        var top = new SplitPaneToplevel(new TerminalGuiOptions(), InlineDispatcher.Instance);
        top.Agents.Bind(new TuiIntegration { AgentRows = () => rows.Rows });
        // A real frame, as CreatePane gives one: the soft-wrapped input needs a viewport width.
        top.Frame = new System.Drawing.Rectangle(0, 0, 100, 30);
        top.Layout();
        return (top, rows);
    }

    [Fact]
    public void Down_from_the_field_reaches_the_list_and_up_on_its_first_row_restores_text_and_caret()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        top.Repl.CurrentInput = "draft text";
        top.Repl.Input.InsertionPoint = new System.Drawing.Point(5, 0);

        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        Assert.True(top.Agents.SelectionActive);
        Assert.Equal(0, top.Agents.SelectedIndex);

        top.Agents.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(1, top.Agents.SelectedIndex);
        top.Agents.NewKeyDownEvent(Key.CursorUp);
        top.Agents.NewKeyDownEvent(Key.CursorUp);

        Assert.False(top.Agents.SelectionActive);
        Assert.Equal("draft text", top.Repl.CurrentInput);
        Assert.Equal(5, top.Repl.Input.CurrentColumn);
    }

    [Fact]
    public void Escape_in_the_list_returns_to_the_field_and_keeps_the_selected_task_as_recipient()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        top.Repl.CurrentInput = "hello";

        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.CursorDown);
        top.Agents.NewKeyDownEvent(Key.Esc);

        Assert.False(top.Agents.SelectionActive);
        Assert.Equal("hello", top.Repl.CurrentInput);
        Assert.Equal("worker@crew", top.Repl.Navigation.RecipientName);
        Assert.Equal("Message @worker@crew…", top.Repl.CurrentPlaceholder);
        // The recipient is named on the top separator, no longer in the status bar.
        Assert.Equal("worker@crew", top.Repl.RuleChip.CurrentLabel);
        Assert.DoesNotContain("worker@crew", top.HintBar.CurrentText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, top.Repl.BottomRule.CurrentLabel);
    }

    [Fact]
    public void The_hint_bar_shows_the_lists_affordance_while_it_has_focus()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;

        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));

        Assert.Contains("↑/↓ select", top.HintBar.CurrentText, StringComparison.Ordinal);
        Assert.Contains("enter view", top.HintBar.CurrentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enter_to_a_task_without_the_port_reports_and_keeps_the_text()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        var read = top.Repl.ReadLineAsync(CancellationToken.None);
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.CursorDown);
        top.Agents.NewKeyDownEvent(Key.Esc);
        top.Repl.CurrentInput = "for the worker";

        top.Repl.RaiseKeyDown(new Key(KeyCode.Enter));

        Assert.Equal("for the worker", top.Repl.CurrentInput);
        Assert.Contains("not supported yet", top.Repl.CurrentHistory, StringComparison.Ordinal);
        Assert.False(read.IsCompleted);
        Assert.Empty(top.Repl.Navigation.History);

        // Selecting `main` again restores the default recipient: Enter submits.
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.Home);
        top.Agents.NewKeyDownEvent(Key.Esc);
        top.Repl.RaiseKeyDown(new Key(KeyCode.Enter));
        Assert.Equal("for the worker", await read);
    }

    [Fact]
    public void Enter_to_a_task_with_the_port_hands_it_the_text()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        var sent = new List<(string Ticket, string Text)>();
        top.Repl.BindIntegration(new TuiIntegration { SendToTask = (ticket, text) => { sent.Add((ticket, text)); return true; } });
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.End);
        top.Agents.NewKeyDownEvent(Key.Esc);
        top.Repl.CurrentInput = "scout this";

        top.Repl.RaiseKeyDown(new Key(KeyCode.Enter));

        Assert.Equal([("T-2", "scout this")], sent);
        Assert.Equal(string.Empty, top.Repl.CurrentInput);
        Assert.Equal(["scout this"], top.Repl.Navigation.History);
    }

    [Fact]
    public void Enter_in_the_list_opens_the_task_view_and_sends_nothing()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        string? opened = null;
        top.Agents.RowActivated += (_, e) => opened = e.Ticket;
        top.Repl.CurrentInput = "unsent";

        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.CursorDown);
        top.Agents.NewKeyDownEvent(Key.Enter);

        Assert.Equal("T-1", opened);
        Assert.True(top.Agents.SelectionActive);
        Assert.Equal("unsent", top.Repl.CurrentInput);
        Assert.Empty(top.Repl.Navigation.History);
    }

    [Fact]
    public void A_printable_key_in_the_list_returns_to_the_field_with_the_character()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        top.Repl.CurrentInput = "ab";

        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.C);

        Assert.False(top.Agents.SelectionActive);
        Assert.Equal("abc", top.Repl.CurrentInput);
    }

    [Fact]
    public void A_list_emptied_while_focused_returns_to_the_field_with_main_as_recipient()
    {
        var (top, rows) = CreateTopWithTasks();
        using var owner = top;
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.CursorDown);

        rows.Rows.Clear();
        top.Agents.Refresh();

        Assert.False(top.Agents.SelectionActive);
        Assert.Equal(InputNavigationState.Field, top.Repl.Navigation.State);
        Assert.Null(top.Repl.Navigation.RecipientName);
    }

    [Fact]
    public void A_resort_of_the_rows_keeps_the_selection_on_its_task()
    {
        var (top, rows) = CreateTopWithTasks();
        using var owner = top;
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        top.Agents.NewKeyDownEvent(Key.CursorDown); // worker@crew (T-1), index 1

        (rows.Rows[1], rows.Rows[2]) = (rows.Rows[2], rows.Rows[1]);
        top.Agents.Refresh();

        Assert.Equal(2, top.Agents.SelectedIndex);
        Assert.Equal("T-1", top.Repl.Navigation.SelectedTicket);
    }

    [Fact]
    public void F4_still_toggles_the_list()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        top.Repl.CurrentInput = "kept";

        top.Agents.EnterSelectionMode();
        Assert.Equal(InputNavigationState.List, top.Repl.Navigation.State);
        top.Agents.ExitSelectionMode();

        Assert.Equal(InputNavigationState.Field, top.Repl.Navigation.State);
        Assert.False(top.Agents.SelectionActive);
        Assert.Equal("kept", top.Repl.CurrentInput);
    }

    [Fact]
    public void Down_without_a_task_leaves_the_field_alone()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "alone";

        pane.RaiseKeyDown(new Key(KeyCode.CursorDown));

        Assert.Equal(InputNavigationState.Field, pane.Navigation.State);
        Assert.Equal("alone", pane.CurrentInput);
    }

    [Fact]
    public void Down_from_the_newest_entry_returns_the_draft_then_reaches_the_list()
    {
        var (top, _) = CreateTopWithTasks();
        using var owner = top;
        Submit(top.Repl, "older");
        top.Repl.CurrentInput = "draft";

        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorUp));
        Assert.Equal("older", top.Repl.CurrentInput);
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));
        Assert.Equal("draft", top.Repl.CurrentInput);
        Assert.False(top.Agents.SelectionActive);
        top.Repl.RaiseKeyDown(new Key(KeyCode.CursorDown));

        Assert.True(top.Agents.SelectionActive);
    }

    [Fact]
    public void Editing_a_recalled_entry_leaves_the_history_and_keeps_the_text()
    {
        using var pane = CreatePane();
        Submit(pane, "older");
        pane.CurrentInput = "draft";
        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));

        pane.Input.InsertText("!");

        Assert.Equal(InputNavigationState.Field, pane.Navigation.State);
        Assert.Equal("older!", pane.CurrentInput);
        // ↓ now has no draft to return to: the edited text stays.
        pane.RaiseKeyDown(new Key(KeyCode.CursorDown));
        Assert.Equal("older!", pane.CurrentInput);
    }

    [Fact]
    public async Task Enter_on_a_recalled_entry_submits_it_and_drops_the_draft()
    {
        using var pane = CreatePane();
        Submit(pane, "older");
        pane.CurrentInput = "draft";
        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));
        var read = pane.ReadLineAsync(CancellationToken.None);

        pane.RaiseKeyDown(new Key(KeyCode.Enter));

        Assert.Equal("older", await read);
        Assert.Equal(string.Empty, pane.CurrentInput);
        Assert.Equal(InputNavigationState.Field, pane.Navigation.State);
        Assert.Equal(string.Empty, pane.Navigation.Draft);
    }

    [Fact]
    public void Up_on_a_long_wrapped_line_moves_through_its_rows_before_the_history()
    {
        using var pane = CreatePane();
        Submit(pane, "old command");
        var longLine = new string('x', 250); // three visual rows in the ~98-column field
        pane.CurrentInput = longLine;        // caret at the end: the last visual row

        pane.RaiseKeyDown(new Key(KeyCode.CursorUp));

        Assert.Equal(longLine, pane.CurrentInput);
        Assert.Equal(InputNavigationState.Field, pane.Navigation.State);
    }

    [Theory]
    [InlineData("", 0, 0, 1, 10, true, true)]
    [InlineData("abcdefghijklmnopqrstuvwxy", 25, 0, 1, 10, false, true)]  // end of a 3-row line
    [InlineData("abcdefghijklmnopqrstuvwxy", 5, 0, 1, 10, true, false)]   // first visual row
    [InlineData("abcdefghijklmnopqrstuvwxy", 15, 0, 1, 10, false, false)] // middle visual row
    [InlineData("ab\ncd", 1, 1, 2, 10, false, true)]                     // second logical line
    [InlineData("abcdefghijklmnopqrstuvwxy", 3, 1, 3, 10, false, false)]  // live wrapped model, middle row
    [InlineData("abcdefghijklmnopqrstuvwxy", 3, 2, 3, 10, false, true)]   // live wrapped model, last row
    [InlineData("ab\ncd", 0, 0, 2, 0, true, false)]                      // width unknown: logical rows
    public void CaretRowBounds_reads_visual_rows(string text, int column, int row, int fieldRows, int width, bool first, bool last)
    {
        Assert.Equal((first, last), ReplPaneView.CaretRowBounds(text, column, row, fieldRows, width));
    }

    // ── Soft-wrap of long input lines ─────────────────────────────────────────────────────

    [Fact]
    public void Input_soft_wraps_long_lines_instead_of_scrolling_horizontally()
    {
        using var pane = CreatePane();
        Assert.True(pane.Input.WordWrap);
    }

    [Theory]
    [InlineData("", 10, 1)]            // empty draft still occupies one row
    [InlineData("abcdef", 10, 1)]      // fits on one row
    [InlineData("abcdefghij", 10, 1)]  // exactly fills one row, no wrap yet
    [InlineData("abcdefghijk", 10, 2)] // one over ⇒ wraps to a second row
    [InlineData("0123456789012345678901234", 10, 3)] // 25 chars / 10 ⇒ 3 rows
    [InlineData("ab\ncd", 10, 2)]      // two short logical lines ⇒ two rows
    [InlineData("abcdefghijkl\ncd", 10, 3)] // wrapped first line (2) + second line (1)
    public void CountWrappedRows_counts_display_rows_at_a_given_width(string text, int width, int expected)
    {
        Assert.Equal(expected, ReplPaneView.CountWrappedRows(text, width));
    }

    [Fact]
    public void CountWrappedRows_falls_back_to_logical_lines_when_width_unknown()
    {
        // width <= 0 (view not laid out yet): wrap is unknown, so count logical lines only.
        Assert.Equal(1, ReplPaneView.CountWrappedRows("a very long line with no width", 0));
        Assert.Equal(2, ReplPaneView.CountWrappedRows("one\ntwo", 0));
    }

    // ── CH-10: the input is a MouseClipboardTextView ──────────────────────────────────────

    [Fact]
    [Trait("BR", "CH-10")]
    public void Input_is_a_mouse_clipboard_text_view()
    {
        using var pane = CreatePane();
        Assert.IsType<MouseClipboardTextView>(pane.Input);
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void Releasing_a_drag_in_the_input_copies_the_selection()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriterOverride = new StringWriter();
        try
        {
            using var pane = CreatePane();
            pane.CurrentInput = "alpha beta";
            pane.Input.SelectAll();

            pane.Input.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new System.Drawing.Point(0, 0) });

            Assert.True(TuiClipboard.TryGetText(out var text));
            Assert.Equal("alpha beta", text);
        }
        finally
        {
            TuiClipboard.Reset();
            TuiClipboard.TerminalWriterOverride = null;
        }
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void Dragging_in_the_input_highlights_text_in_the_multiline_wrapped_input()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriterOverride = new StringWriter();
        try
        {
            using var pane = CreatePane();
            var text = new string('x', 150);
            pane.Input.Height = 5;
            pane.Layout();
            pane.CurrentInput = text;

            pane.Input.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new System.Drawing.Point(0, 0) });
            pane.Input.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, Position = new System.Drawing.Point(10, 1) });

            var selected = pane.Input.SelectedText;
            pane.Input.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new System.Drawing.Point(10, 1) });
            Assert.True(TuiClipboard.TryGetText(out var copied));
            Assert.Equal(selected, copied);
            Assert.True(pane.Input.WordWrap);
            Assert.True(selected.Length > 10);
            Assert.StartsWith(selected, text, StringComparison.Ordinal);
        }
        finally
        {
            TuiClipboard.Reset();
            TuiClipboard.TerminalWriterOverride = null;
        }
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void KeyCaptured_still_raises_for_ordinary_keys_after_the_input_change()
    {
        // Ctrl+A and Ctrl+C-with-selection are consumed by MouseClipboardTextView.OnKeyDown before the
        // KeyDown event: they are not asserted to raise KeyCaptured (Ctrl+A is asserted NOT to).
        using var pane = CreatePane();
        var raised = new List<KeyCode>();
        pane.KeyCaptured += (_, e) => raised.Add(e.Key.KeyCode);

        pane.Input.NewKeyDownEvent(Key.A.WithCtrl);
        Assert.Empty(raised);

        pane.Input.NewKeyDownEvent(new Key('x'));
        pane.Input.NewKeyDownEvent(new Key(KeyCode.Tab));
        pane.Input.NewKeyDownEvent(new Key(KeyCode.Enter));

        Assert.Equal(3, raised.Count);
        Assert.IsType<MouseClipboardTextView>(pane.Input);
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void Esc_then_Ctrl_A_then_Esc_does_not_interrupt_the_turn()
    {
        using var pane = CreatePane();
        var raised = 0;
        pane.InterruptRequested += (_, _) => raised++;

        // Through the view's own key path, as in the running app (Ctrl+A reaches the view's OnKeyDown).
        pane.Input.NewKeyDownEvent(new Key(KeyCode.Esc));
        pane.Input.NewKeyDownEvent(Key.A.WithCtrl);
        pane.Input.NewKeyDownEvent(new Key(KeyCode.Esc));

        Assert.Equal(0, raised);
    }

    private static void Submit(ReplPaneView pane, string text)
    {
        pane.CurrentInput = text;
        pane.RaiseKeyDown(new Key(KeyCode.Enter));
    }

    private sealed class FakeAssist : IReplInputAssist
    {
        private readonly string[] _commands;
        private readonly string[] _paths;

        public FakeAssist(string[]? commands = null, string[]? paths = null)
        {
            _commands = commands ?? Array.Empty<string>();
            _paths = paths ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> CompleteCommand(string prefix)
            => _commands.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        public IReadOnlyList<string> CompletePath(string prefix)
        {
            var leaf = prefix.Contains('/') ? prefix[(prefix.LastIndexOf('/') + 1)..] : prefix;
            return _paths
                .Where(p => (p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p)
                    .StartsWith(leaf, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
