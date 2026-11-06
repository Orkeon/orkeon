using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Services;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Session;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>The bar's <c>context N%</c>, read through the delegate the wiring leaves on the integration.</summary>
[Collection("PromptScope")]
public sealed class TuiFidelityWiringContextWindowTests : IDisposable
{
    private readonly CommandInstanceRegistry _registry = new();
    private readonly CommandDispatchService _dispatch;
    private readonly InMemorySessionBufferService _buffer = new();
    private readonly TuiIntegration _integration = new();
    private ServiceProvider? _services;
    private string? _ticket;

    public TuiFidelityWiringContextWindowTests()
    {
        _dispatch = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            _registry,
            NullLogger<CommandDispatchService>.Instance);
    }

    public void Dispose()
    {
        if (_ticket is not null) _dispatch.cancel(_ticket);
        _services?.Dispose();
    }

    private void Wire(int? window = null)
    {
        _services = new ServiceCollection()
            .AddSingleton(_integration)
            .AddSingleton(_dispatch)
            .AddSingleton<ISessionBufferService>(_buffer)
            .Configure<CliSessionOptions>(options => options.ContextWindowTokens = window)
            .BuildServiceProvider();
        TuiFidelityWiring.Wire(_services);
    }

    private void CallTheModel(string line, long promptTokens)
    {
        using var scope = _dispatch.BeginCommand("a", CancellationToken.None, timeout: null, promptLine: line);
        _ticket = _dispatch.postWork("turn", "crew:x", async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new CommandResponse("x", "turn", success: true, payload: string.Empty, error: null);
        });
        _registry.Get(_ticket)!.AddUsage(new CostUsageEvent { PromptTokens = (int)promptTokens }, null);
    }

    [Fact]
    public void An_unset_window_reads_the_default_the_session_budgets_against()
    {
        // Arrange
        Wire();
        CallTheModel("/a", CliSessionOptions.DefaultContextWindowTokens / 2);

        // Act
        var percent = _integration.ContextPercent!();

        // Assert
        Assert.Equal(50, percent);
    }

    [Fact]
    public void A_configured_window_is_the_one_the_figure_reads()
    {
        // Arrange
        Wire(window: 1000);
        CallTheModel("/a", 100);

        // Act
        var percent = _integration.ContextPercent!();

        // Assert
        Assert.Equal(10, percent);
    }

    [Fact]
    public void An_emptied_session_leaves_no_context_figure()
    {
        // Arrange
        Wire();
        _buffer.AppendMessage(new SessionMessage { Role = "user", Content = "hello" });
        CallTheModel("/a", 1000);
        Assert.NotNull(_integration.ContextPercent!());
        using var clear = _dispatch.BeginCommand("clear", CancellationToken.None, timeout: null, promptLine: "/clear");
        _buffer.Reset();

        // Act
        var percent = _integration.ContextPercent!();

        // Assert
        Assert.Null(percent);
    }

    [Fact]
    public void A_new_prompt_of_a_session_with_messages_keeps_the_last_figure_until_its_first_call()
    {
        // Arrange
        Wire();
        _buffer.AppendMessage(new SessionMessage { Role = "user", Content = "hello" });
        CallTheModel("/a", CliSessionOptions.DefaultContextWindowTokens / 4);
        Assert.Equal(25, _integration.ContextPercent!());
        using var next = _dispatch.BeginCommand("b", CancellationToken.None, timeout: null, promptLine: "/b");

        // Act
        var percent = _integration.ContextPercent!();

        // Assert
        Assert.Equal(25, percent);
    }

    [Fact]
    public void A_session_that_keeps_no_messages_still_shows_its_figure()
    {
        // Arrange
        Wire();
        CallTheModel("/a", CliSessionOptions.DefaultContextWindowTokens / 4);

        // Act
        var percent = _integration.ContextPercent!();

        // Assert
        Assert.Equal(25, percent);
    }
}
