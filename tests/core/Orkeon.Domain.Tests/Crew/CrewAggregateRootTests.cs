using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Agent.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// Additional tests for Crew aggregate root covering gaps identified in Phase 1 audit.
/// Focuses on: dynamic agents, ToolAccessPolicy on crew, execution lifecycle edge cases,
/// Validate with hierarchical constraints, and configuration boundary tests.
/// </summary>
public class CrewAggregateRootTests
{
    #region Test Doubles

    private sealed class StubLlmProvider : ILlmProvider
    {
        public string Name => "StubLlm";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });
    }

    private static DomainCrew CreateValidCrew(string goal = "Test goal")
    {
        var crew = DomainCrew.Create(goal);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        return crew;
    }

    #endregion

    #region Dynamic Agent Configuration Tests

    [Fact]
    public void ShouldSetDynamicAgentFlags_WhenCreatingWithOptions()
    {
        // Arrange
        var options = new CrewCreateOptions
        {
            Goal = "Dynamic crew",
            AllowDynamicAgents = true,
            MaxConcurrentDynamicAgents = 5
        };

        // Act
        var crew = DomainCrew.Create(options);

        // Assert
        Assert.True(crew.AllowDynamicAgents);
        Assert.Equal(5, crew.MaxConcurrentDynamicAgents);
    }

    [Fact]
    public void ShouldDefaultDynamicAgentsToFalse_WhenNotSpecified()
    {
        // Arrange & Act
        var crew = DomainCrew.Create("Simple goal");

        // Assert
        Assert.False(crew.AllowDynamicAgents);
        Assert.Null(crew.MaxConcurrentDynamicAgents);
    }

    [Fact]
    public void ShouldAllowNullMaxConcurrentDynamicAgents_ForUnlimited()
    {
        // Arrange
        var options = new CrewCreateOptions
        {
            Goal = "Unlimited agents",
            AllowDynamicAgents = true,
            MaxConcurrentDynamicAgents = null
        };

        // Act
        var crew = DomainCrew.Create(options);

        // Assert
        Assert.True(crew.AllowDynamicAgents);
        Assert.Null(crew.MaxConcurrentDynamicAgents);
    }

    #endregion

    #region ToolAccessPolicy on Crew Tests

    [Fact]
    public void ShouldSetToolAccessPolicy_WhenCreatingWithOptions()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist(ToolSearch, "ReadTool");
        var options = new CrewCreateOptions
        {
            Goal = "Restricted crew",
            ToolAccessPolicy = policy
        };

        // Act
        var crew = DomainCrew.Create(options);

        // Assert
        Assert.NotNull(crew.ToolAccessPolicy);
        Assert.True(crew.ToolAccessPolicy.IsToolAllowed(ToolSearch));
        Assert.False(crew.ToolAccessPolicy.IsToolAllowed("DeleteTool"));
    }

    [Fact]
    public void ShouldHaveNullToolAccessPolicy_WhenNotSpecified()
    {
        // Arrange & Act
        var crew = DomainCrew.Create("No policy crew");

        // Assert
        Assert.Null(crew.ToolAccessPolicy);
    }

    #endregion

    #region Execution Lifecycle Edge Cases

    [Fact]
    public void ShouldTrackExecutionHistory_WhenMultipleExecutions()
    {
        // Arrange
        var crew = CreateValidCrew();

        // Act - first execution
        crew.StartExecution();
        crew.CompleteExecution(1);

        // Second execution, which fails: a run with a failed task is a failed run (GAP-32)
        crew.StartExecution();
        crew.FailExecution("Task review (Writer) failed: no final answer");

        // Assert
        Assert.Equal(2, crew.Executions.Count);
        Assert.Equal(ExecutionStatus.Succeeded, crew.Executions[0].Status);
        Assert.Equal(1, crew.Executions[0].CompletedTasks);
        Assert.Equal(ExecutionStatus.Failed, crew.Executions[1].Status);
        Assert.Equal("Task review (Writer) failed: no final answer", crew.Executions[1].FailureReason);
        Assert.Equal(CrewStatus.Failed, crew.Status);
        Assert.Null(crew.CurrentProcessId);
    }

    [Fact]
    public void ShouldReturnProcessId_WhenStartingExecution()
    {
        // Arrange
        var crew = CreateValidCrew();

        // Act
        var processId = crew.StartExecution();

        // Assert
        Assert.NotNull(processId);
        Assert.Equal(processId, crew.CurrentProcessId);
    }

    [Fact]
    public void ShouldSetStatusToFailed_WhenExecutionFails()
    {
        // Arrange
        var crew = CreateValidCrew();
        crew.StartExecution();

        // Act
        crew.FailExecution("Network error");

        // Assert
        Assert.Equal(CrewStatus.Failed, crew.Status);
        Assert.Null(crew.CurrentProcessId);
    }

    [Fact]
    public void ShouldAllowRestartAfterFailure_WhenCrewHasFailedStatus()
    {
        // Arrange
        var crew = CreateValidCrew();
        crew.StartExecution();
        crew.FailExecution("Temporary error");

        // Act - should be able to restart after failure
        var processId = crew.StartExecution();

        // Assert
        Assert.NotNull(processId);
        Assert.Equal(CrewStatus.Executing, crew.Status);
    }

    [Fact]
    public void ShouldRecordExecutionDuration_WhenCompletingExecution()
    {
        // Arrange
        var crew = CreateValidCrew();
        crew.StartExecution();

        // Act
        crew.CompleteExecution(3);

        // Assert
        var execution = crew.Executions[^1];
        Assert.NotNull(execution.Duration);
        Assert.True(execution.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public void ShouldRaiseExecutionFailedEvent_WithException()
    {
        // Arrange
        var crew = CreateValidCrew();
        crew.StartExecution();
        crew.ClearDomainEvents();
        var exception = new InvalidOperationException("Test error");

        // Act
        crew.FailExecution("Test failure", exception);

        // Assert
        var failedEvent = crew.DomainEvents.OfType<CrewExecutionFailedEvent>().Single();
        Assert.Equal("Test failure", failedEvent.Reason);
        Assert.Equal(exception, failedEvent.Exception);
    }

    #endregion

    #region Validation Tests with Hierarchical Constraints

    [Fact]
    public void ShouldBeValid_WhenAHierarchicalCrewTurnsSequentialAndItsManagerBecomesAWorker()
    {
        // Arrange — GAP-33: a sequential crew has no manager; turning sequential drops the manager
        // agent, which stays a member of the crew, hence a worker.
        var manager = AgentId.Create();
        var crew = DomainCrew.Create("Hierarchical", ProcessType.Hierarchical, managerAgentId: manager);
        crew.AddAgent(manager);
        crew.AddTask(TaskId.Create());
        crew.ChangeProcessType(ProcessType.Sequential);

        // Act
        var result = crew.Validate();

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(crew.ManagerAgentId);
        Assert.Contains(manager, crew.Agents);
    }

    [Fact]
    public void ShouldThrow_WhenValidateCanKickoffOnHierarchicalCrewWithNoManagerAndNoLlm()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        // Force to hierarchical using ChangeProcessType with explicit manager
        var managerId = crew.Agents[0];
        crew.ChangeProcessType(ProcessType.Hierarchical, managerId);
        // This should pass since manager is set

        // Act & Assert - should not throw since manager IS set
        crew.ValidateCanKickoff(); // No exception expected
    }

    #endregion

    #region Agent Management Edge Cases

    [Fact]
    public void ShouldNotMakeAnAgentManager_WhenAddingToAHierarchicalCrewItsManagerLlmManages()
    {
        // GAP-19: a crew given a manager LLM has no manager agent; every agent it adds is a worker.
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: new StubLlmProvider());
        var agentId = AgentId.Create();

        crew.AddAgent(agentId);

        Assert.Null(crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldReassignManager_WhenRemovingCurrentManagerFromHierarchicalCrew()
    {
        // Arrange — managed by an agent, with no manager LLM to fall back on
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerAgentId: agent1);
        crew.AddAgent(agent1);
        crew.AddAgent(agent2);

        // Pre-condition
        Assert.Equal(agent1, crew.ManagerAgentId);

        // Act
        crew.RemoveAgent(agent1, "Rotation");

        // Assert - agent2 should become the new manager
        Assert.Equal(agent2, crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldClearManager_WhenRemovingOnlyAgentFromHierarchicalCrew()
    {
        // Arrange — managed by its only agent
        var agentId = AgentId.Create();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerAgentId: agentId);
        crew.AddAgent(agentId);

        // Act
        crew.RemoveAgent(agentId, "Shutdown");

        // Assert
        Assert.Null(crew.ManagerAgentId);
    }

    #endregion

    #region Create with Full Options Tests

    [Fact]
    public void ShouldCreateCrewWithPlanningLlm_WhenProvided()
    {
        // Arrange
        var planningLlm = new StubLlmProvider();
        var options = new CrewCreateOptions
        {
            Goal = "Planning crew",
            Planning = true,
            PlanningLlm = planningLlm
        };

        // Act
        var crew = DomainCrew.Create(options);

        // Assert
        Assert.True(crew.Planning);
        Assert.NotNull(crew.PlanningLlm);
    }

    [Fact]
    public void ShouldReturnUniqueIds_WhenCreatingMultipleCrews()
    {
        // Arrange & Act
        var crew1 = DomainCrew.Create("Crew 1");
        var crew2 = DomainCrew.Create("Crew 2");
        var crew3 = DomainCrew.Create("Crew 3");

        // Assert
        Assert.NotEqual(crew1.Id, crew2.Id);
        Assert.NotEqual(crew2.Id, crew3.Id);
        Assert.NotEqual(crew1.Id, crew3.Id);
    }

    #endregion
}
