using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Tests.Shared.Timing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Tests.Console;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// inside xUnit test processes (TypeLoadException on MemberNotNullWhenAttribute via
// Microsoft.TestPlatform.CoreUtilities). Reactivation verified to fail. Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes.
// Tracked in project/tasks: TUI-12.
public class TerminalGuiConsoleAdapterTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static (TerminalGuiConsoleAdapter adapter, ReplPaneView repl) Create()
    {
        var repl = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        return (new TerminalGuiConsoleAdapter(repl), repl);
    }

    [Fact]
    public void Write_with_prompt_pattern_sets_prompt_prefix()
    {
        var (adapter, repl) = Create();
        adapter.Write("[claim-verifier] > ");
        Assert.Equal("[claim-verifier] > ", repl.CurrentPromptPrefix);
        Assert.Equal(string.Empty, repl.CurrentHistory);
    }

    [Fact]
    public void Write_without_prompt_pattern_appends_to_history()
    {
        var (adapter, repl) = Create();
        adapter.Write("regular output");
        Assert.Equal(string.Empty, repl.CurrentPromptPrefix);
        Assert.Equal("regular output", repl.CurrentHistory);
    }

    [Fact]
    public void WriteLine_appends_with_newline()
    {
        var (adapter, repl) = Create();
        adapter.WriteLine("hello");
        Assert.Equal("hello\n", repl.CurrentHistory);
    }

    [Fact]
    public void Clear_empties_history()
    {
        var (adapter, repl) = Create();
        adapter.WriteLine("noise");
        adapter.Clear();
        Assert.Equal(string.Empty, repl.CurrentHistory);
    }

    [Fact]
    public async Task ReadLine_returns_text_when_user_submits()
    {
        var (adapter, repl) = Create();
        var task = Task.Run(() => adapter.ReadLine());
        // Deterministic wait (R5.6): the pending read arms inside Task.Run at an
        // unobservable instant, so keep re-submitting until the reader observes it
        // (a pre-arm Enter is dropped by the pane and the input is re-typed).
        await Polling.WaitUntilAsync(() =>
        {
            repl.CurrentInput = "list";
            repl.RaiseKeyDown(new Key(KeyCode.Enter));
            return task.IsCompleted;
        });
        var result = await task;
        Assert.Equal("list", result);
    }

    [Fact]
    public async Task ReadKey_intercept_marks_key_handled_and_returns_console_key()
    {
        var (adapter, repl) = Create();
        var task = Task.Run(() => adapter.ReadKey(intercept: true));
        var key = new Key(KeyCode.A);
        // Deterministic wait (R5.6): ReadKey subscribes KeyCaptured inside Task.Run at
        // an unobservable instant, so keep re-raising until it captures the key
        // (a pre-subscription key-down is a no-op for a bare letter key).
        await Polling.WaitUntilAsync(() =>
        {
            repl.RaiseKeyDown(key);
            return task.IsCompleted;
        });
        var result = await task;
        Assert.True(key.Handled);
        Assert.Equal(ConsoleKey.A, result.Key);
    }

    [Fact]
    public async Task ReadLineAsync_override_resolves_when_user_submits()
    {
        // R10.10 (ANT-010): the adapter overrides the sync-delegating IConsoleAdapter default
        // with the pane's real async pipeline. Arming is synchronous (no Task.Run needed):
        // the pending read exists before we raise the submitting key.
        var (adapter, repl) = Create();
        IConsoleAdapter console = adapter;

        var task = console.ReadLineAsync(TestContext.Current.CancellationToken);
        repl.CurrentInput = "status";
        repl.RaiseKeyDown(new Key(KeyCode.Enter));

        Assert.Equal("status", await task);
    }

    [Fact]
    public async Task ReadLineAsync_override_returns_null_when_token_is_cancelled()
    {
        var (adapter, _) = Create();
        IConsoleAdapter console = adapter;
        using var cts = new CancellationTokenSource();

        var task = console.ReadLineAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Null(await task);
    }

    [Fact]
    public async Task ReadKeyAsync_override_resolves_on_captured_key()
    {
        // Subscription to KeyCaptured happens synchronously inside the override,
        // so a single raise deterministically completes the read.
        var (adapter, repl) = Create();
        IConsoleAdapter console = adapter;

        var task = console.ReadKeyAsync(intercept: true, TestContext.Current.CancellationToken);
        var key = new Key(KeyCode.A);
        repl.RaiseKeyDown(key);

        var result = await task;
        Assert.True(key.Handled);
        Assert.Equal(ConsoleKey.A, result.Key);
    }

    [Fact]
    public async Task ReadKeyAsync_override_cancels_and_detaches_when_token_is_cancelled()
    {
        var (adapter, repl) = Create();
        IConsoleAdapter console = adapter;
        using var cts = new CancellationTokenSource();

        var task = console.ReadKeyAsync(intercept: true, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        // The listener was detached on cancellation: a later key is no longer intercepted.
        var lateKey = new Key(KeyCode.B);
        repl.RaiseKeyDown(lateKey);
        Assert.False(lateKey.Handled);
    }

    [Theory]
    [InlineData("orkeon> ", true)]
    [InlineData("[claim-verifier] > ", true)]
    [InlineData("> ", true)]
    [InlineData(">", true)]
    [InlineData("regular output", false)]
    [InlineData("multi\nline > ", false)]
    [InlineData("", false)]
    public void LooksLikePrompt_heuristic(string text, bool expected)
    {
        Assert.Equal(expected, TerminalGuiConsoleAdapter.LooksLikePrompt(text));
    }
}
#pragma warning restore xUnit1004
