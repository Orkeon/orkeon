using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class HintBarViewTests
{
    [Fact]
    [Trait("BR", "CH-07")]
    public void Bar_is_running_when_only_the_prompt_turn_runs()
    {
        var idle = new FakeInteractiveRunner { Running = false };
        Assert.True(HintBarView.IsTurnRunning(idle, new TuiIntegration { PromptTurnRunning = () => true }));
        Assert.False(HintBarView.IsTurnRunning(idle, new TuiIntegration { PromptTurnRunning = () => false }));
        Assert.False(HintBarView.IsTurnRunning(idle, null));
    }
}
