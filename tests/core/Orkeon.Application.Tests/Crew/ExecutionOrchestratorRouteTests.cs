using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Routing;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Tests.Shared.FileSystem;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew;

/// <summary>A task of an agent on <c>llm.profile: auto</c> runs on the profile System One picks at its start (#434).</summary>
public sealed class ExecutionOrchestratorRouteTests : IDisposable
{
    private readonly ScriptedFullLlmProvider _default = new();
    private readonly ScriptedFullLlmProvider _small = new();
    private readonly ScriptedFullLlmProvider _large = new();
    private readonly LlmProviderToChatClientAdapter _defaultClient;
    private readonly LlmProviderToChatClientAdapter _smallClient;
    private readonly LlmProviderToChatClientAdapter _largeClient;
    private readonly FakeDecisionModel _model = new();
    private readonly FakeAgentRouteObserver _observer = new();

    public ExecutionOrchestratorRouteTests()
    {
        _defaultClient = new LlmProviderToChatClientAdapter(_default);
        _smallClient = new LlmProviderToChatClientAdapter(_small);
        _largeClient = new LlmProviderToChatClientAdapter(_large);
    }

    public void Dispose()
    {
        _defaultClient.Dispose();
        _smallClient.Dispose();
        _largeClient.Dispose();
    }

    private ExecutionOrchestrator Orchestrator(DecisionCircuit circuit)
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(_model).AddSingleton(circuit).BuildServiceProvider();
        return new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(_default), _defaultClient, [], new FakeFileSystemService())
        {
            LlmProfiles = new StubLlmProfileRegistry(("small", _small, _smallClient), ("large", _large, _largeClient)),
            ProfileRouter = new AgentProfileRouter(services, new AgentRouteOptions { Ladder = ["small", "large"] }, NullLogger.Instance),
            RouteObserver = _observer,
        };
    }

    private static Orkeon.Domain.Agent.Agent AutoAgent() => new AgentBuilder().Role("Coder").Goal("Change code")
        .WithLlmConfig(LlmConfig.Create("m") with { Profile = LlmProfiles.Auto }).Build();

    private static DomainTask Task() => DomainTask.Create(TaskDescription.From("Design the cache layer."), ExpectedOutput.From("A design."));

    private static SimpleExecutionContext Context() => new(CrewId.Create(), [], NullMemoryScope.Instance, []);

    private void Answer(string choice, double probability) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add(AgentProfileRouter.QuestionName, new ChoiceAnswer
            {
                Choice = choice,
                Confidence = probability,
                Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, probability),
            }),
        };

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldRunTheTaskOnThePickedProfileAndTellTheObserver_WhenTheAgentIsOnAuto()
    {
        // Arrange
        Answer("large", 0.9);

        // Act
        var result = await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(AutoAgent(), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.NotEmpty(_large.ReceivedTurns);
        Assert.Empty(_default.ReceivedTurns);
        var notice = Assert.Single(_observer.Notices);
        Assert.Equal("large", notice.Profile);
        Assert.Equal(AgentRouteReasons.Model, notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldRunOnTheDefaultProfileWithNoDecisionCallAndNoError_WhenTheCircuitIsOff()
    {
        // Arrange
        Answer("large", 0.9);

        // Act
        var result = await Orchestrator(DecisionCircuit.Off).ExecuteTaskCoreAsync(AutoAgent(), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Empty(_model.Requests);
        Assert.NotEmpty(_default.ReceivedTurns);
        Assert.Empty(_large.ReceivedTurns);
        Assert.Equal(AgentRouteReasons.CircuitOff, Assert.Single(_observer.Notices).Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldAskOnceAndKeepTheProfile_WhenTheSameTaskRunsAgain()
    {
        // Arrange
        Answer("small", 0.9);
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = AutoAgent();
        var task = Task();
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);
        Answer("large", 0.9);

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
        Assert.Single(_observer.Notices);
        Assert.Empty(_large.ReceivedTurns);
        Assert.Empty(_default.ReceivedTurns);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPickItsOwnProfile_WhenAnotherAgentRunsTheSameTask()
    {
        // Arrange: two voters of a consensual crew on one task.
        Answer("small", 0.9);
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var task = Task();
        await orchestrator.ExecuteTaskCoreAsync(AutoAgent(), task, Context(), TestContext.Current.CancellationToken);
        Answer("large", 0.9);

        // Act
        await orchestrator.ExecuteTaskCoreAsync(AutoAgent(), task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["small", "large"], _observer.Notices.Select(n => n.Profile));
        Assert.NotEmpty(_large.ReceivedTurns);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPickAgain_WhenTheNextRunRunsTheSameTask()
    {
        // Arrange: each kickoff of a crew opens the scope of its run.
        Answer("small", 0.9);
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = AutoAgent();
        var task = Task();
        using (TaskPickScope.Begin())
            await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);
        Answer("large", 0.9);
        using var next = TaskPickScope.Begin();

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _model.Requests.Count);
        Assert.NotEmpty(_large.ReceivedTurns);
    }
}
