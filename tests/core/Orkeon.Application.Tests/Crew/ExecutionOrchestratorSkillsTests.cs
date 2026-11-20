using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Skills;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew;

/// <summary>An agent's skills reach its system prompt, and the run is told which (#434).</summary>
public sealed class ExecutionOrchestratorSkillsTests
{
    private readonly FakeFileSystemService _files = new FakeFileSystemService()
        .AddMount("/workspace")
        .AddFile("/workspace/skills/review.md", "Always check the inputs first.");

    private readonly FakeDecisionModel _model = new();
    private readonly FakeAgentSkillsObserver _observer = new();
    private readonly ScriptedBasicLlmProvider _llm = new();

    private ExecutionOrchestrator Orchestrator(DecisionCircuit circuit)
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(_model).AddSingleton(circuit).BuildServiceProvider();
        return new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, _llm)
        {
            SkillsResolver = new AgentSkillsResolver(_files, services, new AgentSkillsOptions(), NullLogger.Instance),
            SkillsObserver = _observer,
        };
    }

    private static DomainTask Task() => DomainTask.Create(TaskDescription.From("Review the change."), ExpectedOutput.From("A verdict."));

    private static SimpleExecutionContext Context() =>
        new(CrewId.From(Guid.NewGuid()), new Dictionary<string, string>(), NullMemoryScope.Instance, [], CancellationToken.None);

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldCarryTheSkillInThePromptAndTellTheObserver_WhenTheAgentListsASkill()
    {
        // Arrange
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review code")
            .Skills(new AgentSkills { Paths = ["/workspace/skills/review.md"] }).Build();

        // Act
        await Orchestrator(DecisionCircuit.On).ExecuteTaskCoreAsync(agent, Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        var prompt = Assert.Single(_llm.ReceivedPrompts);
        Assert.Contains("## Skills", prompt, StringComparison.Ordinal);
        Assert.Contains("Always check the inputs first.", prompt, StringComparison.Ordinal);
        var notice = Assert.Single(_observer.Notices);
        Assert.Equal(AgentSkillsModes.List, notice.Mode);
        Assert.Equal("Reviewer", notice.AgentRole);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldRunWithNoSkillAndNoDecisionCall_WhenTheCircuitIsOffAndTheAutoAgentHasNoFallback()
    {
        // Arrange
        _files.AddFile($"{AgentSkillsOptions.DefaultLibrary}/review.md", "---\nname: review\n---\nBody.");
        var agent = new AgentBuilder().Role("Reviewer").Goal("Review code").Skills(new AgentSkills { Auto = true }).Build();

        // Act
        var result = await Orchestrator(DecisionCircuit.Off).ExecuteTaskCoreAsync(agent, Task(), Context(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(_model.Requests);
        Assert.DoesNotContain("## Skills", Assert.Single(_llm.ReceivedPrompts), StringComparison.Ordinal);
        Assert.Equal(AgentSkillsModes.Fallback, Assert.Single(_observer.Notices).Mode);
    }
}
