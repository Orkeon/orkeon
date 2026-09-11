using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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

    [Fact]
    public void The_factory_registered_by_AddOrkeonAgentFramework_builds_agents_for_registered_crews()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ICrewOrchestrationService>(services, new FakeCrewOrchestrationService());
        DependencyInjection.ServiceCollectionExtensions.AddOrkeonAgentFramework(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        var factory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DependencyInjection.ICrewAgentFactory>(provider);
        var crewId = CrewId.Create();
        var agent = factory.Create(crewId, "named");

        Assert.Equal(crewId, agent.CrewId);
        Assert.Equal("named", agent.Name);
    }
}
