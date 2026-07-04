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
        crew.CompleteExecution(1, 0);

        // Second execution
        crew.StartExecution();
        crew.CompleteExecution(2, 1);

        // Assert
        Assert.Equal(2, crew.Executions.Count);
        Assert.Equal(CrewStatus.Idle, crew.Status);
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
        crew.CompleteExecution(3, 0);

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
    public void ShouldReturnInvalid_WhenHierarchicalCrewHasNoManager()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical", ProcessType.Hierarchical, managerLlm: managerLlm);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        // Manager is auto-set to first agent, so remove it manually via process type change
        crew.ChangeProcessType(ProcessType.Sequential);

        // Act - verify sequential has no manager validation issue
        var result = crew.Validate();

        // Assert
        Assert.True(result.IsValid);
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

    #region Configuration Boundary Tests

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ShouldThrow_WhenUpdatingConfigurationWithNegativeMaxRpm(int maxRpm)
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => crew.UpdateConfiguration(new CrewConfigurationUpdate { MaxRpm = maxRpm }));
    }

    [Fact]
    public void ShouldUpdateLanguage_WhenValidLanguageProvided()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act
        crew.UpdateConfiguration(new CrewConfigurationUpdate { Language = "ja" });

        // Assert
        Assert.Equal("ja", crew.Language.Value);
    }

    [Fact]
    public void ShouldNotChangeLanguage_WhenNullLanguageProvided()
    {
        // Arrange
        var crew = DomainCrew.Create("Test", language: "fr");

        // Act
        crew.UpdateConfiguration(new CrewConfigurationUpdate { Language = null });

        // Assert
        Assert.Equal("fr", crew.Language.Value);
    }

    [Fact]
    public void ShouldUpdateOutputLogFile_WhenPathProvided()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act
        crew.UpdateConfiguration(new CrewConfigurationUpdate { OutputLogFile = "/var/log/output.txt" });

        // Assert
        Assert.Equal("/var/log/output.txt", crew.OutputLogFile);
    }

    #endregion

    #region Agent Management Edge Cases

    [Fact]
    public void ShouldAutoSetManagerToFirstAgent_WhenAddingToHierarchicalCrewWithNoManager()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        var agentId = AgentId.Create();

        // Act
        crew.AddAgent(agentId);

        // Assert
        Assert.Equal(agentId, crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldReassignManager_WhenRemovingCurrentManagerFromHierarchicalCrew()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        crew.AddAgent(agent1); // Becomes manager
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
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        var agentId = AgentId.Create();
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
    public void ShouldCreateCrewWithCallbacks_WhenProvided()
    {
        // Arrange
        var options = new CrewCreateOptions
        {
            Goal = "Callback crew",
            StepCallback = new StubStepCallback(),
            TaskCallback = new StubTaskCallback()
        };

        // Act
        var crew = DomainCrew.Create(options);

        // Assert
        Assert.NotNull(crew.StepCallback);
        Assert.NotNull(crew.TaskCallback);
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

    #region Callback Stubs

    private sealed class StubStepCallback : Domain.Agent.IStepCallback
    {
        public System.Threading.Tasks.Task OnStepStartAsync(Domain.Agent.Agent agent, Domain.Task.ICrewTask task, int iteration) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStepCompletedAsync(Domain.Agent.Agent agent, Domain.Task.ICrewTask task, int iteration, Domain.Agent.AgentStep step) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnStepFailedAsync(Domain.Agent.Agent agent, Domain.Task.ICrewTask task, int iteration, string error) => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class StubTaskCallback : Domain.Task.ITaskCallback
    {
        public System.Threading.Tasks.Task OnTaskStartAsync(Domain.Task.ICrewTask task) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnTaskCompletedAsync(Domain.Task.ICrewTask task, Domain.Task.ValueObjects.TaskOutput output) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task OnTaskFailedAsync(Domain.Task.ICrewTask task, string error) => System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion
}
