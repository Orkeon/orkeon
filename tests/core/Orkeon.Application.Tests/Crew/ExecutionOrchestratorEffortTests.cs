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
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Tests.Shared.FileSystem;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew;

/// <summary>A task on <c>thinking: { effort: auto }</c> runs with the level System One picks at its start (#439).</summary>
public sealed class ExecutionOrchestratorEffortTests : IDisposable
{
    private static readonly LlmThinkingConfig ProfileThinking = new() { Effort = "low" };

    private readonly ScriptedFullLlmProvider _default = new();
    private readonly ScriptedFullLlmProvider _small = new() { BaseConfig = LlmConfig.OnProfile() with { Thinking = ProfileThinking } };
    private readonly ScriptedFullLlmProvider _large = new() { BaseConfig = LlmConfig.OnProfile() with { Thinking = ProfileThinking } };
    private readonly LlmProviderToChatClientAdapter _defaultClient;
    private readonly LlmProviderToChatClientAdapter _smallClient;
    private readonly LlmProviderToChatClientAdapter _largeClient;
    private readonly FakeDecisionModel _model = new();
    private readonly FakeAgentRouteObserver _observer = new();

    public ExecutionOrchestratorEffortTests()
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
            ProfileRouter = new AgentProfileRouter(
                services, new AgentRouteOptions { Ladder = ["small", "large"] }, new AgentEffortOptions(), NullLogger.Instance),
            RouteObserver = _observer,
        };
    }

    private static Orkeon.Domain.Agent.Agent Agent(string profile, LlmThinkingConfig? thinking) => new AgentBuilder()
        .Role("Coder").Goal("Change code").WithLlmConfig(LlmConfig.OnProfile(profile) with { Thinking = thinking }).Build();

    private static Orkeon.Domain.Agent.Agent AutoEffortAgent(string profile = "small") =>
        Agent(profile, new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Any });

    private static DomainTask Task() => DomainTask.Create(TaskDescription.From("Design the cache layer."), ExpectedOutput.From("A design."));

    private static SimpleExecutionContext Context() => new(CrewId.Create(), [], NullMemoryScope.Instance, []);

    private static ChoiceAnswer Choice(string choice, double probability) => new()
    {
        Choice = choice,
        Confidence = probability,
        Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, probability),
    };

    private void Answer(params (string Question, string Choice, double Probability)[] answers) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = answers.ToImmutableDictionary(a => a.Question, a => (DecisionAnswer)Choice(a.Choice, a.Probability)),
        };

    private static string? SentEffort(ScriptedFullLlmProvider provider) => Assert.Single(provider.ReceivedConfigs)?.Thinking?.Effort;

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldSendThePickedLevelAndTellTheObserver_WhenTheAgentIsOnEffortAuto()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.9));

        // Act
        var result = await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(AutoEffortAgent(), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Equal("high", SentEffort(_small));
        var notice = Assert.Single(_observer.EffortNotices);
        Assert.Equal("high", notice.Level);
        Assert.Equal(AgentEffortReasons.Model, notice.Reason);
        Assert.Empty(_observer.Notices);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldSendTheProfilesLevelWithNoDecisionCall_WhenTheCircuitIsOff()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.9));

        // Act
        var result = await Orchestrator(DecisionCircuit.Off).ExecuteTaskCoreAsync(AutoEffortAgent(), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, result.Error);
        Assert.Empty(_model.Requests);
        Assert.Equal("low", SentEffort(_small));
        Assert.Equal(AgentEffortReasons.CircuitOff, Assert.Single(_observer.EffortNotices).Reason);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldSendTheProfilesLevel_WhenThePickIsUnderTheBar()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.4));

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(AutoEffortAgent(), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("low", SentEffort(_small));
        var notice = Assert.Single(_observer.EffortNotices);
        Assert.Equal("low", notice.Level);
        Assert.Equal(AgentEffortReasons.LowConfidence, notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldSendTheAgentsOwnLevel_WhenTheTaskLeavesTheEffortToSystemOneAndGetsNoPick()
    {
        // Arrange
        var task = new CrewTaskBuilder().Description("Design the cache layer.").ExpectedOutput("A design.")
            .WithLlmOverride(new LlmConfigOverride { Thinking = new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Any } })
            .Build();

        // Act
        await Orchestrator(DecisionCircuit.Off).ExecuteTaskCoreAsync(
            Agent("small", new LlmThinkingConfig { Effort = "medium" }), task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("medium", SentEffort(_small));
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldPickTheProfileAndTheLevelInOneCall_WhenBothAreAuto()
    {
        // Arrange
        Answer((AgentProfileRouter.QuestionName, "large", 0.9), (AgentProfileRouter.EffortQuestionName, "medium", 0.9));

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(AutoEffortAgent(LlmProfiles.Auto), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
        Assert.Equal("medium", SentEffort(_large));
        Assert.Equal("large", Assert.Single(_observer.Notices).Profile);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldAskOnceAndKeepTheLevel_WhenTheSameTaskRunsAgain()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.9));
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = AutoEffortAgent();
        var task = Task();
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);
        Answer((AgentProfileRouter.EffortQuestionName, "low", 0.9));

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
        Assert.Equal(["high", "high"], _small.ReceivedConfigs.Select(c => c?.Thinking?.Effort));
    }
}
