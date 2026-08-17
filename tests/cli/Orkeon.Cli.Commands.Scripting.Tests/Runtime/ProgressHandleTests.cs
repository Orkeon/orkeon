using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

public sealed class ProgressHandleTests
{
    [Fact]
    public void Advance_increments_and_renders_with_total()
    {
        var console = new ScriptedTestConsole();
        var sut = new ProgressHandle(console, "deploy", total: 3);

        sut.advance("validate");
        sut.advance("apply");
        sut.advance("verify");

        Assert.Contains("[deploy] 1/3 — validate", console.Output);
        Assert.Contains("[deploy] 2/3 — apply", console.Output);
        Assert.Contains("[deploy] 3/3 — verify", console.Output);
    }

    [Fact]
    public void Advance_without_total_renders_without_denominator()
    {
        var console = new ScriptedTestConsole();
        var sut = new ProgressHandle(console, "scan", total: null);
        sut.advance();
        sut.advance("found");

        Assert.Contains("[scan] 1", console.Output);
        Assert.Contains("[scan] 2 — found", console.Output);
    }

    [Fact]
    public void Set_jumps_to_value()
    {
        var console = new ScriptedTestConsole();
        var sut = new ProgressHandle(console, "task", total: 10);
        sut.set(7, "halfway-ish");
        Assert.Contains("[task] 7/10 — halfway-ish", console.Output);
    }

    [Fact]
    public void Done_emits_done_line_and_silences_further_calls()
    {
        var console = new ScriptedTestConsole();
        var sut = new ProgressHandle(console, "deploy", total: 2);
        sut.done("OK");
        sut.advance("ignored"); // should be no-op after done

        Assert.Contains("[deploy] done: OK", console.Output);
        Assert.DoesNotContain("ignored", console.Output);
    }

    [Fact]
    public void Publishes_live_snapshots_to_the_broker_and_clears_on_done()
    {
        // The console lines are the deterministic transcript surface; the broker is what
        // the TUI status line polls for the live bar.
        var console = new ScriptedTestConsole();
        var broker = new Orkeon.Cli.Commands.Scripting.Progress.ProgressBroker();
        var sut = new ProgressHandle(console, "deploy", total: 4, broker);

        sut.advance("validate");
        Assert.Equal(1, broker.Current!.Step);
        Assert.Equal(4, broker.Current.Total);
        Assert.Equal("validate", broker.Current.Message);

        sut.set(3);
        Assert.Equal(3, broker.Current!.Step);

        sut.done();
        Assert.Null(broker.Current);
    }

    [Fact]
    public void Without_a_broker_the_console_lines_are_the_whole_behavior()
    {
        var console = new ScriptedTestConsole();
        var sut = new ProgressHandle(console, "scan", total: null);
        sut.advance();
        sut.done();
        Assert.Contains("[scan] 1", console.Output);
        Assert.Contains("[scan] done", console.Output);
    }
}
