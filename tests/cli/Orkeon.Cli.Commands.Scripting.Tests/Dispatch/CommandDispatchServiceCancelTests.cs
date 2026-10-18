using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>The prompt scope is a static field of the service: its tests never run in parallel.</summary>
[CollectionDefinition("PromptScope", DisableParallelization = true)]
public sealed class PromptScopeCollection;

/// <summary>Cancellation and prompt-turn bookkeeping of <see cref="CommandDispatchService"/>.</summary>
[Collection("PromptScope")]
public sealed class CommandDispatchServiceCancelTests
{
    private static CommandDispatchService NewService()
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
               new AgentCommandDirectory(),
               new CommandInstanceRegistry());

    private static string PostIdle(CommandDispatchService service, string label)
        => service.postWork(label, "crew:x", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new CommandResponse("x", label, success: true, payload: string.Empty, error: null);
        });

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task Cancel_ends_a_posted_turn_waiting_on_a_delay()
    {
        var service = NewService();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticket = service.postWork("turn", "crew:x", async ct =>
        {
            try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { observed.SetResult(); throw; }
            return new CommandResponse("x", "turn", success: true, payload: string.Empty, error: null);
        });

        Assert.True(service.cancel(ticket));

        await observed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(service.Registry.Get(ticket)!.IsTerminal);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void Ticket_posted_under_a_prompt_scope_is_in_the_prompt_turn_before_dispatch_returns()
    {
        var service = NewService();
        using var scope = CommandDispatchService.BeginCommand("x", CancellationToken.None, timeout: null, promptLine: "/x");

        var ticket = PostIdle(service, "x");

        Assert.Contains(ticket, service.PromptTurnTickets());
        service.cancel(ticket);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void Ticket_of_an_earlier_prompt_line_is_not_in_the_current_turn()
    {
        var service = NewService();
        string a;
        using (CommandDispatchService.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: "/a"))
            a = PostIdle(service, "a");

        string b;
        using (CommandDispatchService.BeginCommand("b", CancellationToken.None, timeout: null, promptLine: "/b"))
            b = PostIdle(service, "b");

        var turn = service.PromptTurnTickets();
        Assert.Contains(b, turn);
        Assert.DoesNotContain(a, turn);
        service.cancel(a);
        service.cancel(b);
    }
}
