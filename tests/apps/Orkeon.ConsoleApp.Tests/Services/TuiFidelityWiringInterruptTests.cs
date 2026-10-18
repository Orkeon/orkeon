using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Services;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>The prompt scope of <see cref="CommandDispatchService"/> is static: classes opening one must not overlap.</summary>
[CollectionDefinition("PromptScope", DisableParallelization = true)]
public sealed class PromptScopeCollection;

[Collection("PromptScope")]
public sealed class TuiFidelityWiringInterruptTests
{
    private static CommandDispatchService NewDispatch()
        => new(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry(),
            NullLogger<CommandDispatchService>.Instance);

    private static string PostIdle(CommandDispatchService dispatch, string label)
        => dispatch.postWork(label, "crew:x", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new CommandResponse("x", label, success: true, payload: string.Empty, error: null);
        });

    private static (ServiceProvider Services, TuiIntegration Integration) Wired(CommandDispatchService dispatch)
    {
        var integration = new TuiIntegration();
        var services = new ServiceCollection()
            .AddSingleton(integration)
            .AddSingleton(dispatch)
            .BuildServiceProvider();
        TuiFidelityWiring.Wire(services);
        return (services, integration);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    [Trait("BR", "CH-03")]
    public void InterruptCurrent_cancels_the_prompt_turn_and_not_a_newer_background_ticket()
    {
        var dispatch = NewDispatch();
        var (services, integration) = Wired(dispatch);
        using var _ = services;

        string promptTicket;
        using (CommandDispatchService.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: "/a"))
        {
            promptTicket = PostIdle(dispatch, "turn");
        }

        string backgroundTicket;
        using (CommandDispatchService.BeginCommand("b", CancellationToken.None, timeout: null))
        {
            backgroundTicket = PostIdle(dispatch, "crew");
        }

        Assert.NotNull(integration.InterruptCurrent);
        integration.InterruptCurrent!();

        Assert.True(dispatch.Registry.Get(promptTicket)!.State == CommandInstanceState.Cancelled
            || dispatch.Registry.Get(promptTicket)!.IsTerminal
            || SpinUntilTerminal(dispatch, promptTicket));
        Assert.Equal(CommandInstanceState.Running, dispatch.Registry.Get(backgroundTicket)!.State);

        dispatch.cancel(promptTicket);
        dispatch.cancel(backgroundTicket);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void PromptTurnRunning_is_false_when_only_a_background_ticket_runs()
    {
        var dispatch = NewDispatch();
        var (services, integration) = Wired(dispatch);
        using var _ = services;

        string backgroundTicket;
        using (CommandDispatchService.BeginCommand("b", CancellationToken.None, timeout: null))
        {
            backgroundTicket = PostIdle(dispatch, "crew");
        }

        Assert.NotNull(integration.PromptTurnRunning);
        Assert.False(integration.PromptTurnRunning!());

        string promptTicket;
        using (CommandDispatchService.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: "/a"))
        {
            promptTicket = PostIdle(dispatch, "turn");
        }

        Assert.True(integration.PromptTurnRunning!());

        dispatch.cancel(promptTicket);
        dispatch.cancel(backgroundTicket);
    }

    private static bool SpinUntilTerminal(CommandDispatchService dispatch, string ticket)
        => SpinWait.SpinUntil(() => dispatch.Registry.Get(ticket)!.IsTerminal, TimeSpan.FromSeconds(30));
}
