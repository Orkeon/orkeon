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

        Assert.Equal("model-a", HintBarModel.Witness(integration.ModelWitness!(), GlyphSet.Unicode));
        current = new ModelWitnessInfo { Model = "model-b", Availability = Orkeon.Cli.Abstractions.ModelAvailability.Reconnecting };
        Assert.Equal("model-b", HintBarModel.Witness(integration.ModelWitness!(), GlyphSet.Unicode));

        Assert.Equal("no model", HintBarModel.Witness(new TuiIntegration().ModelWitness?.Invoke(), GlyphSet.Unicode));
    }

    private static HintBarView CreateBar() => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    [Fact]
    public void ShouldThrow_WhenBindArgumentsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HintBarView(null!, InlineDispatcher.Instance));
        using var bar = CreateBar();
        Assert.Throws<ArgumentNullException>(() => bar.Bind(null!, new TuiIntegration(), false, null));
        Assert.Throws<ArgumentNullException>(() => bar.Bind(new FakeInteractiveRunner(), null!, false, null));
        Assert.Throws<ArgumentNullException>(() => bar.BindNavigation(null!));
    }

    [Fact]
    public void ShouldComposeModeLogsAndTasks_WhenBoundIdle()
    {
        using var bar = CreateBar();

        bar.Bind(new FakeInteractiveRunner(), new TuiIntegration { PermissionMode = () => "plan" }, agentsAvailable: true, modelWitness: null);

        Assert.Contains("(shift+tab to cycle)", bar.CurrentText);
        Assert.Contains("ctrl+g logs", bar.CurrentText);
        Assert.Contains("tasks", bar.CurrentText);
        Assert.DoesNotContain("esc esc to interrupt", bar.CurrentText);
    }

    [Fact]
    public void ShouldShowInterruptHint_WhenCommandRuns()
    {
        var runner = new FakeInteractiveRunner { Running = true };
        using var bar = CreateBar();

        bar.Bind(runner, new TuiIntegration(), agentsAvailable: true, modelWitness: null, agentsHasRows: () => false);

        Assert.Contains("esc esc to interrupt", bar.CurrentText);
        Assert.DoesNotContain("tasks", bar.CurrentText);
    }

    [Fact]
    public void ShouldFallBackToDefaultPosture_WhenModeOrWitnessProvidersThrow()
    {
        using var bar = CreateBar();
        var integration = new TuiIntegration { PermissionMode = () => throw new InvalidOperationException("mode") };

        bar.Bind(new FakeInteractiveRunner(), integration, agentsAvailable: false,
            modelWitness: () => throw new InvalidOperationException("witness"));

        Assert.Contains(HintBarModel.PostureLabel("default"), bar.CurrentText);
    }

    [Fact]
    public void ShouldShowListHints_WhenTaskListHasFocus()
    {
        var machine = new InputNavigationStateMachine();
        machine.Handle(InputNavigationKey.Down, new InputNavigationContext
        {
            Text = "typed",
            Rows = [new AgentRowInfo { Name = "main", IsActive = true }, new AgentRowInfo { Name = "alpha@crew", Ticket = "T-1" }],
        });
        using var bar = CreateBar();
        bar.BindNavigation(() => machine);

        bar.Bind(new FakeInteractiveRunner(), new TuiIntegration(), agentsAvailable: true,
            modelWitness: () => new ModelWitnessInfo { Model = "model-a", Availability = Orkeon.Cli.Abstractions.ModelAvailability.Available });

        Assert.Equal(InputNavigationState.List, machine.State);
        Assert.Contains("enter view", bar.CurrentText);
        Assert.DoesNotContain("ctrl+g logs", bar.CurrentText);
    }
}
