using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// These run live under Terminal.Gui 2.4.4 (the 2.1.0 ModuleInitializer crash that forced
// xUnit skips — TUI-12 — is fixed; verified by un-skipping under 2.4.4).
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
    public void DoubleEscape_clears_the_current_draft()
    {
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "half-typed command";

        pane.RaiseKeyDown(new Key(KeyCode.Esc));
        pane.RaiseKeyDown(new Key(KeyCode.Esc));

        Assert.Equal(string.Empty, pane.CurrentInput);
        Assert.False(read.IsCompleted); // clearing must not complete the pending read
    }

    [Fact]
    public void SingleEscape_is_swallowed_and_leaves_the_draft_intact()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "keep me";

        var key = new Key(KeyCode.Esc);
        pane.RaiseKeyDown(key);

        Assert.True(key.Handled);            // swallowed ⇒ never reaches the app quit binding
        Assert.Equal("keep me", pane.CurrentInput);
    }

    [Fact]
    public void Escape_sequence_resets_after_another_key()
    {
        using var pane = CreatePane();
        pane.CurrentInput = "keep me";

        pane.RaiseKeyDown(new Key(KeyCode.Esc));      // arm
        pane.RaiseKeyDown(new Key(KeyCode.CursorUp)); // unrelated key disarms (no history ⇒ no change)
        pane.RaiseKeyDown(new Key(KeyCode.Esc));      // arms again, but does NOT clear

        Assert.Equal("keep me", pane.CurrentInput);
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
