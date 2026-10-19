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

    [Fact]
    [Trait("BR", "CH-08")]
    public void Refresh_reads_the_model_witness_each_time()
    {
        // A running Terminal.Gui application is needed to drive HintBarView.Refresh, so the
        // witness text is observed through the pure model the view renders on each Refresh.
        var current = new ModelWitnessInfo { Model = "model-a", Availability = Orkeon.Cli.Abstractions.ModelAvailability.Available };
        var integration = new TuiIntegration { ModelWitness = () => current };

        Assert.Equal("model-a", HintBarModel.Witness(integration.ModelWitness!()));
        current = new ModelWitnessInfo { Model = "model-b", Availability = Orkeon.Cli.Abstractions.ModelAvailability.Reconnecting };
        Assert.Equal("model-b", HintBarModel.Witness(integration.ModelWitness!()));

        Assert.Equal("no model", HintBarModel.Witness(new TuiIntegration().ModelWitness?.Invoke()));
    }
}
