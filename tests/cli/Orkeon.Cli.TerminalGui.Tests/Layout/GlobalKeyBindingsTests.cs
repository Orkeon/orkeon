using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class GlobalKeyBindingsTests
{
    [Fact]
    [Trait("BR", "CH-06")]
    public void Quit_needs_confirmation_only_while_a_command_runs()
    {
        var runner = new FakeInteractiveRunner();
        Assert.False(GlobalKeyBindings.QuitNeedsConfirmation(runner, null));
        runner.Running = true;
        Assert.True(GlobalKeyBindings.QuitNeedsConfirmation(runner, null));
    }

    [Fact]
    [Trait("BR", "CH-06")]
    public void Quit_needs_confirmation_while_the_prompt_turn_runs()
    {
        var runner = new FakeInteractiveRunner();
        Assert.True(GlobalKeyBindings.QuitNeedsConfirmation(runner, new TuiIntegration { PromptTurnRunning = () => true }));
        Assert.False(GlobalKeyBindings.QuitNeedsConfirmation(runner, new TuiIntegration { PromptTurnRunning = () => false }));
        Assert.False(GlobalKeyBindings.QuitNeedsConfirmation(runner, new TuiIntegration()));
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void Escape_pair_reaches_Interrupt_when_only_a_dispatched_turn_runs()
    {
        using var top = new SplitPaneToplevel(new TerminalGuiOptions(), InlineDispatcher.Instance);
        var runner = new FakeInteractiveRunner();
        top.Runner = runner;
        var interrupts = 0;
        var integration = new TuiIntegration { PromptTurnRunning = () => true, InterruptCurrent = () => interrupts++ };

        GlobalKeyBindings.OnInterruptRequested(top, integration);
        Assert.Equal(1, interrupts);

        var idle = new TuiIntegration { PromptTurnRunning = () => false, InterruptCurrent = () => interrupts++ };
        GlobalKeyBindings.OnInterruptRequested(top, idle);
        Assert.Equal(1, interrupts);
        Assert.Equal(0, runner.CancelRequests);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void Escape_pair_cancels_a_synchronous_command_even_when_a_turn_delegate_is_wired()
    {
        using var top = new SplitPaneToplevel(new TerminalGuiOptions(), InlineDispatcher.Instance);
        var runner = new FakeInteractiveRunner();
        top.Runner = runner;
        var interrupts = 0;

        TuiIntegration Wire(bool turn) => new() { PromptTurnRunning = () => turn, InterruptCurrent = () => interrupts++ };

        runner.Running = true;
        GlobalKeyBindings.OnInterruptRequested(top, Wire(false));
        Assert.Equal(1, runner.CancelRequests);
        Assert.Equal(0, interrupts);

        GlobalKeyBindings.OnInterruptRequested(top, Wire(true));
        Assert.Equal(2, runner.CancelRequests);
        Assert.Equal(1, interrupts);

        runner.Running = false;
        GlobalKeyBindings.OnInterruptRequested(top, Wire(true));
        Assert.Equal(2, runner.CancelRequests);
        Assert.Equal(2, interrupts);

        GlobalKeyBindings.OnInterruptRequested(top, Wire(false));
        Assert.Equal(2, runner.CancelRequests);
        Assert.Equal(2, interrupts);
    }
}
