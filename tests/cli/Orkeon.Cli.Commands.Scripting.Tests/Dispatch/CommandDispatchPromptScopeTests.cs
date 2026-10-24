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
