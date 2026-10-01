using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Interop.AgentFramework.Tests.Doubles;

namespace Orkeon.Interop.AgentFramework.Tests;

public sealed class CrewAgentTests
{
    [Fact]
    public async Task RunAsync_kicks_the_crew_off_with_the_request_as_initial_context()
    {
        var orchestrator = new FakeCrewOrchestrationService { FinalOutput = "the crew says hi" };
        var crewId = CrewId.Create();
        var agent = new CrewAgent(orchestrator, crewId, "Triage crew", "Triages issues");

        var response = await agent.RunAsync("Triage today's issues", cancellationToken: TestContext.Current.CancellationToken);

        var kickoff = Assert.Single(orchestrator.Kickoffs);
        Assert.Equal(crewId, kickoff.CrewId);
        Assert.Equal("Triage today's issues", kickoff.Input.InitialContext);
        Assert.Equal("the crew says hi", response.Text);
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
        Assert.Equal("Triage crew", response.Messages[0].AuthorName);
        Assert.Equal("orkeon-crew-" + crewId, response.AgentId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
    }

    [Fact]
    public async Task RunAsync_maps_the_crew_token_telemetry_to_usage_and_a_failed_crew_to_an_error_finish()
    {
        var orchestrator = new FakeCrewOrchestrationService { TokensUsed = new TokenUsage(120, 30, 150), Succeeded = false };
        var agent = new CrewAgent(orchestrator, CrewId.Create());

        var response = await agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response.Usage);
        Assert.Equal(120, response.Usage!.InputTokenCount);
        Assert.Equal(30, response.Usage.OutputTokenCount);
        Assert.Equal(150, response.Usage.TotalTokenCount);
        Assert.Equal("error", response.FinishReason?.Value);
    }

    [Fact]
    public async Task RunAsync_without_telemetry_leaves_usage_null_rather_than_a_fabricated_zero()
    {
        var agent = new CrewAgent(new FakeCrewOrchestrationService(), CrewId.Create());

        var response = await agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(response.Usage);
    }

    [Fact]
    public void A_multi_turn_conversation_becomes_a_quoted_history_plus_the_last_request()
    {
        var context = CrewAgent.ConversationToContext(
        [
            new ChatMessage(ChatRole.User, "first"),
            new ChatMessage(ChatRole.Assistant, "answer"),
            new ChatMessage(ChatRole.User, "second"),
        ]);

        Assert.Equal("Conversation so far:\nuser: first\nassistant: answer\n\nRequest:\nsecond", context);
        Assert.Equal("only", CrewAgent.ConversationToContext([new ChatMessage(ChatRole.User, "only")]));
        Assert.Equal(string.Empty, CrewAgent.ConversationToContext([]));
    }

    [Fact]
    public async Task RunStreamingAsync_yields_the_final_answer_as_updates()
    {
        var agent = new CrewAgent(new FakeCrewOrchestrationService { FinalOutput = "streamed" }, CrewId.Create());

        var text = string.Empty;
        await foreach (var update in agent.RunStreamingAsync("go", cancellationToken: TestContext.Current.CancellationToken))
            text += update.Text;

        Assert.Equal("streamed", text);
    }

    [Fact]
    public async Task A_session_round_trips_through_serialisation_and_holds_nothing()
    {
        var agent = new CrewAgent(new FakeCrewOrchestrationService(), CrewId.Create());

        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        var json = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var restored = await agent.DeserializeSessionAsync(json, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(restored);
        var response = await agent.RunAsync("go", restored, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("crew output", response.Text);
    }

    /// <summary>
    /// GAP-25: the orchestrator and the repositories it reads are scoped. The factory used to be
    /// a singleton holding the orchestrator it resolved from the root, so every crew agent of a
    /// host shared one orchestrator for the life of the process, and a host validating scopes
    /// (the default in Development) refused to resolve the factory at all.
    /// </summary>
    [Fact]
    public async Task The_factory_resolves_under_scope_validation_and_each_run_gets_its_own_scope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICrewOrchestrationService>(_ => new FakeCrewOrchestrationService());
        DependencyInjection.ServiceCollectionExtensions.AddOrkeonAgentFramework(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var factory = provider.GetRequiredService<DependencyInjection.ICrewAgentFactory>();
        var loaded = new List<ICrewOrchestrationService>();
        Task<CrewId> Load(IServiceProvider scope, CancellationToken _)
        {
            // The loader registers the crew in the run's own scope: the orchestrator it sees
            // there is the one the run kicks off with.
            loaded.Add(scope.GetRequiredService<ICrewOrchestrationService>());
            return Task.FromResult(CrewId.Create());
        }

        var first = factory.Create(Load, "first", "the first crew");
        var second = factory.Create(Load, "second");
        var ct = TestContext.Current.CancellationToken;
        await first.RunAsync("one", cancellationToken: ct);
        await second.RunAsync("two", cancellationToken: ct);
        await first.RunAsync("three", cancellationToken: ct);

        Assert.Equal(3, loaded.Count);
        Assert.Equal(3, loaded.Distinct().Count());
        Assert.All(loaded, o => Assert.Single(((FakeCrewOrchestrationService)o).Kickoffs));
        Assert.Equal("first", first.Name);
        Assert.Equal("the first crew", first.Description);
        Assert.Equal("orkeon-crew-first", first.Id);
        Assert.Null(first.CrewId);
    }

    [Fact]
    public async Task A_scoped_crew_agent_kicks_off_the_crew_its_loader_registered()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICrewOrchestrationService>(_ => new FakeCrewOrchestrationService { FinalOutput = "scoped answer" });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var crewId = CrewId.Create();
        FakeCrewOrchestrationService? used = null;

        var agent = new CrewAgent(
            provider.GetRequiredService<IServiceScopeFactory>(),
            (scope, _) =>
            {
                used = (FakeCrewOrchestrationService)scope.GetRequiredService<ICrewOrchestrationService>();
                return Task.FromResult(crewId);
            },
            "scoped");

        var response = await agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("scoped answer", response.Text);
        var kickoff = Assert.Single(used!.Kickoffs);
        Assert.Equal(crewId, kickoff.CrewId);
        Assert.Equal("go", kickoff.Input.InitialContext);
    }
}
