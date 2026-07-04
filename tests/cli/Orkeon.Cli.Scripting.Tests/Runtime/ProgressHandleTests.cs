using Orkeon.Cli.Scripting.Runtime;
using Orkeon.Cli.Scripting.Tests.Fixtures;

namespace Orkeon.Cli.Scripting.Tests.Runtime;

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
}
