using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes
// (TypeLoadException on MemberNotNullWhenAttribute via Microsoft.TestPlatform.CoreUtilities).
public class LogsPaneViewTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static LogsPaneView CreatePane(int capacity = 100)
        => new(new TerminalGuiOptions { LogsBufferCapacity = capacity }, InlineDispatcher.Instance);

    [Fact]
    public void Append_increments_line_count()
    {
        using var pane = CreatePane();
        pane.Append("first");
        pane.Append("second");
        Assert.Equal(2, pane.LineCount);
    }

    [Fact]
    public void Append_above_capacity_drops_oldest()
    {
        using var pane = CreatePane(capacity: 3);
        pane.Append("a");
        pane.Append("b");
        pane.Append("c");
        pane.Append("d");
        Assert.Equal(3, pane.LineCount);
        Assert.Equal("b\nc\nd", pane.CurrentText);
    }

    [Fact]
    public void Clear_resets_line_count_to_zero()
    {
        using var pane = CreatePane();
        pane.Append("x");
        pane.Append("y");
        pane.Clear();
        Assert.Equal(0, pane.LineCount);
        Assert.Equal(string.Empty, pane.CurrentText);
    }

    [Fact]
    public void Append_is_thread_safe()
    {
        using var pane = CreatePane(capacity: 10_000);
        const int threads = 8, perThread = 500;
        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < perThread; i++)
                pane.Append($"t{t}-i{i}");
        });
        Assert.Equal(threads * perThread, pane.LineCount);
    }

    // ── Issue #196: append cost, coalescing, hidden pane ─────────────────────────────────

    [Fact]
    public void A_burst_of_appends_costs_one_ui_dispatch()
    {
        var dispatcher = new FakeUiDispatcher();
        using var pane = new LogsPaneView(new TerminalGuiOptions { LogsBufferCapacity = 100 }, dispatcher);

        for (var i = 0; i < 50; i++) pane.Append($"entry {i}");

        Assert.Equal(1, dispatcher.InvokeCount);
        dispatcher.RunPending();
        Assert.Equal(string.Join('\n', Enumerable.Range(0, 50).Select(i => $"entry {i}")), pane.CurrentText);
    }

    [Fact]
    public void Render_work_per_append_stays_constant_over_ten_thousand_lines()
    {
        using var pane = CreatePane(capacity: 5_000);
        var model = pane.RenderedModel;
        var work = new long[10_000];
        for (var i = 0; i < work.Length; i++)
        {
            var before = model.CharactersWrapped;
            pane.Append($"log entry {i:D5}");
            work[i] = model.CharactersWrapped - before;
        }

        Assert.All(work, w => Assert.Equal(work[0], w));
        Assert.Equal(work.Take(1_000).Sum(), work.TakeLast(1_000).Sum());
        Assert.Equal(5_000, model.LineCount);
    }

    [Fact]
    public void A_hidden_pane_does_no_render_work_and_renders_in_full_when_shown()
    {
        var dispatcher = new FakeUiDispatcher();
        using var pane = new LogsPaneView(new TerminalGuiOptions { LogsBufferCapacity = 3 }, dispatcher);
        pane.Visible = false;
        var wrapped = pane.RenderedModel.CharactersWrapped;

        for (var i = 0; i < 1_000; i++) pane.Append($"hidden {i}");

        Assert.Equal(0, dispatcher.InvokeCount);
        Assert.Equal(wrapped, pane.RenderedModel.CharactersWrapped);
        Assert.Equal(3, pane.LineCount);

        pane.Visible = true;

        Assert.Equal("hidden 997\nhidden 998\nhidden 999", pane.CurrentText);
    }

    [Fact]
    public void Appends_queued_before_the_pane_is_shown_are_not_rendered_twice()
    {
        var dispatcher = new FakeUiDispatcher();
        using var pane = new LogsPaneView(new TerminalGuiOptions { LogsBufferCapacity = 10 }, dispatcher);
        pane.Append("a");
        pane.Visible = false;
        pane.Visible = true;
        pane.Append("b");

        dispatcher.RunPending();

        Assert.Equal("a\nb", pane.CurrentText);
    }

    [Fact]
    public void Clear_is_ordered_with_the_appends_around_it()
    {
        var dispatcher = new FakeUiDispatcher();
        using var pane = new LogsPaneView(new TerminalGuiOptions { LogsBufferCapacity = 10 }, dispatcher);
        pane.Append("old");
        pane.Clear();
        pane.Append("new");

        dispatcher.RunPending();

        Assert.Equal("new", pane.CurrentText);
        Assert.Equal(1, pane.LineCount);
    }
}
#pragma warning restore xUnit1004
