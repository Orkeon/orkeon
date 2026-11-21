using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Routing;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.TestDoubles;
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
#pragma warning disable ORKEXP005 // The reasoning capabilities are experimental; a model that does not think is refused (#439).
    private readonly ScriptedFullLlmProvider _flat = new() { Reasoning = new LlmReasoningCapabilities("flat-1", true, ThinkingSupport.None, []) };
#pragma warning restore ORKEXP005
    private readonly LlmProviderToChatClientAdapter _flatClient;
    private readonly FakeDecisionModel _model = new();
    private readonly FakeAgentRouteObserver _observer = new();
    private readonly StubOutputValidationPipeline _validation = new();

    public ExecutionOrchestratorEffortTests()
    {
        _defaultClient = new LlmProviderToChatClientAdapter(_default);
        _smallClient = new LlmProviderToChatClientAdapter(_small);
        _largeClient = new LlmProviderToChatClientAdapter(_large);
        _flatClient = new LlmProviderToChatClientAdapter(_flat);
    }

    public void Dispose()
    {
        _defaultClient.Dispose();
        _smallClient.Dispose();
        _largeClient.Dispose();
        _flatClient.Dispose();
    }

    private ExecutionOrchestrator Orchestrator(DecisionCircuit circuit)
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(_model).AddSingleton(circuit).BuildServiceProvider();
        return new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(_default), _defaultClient, [],
            _validation, new StubOutputParserFactory(), new FakeFileSystemService())
        {
            LlmProfiles = new StubLlmProfileRegistry(("small", _small, _smallClient), ("large", _large, _largeClient), ("flat", _flat, _flatClient)),
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

    /// <summary>The next attempt ends with an empty answer, its retry included: a failed result.</summary>
    private void FailNextAttempt()
    {
        _small.EnqueueText(string.Empty);
        _small.EnqueueText(string.Empty);
    }

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

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldRaiseTheLevelOneStepAndTellTheObserver_WhenTheAttemptBeforeFailed()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "low", 0.9));
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = AutoEffortAgent();
        var task = Task();
        FailNextAttempt();
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
        Assert.Equal("medium", _small.ReceivedConfigs[^1]?.Thinking?.Effort);
        var raised = _observer.EffortNotices[^1];
        Assert.Equal(AgentEffortReasons.Raised, raised.Reason);
        Assert.Equal("low", raised.Previous);
        Assert.Equal("medium", raised.Level);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldKeepTheTopLevelWithNoRaise_WhenTheAttemptBeforeFailedAtTheHighestLevel()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.9));
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = AutoEffortAgent();
        var task = Task();
        FailNextAttempt();
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("high", _small.ReceivedConfigs[^1]?.Thinking?.Effort);
        Assert.DoesNotContain(_observer.EffortNotices, n => n.Reason == AgentEffortReasons.Raised);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldSwitchThinkingOff_WhenSystemOnePicksOffForAnAgentOnEnabledAuto()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, LlmThinkingConfig.Off, 0.9));
        var agent = Agent("small", new LlmThinkingConfig { AutoEnabled = true, Effort = "medium" });

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(agent, Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Single(_small.ReceivedConfigs)?.Thinking?.Enabled);
        Assert.Equal(LlmThinkingConfig.Off, Assert.Single(_observer.EffortNotices).Level);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldSwitchThinkingBackOnAtTheWrittenLevel_WhenAnAttemptWithThinkingOffFailed()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, LlmThinkingConfig.Off, 0.9));
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = Agent("small", new LlmThinkingConfig { AutoEnabled = true, Effort = "medium" });
        var task = Task();
        FailNextAttempt();
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        var sent = _small.ReceivedConfigs[^1]?.Thinking;
        Assert.True(sent?.Enabled);
        Assert.Equal("medium", sent?.Effort);
        Assert.Equal(LlmThinkingConfig.Off, _observer.EffortNotices[^1].Previous);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldRaiseTheLevelForTheNextRoundAndTellTheObserver_WhenTheOutputFailedItsGuardrail()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "low", 0.9));
        _validation.RejectFirst = 1;
        var task = new CrewTaskBuilder().Description("Design the cache layer.").ExpectedOutput("A design.")
            .OutputJson(JsonSchema.From("{\"type\":\"object\"}")).Build();

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(AutoEffortAgent(), task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
        Assert.Equal(["low", "medium"], _small.ReceivedConfigs.Select(c => c?.Thinking?.Effort));
        var raised = _observer.EffortNotices[^1];
        Assert.Equal(AgentEffortReasons.Raised, raised.Reason);
        Assert.Equal("low", raised.Previous);
        Assert.Equal("medium", raised.Level);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldRaiseOncePerFailedRound_WhenTheOutputFailsItsGuardrailTwice()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "low", 0.9));
        _validation.RejectFirst = 2;
        var task = new CrewTaskBuilder().Description("Design the cache layer.").ExpectedOutput("A design.")
            .OutputJson(JsonSchema.From("{\"type\":\"object\"}")).Build();
        var orchestrator = Orchestrator(DecisionCircuit.On);
        orchestrator.MaxOutputRetries = 2;

        // Act
        await orchestrator.ExecuteTaskCoreAsync(AutoEffortAgent(), task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["low", "medium", "high"], _small.ReceivedConfigs.Select(c => c?.Thinking?.Effort));
        Assert.Equal(2, _observer.EffortNotices.Count(n => n.Reason == AgentEffortReasons.Raised));
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldRaiseTheLevel_WhenTheAttemptBeforeThrew()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "low", 0.9));
        var orchestrator = Orchestrator(DecisionCircuit.On);
        var agent = AutoEffortAgent();
        var task = Task();
        _small.Throws = new InvalidOperationException("The provider is down.");
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);
        _small.Throws = null;

        // Act
        await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("medium", _small.ReceivedConfigs[^1]?.Thinking?.Effort);
        Assert.Equal(AgentEffortReasons.Raised, _observer.EffortNotices[^1].Reason);
    }

    public static TheoryData<AgentExitReason> ExitsThatDoNotRaise =>
    [
        AgentExitReason.Cancelled,
        AgentExitReason.GuardianBlocked,
        AgentExitReason.BudgetExhausted,
        AgentExitReason.ExecutionTimeExceeded,
    ];

    [Theory]
    [MemberData(nameof(ExitsThatDoNotRaise))]
    [Trait("BR", "CH-439")]
    public void ShouldNotCountTheAttemptForARaise_WhenItEndedOnACancellationARefusalABudgetOrATimeout(AgentExitReason exit)
    {
        // Arrange
        var result = new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, "stopped") { ExitReason = exit };

        // Act
        var failed = ExecutionOrchestrator.FailedForRaise(result);

        // Assert
        Assert.False(failed);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public void ShouldCountTheAttemptForARaise_WhenItEndedOnAFailedResult()
    {
        // Arrange
        var result = new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, "empty answer") { ExitReason = AgentExitReason.EmptyFinalAnswer };

        // Act
        var failed = ExecutionOrchestrator.FailedForRaise(result);

        // Assert
        Assert.True(failed);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldAskNothingAndKeepThinkingOff_WhenTheAgentWritesThinkingOffUnderEffortAuto()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.9));
        var agent = Agent("small", new LlmThinkingConfig { Enabled = false, AutoEffort = LlmAutoEffort.Any });

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(agent, Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(new LlmThinkingConfig { Enabled = false }, Assert.Single(_small.ReceivedConfigs)?.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldAskNothingAndKeepThinkingOff_WhenTheTaskLeavesTheEffortToSystemOneOverAnAgentWithThinkingOff()
    {
        // Arrange
        Answer((AgentProfileRouter.EffortQuestionName, "high", 0.9));
        var task = new CrewTaskBuilder().Description("Design the cache layer.").ExpectedOutput("A design.")
            .WithLlmOverride(new LlmConfigOverride { Thinking = new LlmThinkingConfig { AutoEffort = LlmAutoEffort.Any } })
            .Build();

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(
            Agent("small", new LlmThinkingConfig { Enabled = false }), task, Context(), TestContext.Current.CancellationToken);

        // Assert: only enabled: auto lets System One switch on a thinking written off.
        Assert.Empty(_model.Requests);
        Assert.Equal(new LlmThinkingConfig { Enabled = false }, Assert.Single(_small.ReceivedConfigs)?.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldFailTheTaskBeforeAnyCall_WhenACrewBuiltInCSharpBoundsThePickOutsideTheHostsLevels()
    {
        // Arrange
        var agent = new AgentBuilder().Role("Coder").Goal("Change code").WithLlmConfig(LlmConfig.OnProfile("small"))
            .ThinkingEffortAuto(min: "xhigh").Build();

        // Act
        var result = await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(agent, Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("keep none of the levels the host offers (low, medium, high", result.Error, StringComparison.Ordinal);
        Assert.Empty(_model.Requests);
        Assert.Empty(_small.ReceivedConfigs);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldFailTheTaskBeforeAnyCall_WhenACrewBuiltInCSharpLeavesTheEffortOfAModelThatDoesNotThink()
    {
        // Act
        var result = await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(AutoEffortAgent("flat"), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("does not think (thinking support None)", result.Error, StringComparison.Ordinal);
        Assert.Empty(_model.Requests);
        Assert.Empty(_flat.ReceivedConfigs);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldWarnOnceAndSendTheProfilesLevel_WhenTheOrchestratorHasNoRouter()
    {
        // Arrange
        var logger = new TestLogger<ExecutionOrchestrator>();
        var orchestrator = new ExecutionOrchestrator(
            logger, new LlmProviderAdapter(_default), _defaultClient, [], _validation, new StubOutputParserFactory(), new FakeFileSystemService())
        {
            LlmProfiles = new StubLlmProfileRegistry(("small", _small, _smallClient)),
        };

        // Act
        await orchestrator.ExecuteTaskCoreAsync(AutoEffortAgent(), Task(), Context(), TestContext.Current.CancellationToken);
        await orchestrator.ExecuteTaskCoreAsync(AutoEffortAgent(), Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["low", "low"], _small.ReceivedConfigs.Select(c => c?.Thinking?.Effort));
        Assert.Single(logger.LogEntries, e => e.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning
            && e.Message.Contains("no profile router", StringComparison.Ordinal));
    }
}
