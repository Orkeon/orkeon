using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

[Collection("PromptScope")]
public sealed class CommandDispatchContextTokensTests
{
    private readonly CommandInstanceRegistry _registry = new();

    private CommandDispatchService NewService()
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance), new AgentCommandDirectory(), _registry);

    private static string PostIdle(CommandDispatchService service)
        => service.postWork("turn", "crew:x", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new CommandResponse("x", "turn", success: true, payload: string.Empty, error: null);
        });

    [Fact]
    public void The_latest_call_of_the_turn_is_what_the_context_holds()
    {
        // Arrange
        var service = NewService();
        using var scope = service.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: "/a");
        var ticket = PostIdle(service);
        var instance = _registry.Get(ticket)!;
        instance.AddUsage(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 5 }, null);

        // Act
        instance.AddUsage(new CostUsageEvent { PromptTokens = 250, CompletionTokens = 5 }, null);

        // Assert
        Assert.Equal(250, service.PromptTurnContextTokens());
        service.cancel(ticket);
    }

    [Fact]
    public void A_call_without_a_prompt_figure_leaves_the_context_as_it_is()
    {
        // Arrange
        var service = NewService();
        using var scope = service.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: "/a");
        var ticket = PostIdle(service);
        var instance = _registry.Get(ticket)!;
        instance.AddUsage(new CostUsageEvent { PromptTokens = 100 }, null);

        // Act
        instance.AddUsage(new CostUsageEvent { CompletionTokens = 5 }, null);

        // Assert
        Assert.Equal(100, service.PromptTurnContextTokens());
        service.cancel(ticket);
    }

    [Fact]
    public void Before_any_prompt_there_is_no_figure()
    {
        // Arrange
        var service = NewService();

        // Act
        var tokens = service.PromptTurnContextTokens();

        // Assert
        Assert.Equal(0, tokens);
    }
}
