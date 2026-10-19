using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Constants.Cli;
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

    private sealed class StateBuffer : ISessionBufferService
    {
        private readonly Dictionary<string, string> _state = new();

        public int MessageCount => 0;
        public IReadOnlyList<SessionMessage> GetMessages() => [];
        public void ReplaceMessages(IReadOnlyList<SessionMessage> messages) { }
        public bool AppendNote(string note) => false;
        public SessionMetadata GetMetadata() => new() { SessionId = "s" };
        public bool SetTitle(string title) => false;
        public int Truncate(int retainCount) => 0;
        public int Reset() => 0;
        public int EstimateTokenCount() => 0;
        public string? GetState(string key) => _state.GetValueOrDefault(key);
        public void SetState(string key, string? value)
        {
            if (value is null) _state.Remove(key); else _state[key] = value;
        }
    }

    private static (TuiIntegration Integration, LlmRetryProgressObserver Observer, ISessionBufferService Buffer) WiredWitness(string? configModel)
    {
        var integration = new TuiIntegration();
        var observer = new LlmRetryProgressObserver(new ProgressBroker());
        var buffer = new StateBuffer();
        var values = new Dictionary<string, string?>();
        if (configModel is not null) values["Llm:Model"] = configModel;
        var services = new ServiceCollection()
            .AddSingleton(integration)
            .AddSingleton<ISessionBufferService>(buffer)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .AddSingleton(observer)
            .BuildServiceProvider();
        TuiFidelityWiring.Wire(services);
        return (integration, observer, buffer);
    }

    private static void Fail(LlmRetryProgressObserver observer, string model)
        => observer.OnCallSettled(new LlmCallSettlement { Model = model, Succeeded = false });

    [Fact]
    [Trait("BR", "CH-08")]
    public void ModelWitness_names_the_selected_model_from_session_state()
    {
        var (integration, _, buffer) = WiredWitness("configured");
        buffer.SetState(SessionStateKeys.SelectedModel, "selected");

        Assert.NotNull(integration.ModelWitness);
        var info = integration.ModelWitness!();

        Assert.Equal("selected", info?.Model);
        Assert.Equal(ModelAvailability.Available, info?.Availability);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ModelWitness_falls_back_to_Llm_Model_and_takes_its_colour_from_the_observer()
    {
        var (integration, observer, _) = WiredWitness("configured");

        Assert.NotNull(integration.ModelWitness);
        Assert.Equal("configured", integration.ModelWitness!()?.Model);
        Fail(observer, "configured");

        var info = integration.ModelWitness!();
        Assert.Equal("configured", info?.Model);
        Assert.Equal(ModelAvailability.Unavailable, info?.Availability);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ModelWitness_ignores_a_settlement_of_another_model()
    {
        var (integration, observer, _) = WiredWitness("model-a");

        Assert.NotNull(integration.ModelWitness);
        Fail(observer, "model-b");

        var info = integration.ModelWitness!();
        Assert.Equal("model-a", info?.Model);
        Assert.Equal(ModelAvailability.Available, info?.Availability);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ModelWitness_resets_to_available_when_the_selected_model_changes()
    {
        var (integration, observer, buffer) = WiredWitness(null);
        buffer.SetState(SessionStateKeys.SelectedModel, "model-a");

        Assert.NotNull(integration.ModelWitness);
        Fail(observer, "model-a");
        Assert.Equal(ModelAvailability.Unavailable, integration.ModelWitness!()?.Availability);

        buffer.SetState(SessionStateKeys.SelectedModel, "model-b");
        var onB = integration.ModelWitness!();
        Assert.Equal("model-b", onB?.Model);
        Assert.Equal(ModelAvailability.Available, onB?.Availability);

        buffer.SetState(SessionStateKeys.SelectedModel, "model-a");
        var backOnA = integration.ModelWitness!();
        Assert.Equal("model-a", backOnA?.Model);
        Assert.Equal(ModelAvailability.Available, backOnA?.Availability);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ModelWitness_turns_amber_on_a_backoff_of_the_displayed_model()
    {
        var (integration, observer, _) = WiredWitness("model-a");

        Assert.NotNull(integration.ModelWitness);
        observer.OnRetryScheduled(new LlmRetryEvent
        {
            Provider = "Kimi",
            Attempt = 1,
            MaxRetries = 10,
            Delay = TimeSpan.FromSeconds(2),
            Model = "model-a",
        });

        var info = integration.ModelWitness!();
        Assert.Equal("model-a", info?.Model);
        Assert.Equal(ModelAvailability.Reconnecting, info?.Availability);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ModelWitness_is_unavailable_when_no_model_is_configured()
    {
        var (integration, _, _) = WiredWitness(null);

        Assert.NotNull(integration.ModelWitness);
        var info = integration.ModelWitness!();

        Assert.NotNull(info);
        Assert.Equal(string.Empty, info!.Model);
        Assert.Equal(ModelAvailability.Unavailable, info.Availability);
    }

    private static bool SpinUntilTerminal(CommandDispatchService dispatch, string ticket)
        => SpinWait.SpinUntil(() => dispatch.Registry.Get(ticket)!.IsTerminal, TimeSpan.FromSeconds(30));
}
