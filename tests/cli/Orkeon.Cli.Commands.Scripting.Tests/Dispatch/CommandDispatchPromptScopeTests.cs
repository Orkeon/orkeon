using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>Prompt scope isolation between two <see cref="CommandDispatchService"/> instances.</summary>
[Collection("PromptScope")]
public sealed class CommandDispatchPromptScopeTests
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

    private static string PostIdleOnMain(CommandDispatchService service)
        => service.postWork("talk", "crew:talk", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new CommandResponse("x", "talk", success: true, payload: string.Empty, error: null);
        }, onMain: true);

    [Fact]
    public void ShouldKeepWorkOnMainInTheTurn_WhenALaterPromptReplacesTheScope()
    {
        var service = NewService();
        string ticket;
        using (service.BeginCommand("assistant", CancellationToken.None, promptLine: "write it"))
            ticket = PostIdleOnMain(service);

        using (service.BeginCommand("help", CancellationToken.None, promptLine: "/help"))
        {
            Assert.Contains(ticket, service.PromptTurnTickets());
        }

        service.cancel(ticket);
    }

    [Fact]
    public void ShouldLeaveAnOrdinaryTaskOutOfTheTurn_WhenALaterPromptReplacesTheScope()
    {
        var service = NewService();
        string ticket;
        using (service.BeginCommand("assistant", CancellationToken.None, promptLine: "write it"))
            ticket = PostIdle(service, "task");

        using (service.BeginCommand("help", CancellationToken.None, promptLine: "/help"))
        {
            Assert.Empty(service.PromptTurnTickets());
        }

        service.cancel(ticket);
    }

    [Fact]
    public void ShouldMakeTheSecondLaunchATask_WhenWorkAlreadyRunsOnMain()
    {
        var service = NewService();
        var first = PostIdleOnMain(service);

        var second = PostIdleOnMain(service);

        Assert.True(service.get(first)!.onMain);
        Assert.False(service.get(second)!.onMain);
        service.cancel(first);
        service.cancel(second);
    }

    [Fact]
    public void ShouldGiveTheMainThreadAgain_WhenThePreviousWorkHasEnded()
    {
        var service = NewService();
        service.cancel(PostIdleOnMain(service));

        var next = PostIdleOnMain(service);

        Assert.True(service.get(next)!.onMain);
        service.cancel(next);
    }

    [Fact]
    [Trait("BR", "R12b")]
    public void ShouldIsolatePromptScope_WhenTwoDispatchServicesRun()
    {
        var a = NewService();
        var b = NewService();

        string ticketA;
        using (a.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: "/a"))
        {
            ticketA = PostIdle(a, "turn-a");
            Assert.Contains(ticketA, a.PromptTurnTickets());
            Assert.Empty(b.PromptTurnTickets());

            using (b.BeginCommand("b", CancellationToken.None, timeout: null, promptLine: "/b"))
            {
                Assert.Equal(new[] { ticketA }, a.PromptTurnTickets());
                Assert.Empty(b.PromptTurnTickets());
            }

            Assert.Equal(new[] { ticketA }, a.PromptTurnTickets());
        }

        Assert.Contains(ticketA, a.PromptTurnTickets());
        a.cancel(ticketA);
    }
}
