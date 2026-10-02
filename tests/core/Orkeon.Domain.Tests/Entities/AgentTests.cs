using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tests.Fixtures;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Entities;

/// <summary>
/// Tests for Agent entity following Clean Architecture principles.
/// Tests the business rules and domain logic of the Agent aggregate root.
/// </summary>
public class AgentTests
{
    [Fact]
    public void ShouldCreateAgent_WhenCreatingWithValidParameters()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role(RoleSeniorDeveloper)
            .Goal("Build high-quality software")
            .Backstory("Experienced developer with 10+ years")
            .Build();

        // Assert
        Assert.NotNull(agent);
        Assert.NotEqual<object>(Guid.Empty, agent.Id.Value);
        Assert.Equal(RoleSeniorDeveloper, agent.Role.Value);
        Assert.Equal("Build high-quality software", agent.Goal.Value);
        Assert.Equal("Experienced developer with 10+ years", agent.Backstory?.Value);
        Assert.Equal(AgentStatus.Idle, agent.Status);
        Assert.False(agent.AllowDelegation);
        Assert.Equal(15, agent.MaxIterations);
        Assert.Equal(10, agent.MaxRpm);
        Assert.True(agent.CacheEnabled);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingWithNullRole()
    {
        // Arrange
        var goal = AgentGoal.From("Valid goal");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DomainAgent.Create(null!, goal)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("options.Role", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingWithNullGoal()
    {
        // Arrange
        var role = AgentRole.From("Valid role");

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => DomainAgent.Create(role, null!)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("options.Goal", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithInvalidMaxIterations()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From(GoalCodeEfficiently);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DomainAgent.Create(role, goal, maxIterations: 0)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithInvalidMaxRpm()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From(GoalCodeEfficiently);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DomainAgent.Create(role, goal, maxRpm: -1)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithInvalidMaxRetryLimit()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From(GoalCodeEfficiently);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DomainAgent.Create(role, goal, maxRetryLimit: 0)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void ShouldSetAllowDelegationTrue_WhenCreatingWithDelegationEnabled()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role("Team Lead")
            .Goal("Coordinate team efforts")
            .AllowDelegation()
            .Build();

        // Assert
        Assert.True(agent.AllowDelegation);
    }

    [Fact]
    public void ShouldSetAllParameters_WhenCreatingWithCustomParameters()
    {
        // Arrange
        var backstory = "Expert in distributed systems";
        var maxIterations = 5;
        var maxRpm = 20;
        var verbose = true;
        var maxExecutionTime = TimeSpan.FromMinutes(30);
        var cacheEnabled = false;
        var systemTemplate = "You are a senior architect";
        var promptTemplate = "Design: {input}";
        var responseTemplate = "Solution: {output}";
        var maxRetryLimit = 3;

        // Act
        var agent = new AgentBuilder()
            .Role("Senior Architect")
            .Goal("Design scalable systems")
            .Backstory(backstory)
            .AllowDelegation()
            .MaxIterations(maxIterations)
            .MaxRpm(maxRpm)
            .Verbose(verbose)
            .MaxExecutionTime(maxExecutionTime)
            .CacheEnabled(cacheEnabled)
            .SystemTemplate(systemTemplate)
            .PromptTemplate(promptTemplate)
            .ResponseTemplate(responseTemplate)
            .MaxRetryLimit(maxRetryLimit)
            .Build();

        // Assert
        Assert.Equal(backstory, agent.Backstory?.Value);
        Assert.True(agent.AllowDelegation);
        Assert.Equal(maxIterations, agent.MaxIterations);
        Assert.Equal(maxRpm, agent.MaxRpm);
        Assert.Equal(verbose, agent.Verbose);
        Assert.Equal(maxExecutionTime, agent.MaxExecutionTime);
        Assert.Equal(cacheEnabled, agent.CacheEnabled);
        Assert.Equal(systemTemplate, agent.SystemTemplate);
        Assert.Equal(promptTemplate, agent.PromptTemplate);
        Assert.Equal(responseTemplate, agent.ResponseTemplate);
        Assert.Equal(maxRetryLimit, agent.MaxRetryLimit);
    }

    [Fact]
    public void ShouldGenerateUniqueAgentIds_WhenCreating()
    {
        // Arrange & Act
        var agent1 = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalCodeEfficiently)
            .Build();
        var agent2 = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalCodeEfficiently)
            .Build();

        // Assert
        Assert.NotEqual(agent1.Id.Value, agent2.Id.Value);
    }

    [Fact]
    public void ShouldReturnAgentIdAsString_WhenUsingIdProperty()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act
        var id = agent.Id;
        var stringId = agent.Id.ToString();

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(stringId);
        // StringId should contain the GUID value
        Assert.Contains(agent.Id.Value.ToString(), stringId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(100)]
    public void ShouldAcceptValue_WhenCreatingWithValidMaxIterations(int maxIterations)
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalCodeEfficiently)
            .MaxIterations(maxIterations)
            .Build();

        // Assert
        Assert.Equal(maxIterations, agent.MaxIterations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(1000)]
    public void ShouldAcceptValue_WhenCreatingWithValidMaxRpm(int maxRpm)
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalCodeEfficiently)
            .MaxRpm(maxRpm)
            .Build();

        // Assert
        Assert.Equal(maxRpm, agent.MaxRpm);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingToolsCollection()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act
        var tools = agent.Tools;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<IBaseTool>>(tools);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingAssignedTasksCollection()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act
        var tasks = agent.AssignedTasks;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<TaskId>>(tasks);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingCurrentTasksCollection()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act
        var currentTasks = agent.CurrentTasks;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<TaskId>>(currentTasks);
    }

    [Fact]
    public void ShouldBeEmpty_WhenUsingCurrentTasksInitially()
    {
        // Arrange & Act
        var agent = AgentFixtures.CreateResearcherAgent();

        // Assert
        Assert.Empty(agent.CurrentTasks);
    }

    #region ValidateForExecution Tests

    [Fact]
    public void ShouldReturnSuccess_WhenValidatingForExecutionWithToolsAndDelegation()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .AllowDelegation()
            .WithTool(new StubTool("CodeTool"))
            .Build();

        // Act
        var result = agent.ValidateForExecution();

        // Assert
        Assert.True(result.CanExecute);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ShouldReturnFailure_WhenValidatingForExecutionWithNoTools()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .AllowDelegation()
            .Build();

        // Act
        var result = agent.ValidateForExecution();

        // Assert
        Assert.False(result.CanExecute);
        Assert.Contains("Agent has no tools assigned", result.Issues);
    }

    [Fact]
    public void ShouldReturnSuccess_WhenValidatingForExecutionWithToolsButNoDelegation()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .WithTool(new StubTool("CodeTool"))
            .Build(); // AllowDelegation defaults to false

        // Act
        var result = agent.ValidateForExecution();

        // Assert — delegation is not a prerequisite for execution
        Assert.True(result.CanExecute);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ShouldReturnSingleIssue_WhenValidatingForExecutionWithNoToolsAndNoDelegation()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .Build();

        // Act
        var result = agent.ValidateForExecution();

        // Assert — only tool availability is checked, not delegation
        Assert.False(result.CanExecute);
        Assert.Single(result.Issues);
        Assert.Contains("Agent has no tools assigned", result.Issues);
    }

    #endregion

    #region Task Lifecycle Tests

    [Fact]
    public void ShouldAssignTask_WhenTaskIsValid()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();

        // Act
        agent.AssignTask(taskId);

        // Assert
        Assert.Contains(taskId, agent.AssignedTasks);
        Assert.Single(agent.AssignedTasks);
    }

    [Fact]
    public void ShouldThrow_WhenAssigningNullTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => agent.AssignTask(null!));
    }

    [Fact]
    public void ShouldThrow_WhenAssigningDuplicateTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => agent.AssignTask(taskId));
        Assert.Contains("already assigned", ex.Message);
    }

    [Fact]
    public void ShouldAssignAnotherTask_WhileExecutingOne()
    {
        // GAP-21 — a parallel wave gives an agent every task declared for it: an agent busy with
        // one task can be given the next.
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId1 = TaskId.Create();
        agent.AssignTask(taskId1);
        agent.StartTask(taskId1);

        var taskId2 = TaskId.Create();
        agent.AssignTask(taskId2);

        Assert.Equal([taskId1, taskId2], agent.AssignedTasks);
    }

    [Fact]
    public void ShouldRaiseAssignedEvent_WhenAssigningTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        agent.ClearDomainEvents(); // Clear the AgentCreatedEvent
        var taskId = TaskId.Create();

        // Act
        agent.AssignTask(taskId);

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var assignedEvent = Assert.IsType<AgentAssignedToTaskEvent>(domainEvent);
        Assert.Equal(agent.Id, assignedEvent.AgentId);
        Assert.Equal(taskId, assignedEvent.TaskId);
    }

    [Fact]
    public void ShouldStartTask_WhenTaskIsAssigned()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);

        // Act
        agent.StartTask(taskId);

        // Assert
        Assert.Equal([taskId], agent.CurrentTasks);
        Assert.Equal(AgentStatus.Busy, agent.Status);
    }

    [Fact]
    public void ShouldThrow_WhenStartingNullTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => agent.StartTask(null!));
    }

    [Fact]
    public void ShouldThrow_WhenStartingUnassignedTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => agent.StartTask(taskId));
        Assert.Contains("not assigned", ex.Message);
    }

    [Fact]
    public void ShouldRunTwoTasksAtOnce_WhenAParallelWaveGivesItTwo()
    {
        // GAP-21 — the parallel mode runs a wave's tasks concurrently, the same agent's included.
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        agent.AssignTask(taskId1);
        agent.AssignTask(taskId2);
        agent.StartTask(taskId1);

        agent.StartTask(taskId2);

        Assert.Equal([taskId1, taskId2], agent.CurrentTasks);
        Assert.Equal(AgentStatus.Busy, agent.Status);
    }

    [Fact]
    public void ShouldStayBusy_UntilEveryTaskItRunsHasEnded()
    {
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        agent.AssignTask(taskId1);
        agent.AssignTask(taskId2);
        agent.StartTask(taskId1);
        agent.StartTask(taskId2);

        agent.CompleteTask(taskId1, TaskOutput.Text("first"));
        Assert.Equal(AgentStatus.Busy, agent.Status);
        Assert.Equal([taskId2], agent.CurrentTasks);

        agent.FailTask(taskId2, "second failed");
        Assert.Equal(AgentStatus.Idle, agent.Status);
        Assert.Empty(agent.CurrentTasks);
    }

    [Fact]
    public void ShouldThrow_WhenStartingATaskItIsAlreadyRunning()
    {
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        var ex = Assert.Throws<InvalidOperationException>(() => agent.StartTask(taskId));
        Assert.Contains("already executing", ex.Message);
    }

    [Fact]
    public void ShouldRaiseStartedEvent_WhenStartingTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.ClearDomainEvents();

        // Act
        agent.StartTask(taskId);

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var startedEvent = Assert.IsType<AgentStartedTaskEvent>(domainEvent);
        Assert.Equal(agent.Id, startedEvent.AgentId);
        Assert.Equal(taskId, startedEvent.TaskId);
    }

    [Fact]
    public void ShouldCompleteTask_WhenTaskIsExecuting()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);
        var output = TaskOutput.Text("Task completed successfully");

        // Act
        agent.CompleteTask(taskId, output);

        // Assert
        Assert.Empty(agent.CurrentTasks);
        Assert.Equal(AgentStatus.Idle, agent.Status);
    }

    [Fact]
    public void ShouldThrow_WhenCompletingWithNullOutput()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => agent.CompleteTask(taskId, null!));
    }

    [Fact]
    public void ShouldThrow_WhenCompletingATaskItIsNotRunning()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var output = TaskOutput.Text("Some output");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => agent.CompleteTask(TaskId.Create(), output));
        Assert.Contains("is not executing task", ex.Message);
    }

    [Fact]
    public void ShouldRaiseCompletedEvent_WhenCompletingTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);
        agent.ClearDomainEvents();
        var output = TaskOutput.Text("Done");

        // Act
        agent.CompleteTask(taskId, output);

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var completedEvent = Assert.IsType<AgentCompletedTaskEvent>(domainEvent);
        Assert.Equal(agent.Id, completedEvent.AgentId);
        Assert.Equal(taskId, completedEvent.TaskId);
        Assert.Equal(output, completedEvent.Output);
    }

    [Fact]
    public void ShouldFailTask_WhenTaskIsExecuting()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act
        agent.FailTask(taskId, "Something went wrong");

        // Assert
        Assert.Empty(agent.CurrentTasks);
        Assert.Equal(AgentStatus.Idle, agent.Status);
    }

    [Fact]
    public void ShouldThrow_WhenFailingWithNullReason()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act & Assert
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        Assert.Throws<ArgumentNullException>(() => agent.FailTask(taskId, null!));
    }

    [Fact]
    public void ShouldThrow_WhenFailingWithEmptyReason()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => agent.FailTask(taskId, "   "));
    }

    [Fact]
    public void ShouldThrow_WhenFailingATaskItIsNotRunning()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => agent.FailTask(TaskId.Create(), "error"));
        Assert.Contains("is not executing task", ex.Message);
    }

    [Fact]
    public void ShouldRaiseFailedEvent_WhenFailingTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);
        agent.ClearDomainEvents();

        // Act
        agent.FailTask(taskId, "Timeout occurred");

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var failedEvent = Assert.IsType<AgentFailedTaskEvent>(domainEvent);
        Assert.Equal(agent.Id, failedEvent.AgentId);
        Assert.Equal(taskId, failedEvent.TaskId);
        Assert.Equal("Timeout occurred", failedEvent.Reason);
    }

    #endregion

    #region Delegation Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNoDelegation_WhenDelegationNotAllowed()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .AllowDelegation(false)
            .Build();
        var task = new StubCrewTask();
        var availableAgents = new[] { AgentId.Create() };

        // Act
        var decision = await agent.ShouldDelegateAsync(task, availableAgents, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(decision.ShouldDelegate);
        Assert.Null(decision.DelegateToAgentId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDelegateToOtherAgent_WhenDelegationAllowedAndOtherAgentAvailable()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleManager)
            .Goal(GoalCoordinate)
            .AllowDelegation()
            .Build();
        var task = new StubCrewTask();
        var otherAgentId = AgentId.Create();

        // Act
        var decision = await agent.ShouldDelegateAsync(task, [otherAgentId], TestContext.Current.CancellationToken);

        // Assert
        Assert.True(decision.ShouldDelegate);
        Assert.Equal(otherAgentId, decision.DelegateToAgentId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotDelegateToSelf_WhenOnlyOwnIdAvailable()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleManager)
            .Goal(GoalCoordinate)
            .AllowDelegation()
            .Build();
        var task = new StubCrewTask();

        // Act - only the agent's own ID is available
        var decision = await agent.ShouldDelegateAsync(task, [agent.Id], TestContext.Current.CancellationToken);

        // Assert
        Assert.False(decision.ShouldDelegate);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSetAgentSelectionStrategy_WhenStrategyProvided()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleManager)
            .Goal(GoalCoordinate)
            .AllowDelegation()
            .Build();
        var expectedAgentId = AgentId.Create();

        // Act
        agent.SetAgentSelectionStrategy(
            (task, agents, ct) => System.Threading.Tasks.Task.FromResult<AgentId?>(expectedAgentId));

        // Assert - verify strategy is used via ShouldDelegateAsync
        var decision = await agent.ShouldDelegateAsync(new StubCrewTask(), [expectedAgentId], TestContext.Current.CancellationToken);
        Assert.True(decision.ShouldDelegate);
        Assert.Equal(expectedAgentId, decision.DelegateToAgentId);
    }

    [Fact]
    public void ShouldThrow_WhenSettingNullSelectionStrategy()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => agent.SetAgentSelectionStrategy(null!));
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void ShouldUpdateGoal_WhenAgentIsIdle()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var newGoal = AgentGoal.From("New research direction");

        // Act
        agent.UpdateGoal(newGoal);

        // Assert
        Assert.Equal("New research direction", agent.Goal.Value);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingGoalWithNull()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => agent.UpdateGoal(null!));
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingGoalWhileBusy()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => agent.UpdateGoal(AgentGoal.From("New goal")));
        Assert.Contains("Cannot update goal while agent is working", ex.Message);
    }

    [Fact]
    public void ShouldUpdateBackstory_WhenNewBackstoryProvided()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var newBackstory = AgentBackstory.From("Updated backstory");

        // Act
        agent.UpdateBackstory(newBackstory);

        // Assert
        Assert.Equal("Updated backstory", agent.Backstory?.Value);
    }

    [Fact]
    public void ShouldClearBackstory_WhenNullProvided()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act
        agent.UpdateBackstory(null);

        // Assert
        Assert.Null(agent.Backstory);
    }

    [Fact]
    public void ShouldUpdateConfiguration_WhenAgentIsIdle()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act
        agent.UpdateConfiguration(
            allowDelegation: true,
            maxIterations: 20,
            maxRpm: 30,
            verbose: true);

        // Assert
        Assert.True(agent.AllowDelegation);
        Assert.Equal(20, agent.MaxIterations);
        Assert.Equal(30, agent.MaxRpm);
        Assert.True(agent.Verbose);
    }

    [Fact]
    public void ShouldOnlyUpdateSpecifiedValues_WhenPartialConfigurationProvided()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Dev")
            .Goal("Code")
            .MaxIterations(10)
            .MaxRpm(5)
            .Build();

        // Act - only update maxIterations
        agent.UpdateConfiguration(maxIterations: 25);

        // Assert
        Assert.Equal(25, agent.MaxIterations);
        Assert.Equal(5, agent.MaxRpm); // unchanged
        Assert.False(agent.AllowDelegation); // unchanged
        Assert.False(agent.Verbose); // unchanged
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingConfigurationWhileBusy()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => agent.UpdateConfiguration(verbose: true));
        Assert.Contains("Cannot update configuration while agent is working", ex.Message);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingConfigurationWithInvalidMaxIterations()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => agent.UpdateConfiguration(maxIterations: 0));
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingConfigurationWithInvalidMaxRpm()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => agent.UpdateConfiguration(maxRpm: -5));
    }

    #endregion

    #region ExecuteTaskAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenExecutingWithNullLlm()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var task = new StubCrewTask();
        var toolRegistry = new StubToolRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => agent.ExecuteTaskAsync(task, null!, toolRegistry, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenExecutingWithNullToolRegistry()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var task = new StubCrewTask();
        var llm = new StubLlmProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => agent.ExecuteTaskAsync(task, llm, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenExecutingWithNullTask()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var llm = new StubLlmProvider();
        var toolRegistry = new StubToolRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => agent.ExecuteTaskAsync(null!, llm, toolRegistry, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenExecutingWhileBusy()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId); // Agent is now Busy

        var task = new StubCrewTask();
        var llm = new StubLlmProvider();
        var toolRegistry = new StubToolRegistry();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.ExecuteTaskAsync(task, llm, toolRegistry, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Tool Management Tests

    [Fact]
    public void ShouldAddTool_WhenToolIsValid()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var tool = new StubTool(ToolSearch);

        // Act
        agent.AddTool(tool);

        // Assert
        Assert.Single(agent.Tools);
        Assert.True(agent.HasTool(ToolSearch));
    }

    [Fact]
    public void ShouldRaiseCapabilitiesUpdatedEvent_WhenAddingTool()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        agent.ClearDomainEvents();
        var tool = new StubTool("NewTool");

        // Act
        agent.AddTool(tool);

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var capEvent = Assert.IsType<AgentCapabilitiesUpdatedEvent>(domainEvent);
        Assert.Contains("NewTool", capEvent.AddedTools);
        Assert.Empty(capEvent.RemovedTools);
    }

    [Fact]
    public void ShouldThrow_WhenAddingDuplicateTool()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        agent.AddTool(new StubTool(ToolSearch));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => agent.AddTool(new StubTool(ToolSearch)));
    }

    [Fact]
    public void ShouldRemoveTool_WhenToolExists()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        agent.AddTool(new StubTool(ToolSearch));

        // Act
        agent.RemoveTool(ToolSearch);

        // Assert
        Assert.Empty(agent.Tools);
        Assert.False(agent.HasTool(ToolSearch));
    }

    [Fact]
    public void ShouldRaiseCapabilitiesUpdatedEvent_WhenRemovingTool()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        agent.AddTool(new StubTool("OldTool"));
        agent.ClearDomainEvents();

        // Act
        agent.RemoveTool("OldTool");

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var capEvent = Assert.IsType<AgentCapabilitiesUpdatedEvent>(domainEvent);
        Assert.Empty(capEvent.AddedTools);
        Assert.Contains("OldTool", capEvent.RemovedTools);
    }

    [Fact]
    public void ShouldThrow_WhenRemovingNonExistentTool()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => agent.RemoveTool("NonExistent"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenHasToolExists()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Dev")
            .Goal("Code")
            .WithTool(new StubTool("MyTool"))
            .Build();

        // Act & Assert
        Assert.True(agent.HasTool("MyTool"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenHasToolDoesNotExist()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.False(agent.HasTool("NonExistent"));
    }

    #endregion

    #region RegisterCancellation and StopAsync Tests

    [Fact]
    public void ShouldRegisterCancellation_WhenTokenSourceProvided()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        using var cts = new CancellationTokenSource();

        // Act
        agent.RegisterCancellation(cts);

        // Assert
        Assert.NotNull(agent);
    }

    [Fact]
    public void ShouldThrow_WhenRegisteringNullCancellation()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => agent.RegisterCancellation(null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopAgent_WhenCancellationRegistered()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);
        using var cts = new CancellationTokenSource();
        agent.RegisterCancellation(cts);

        // Act
        await agent.StopAsync("Test stop");

        // Assert
        Assert.Equal(AgentStatus.Idle, agent.Status);
        Assert.Empty(agent.CurrentTasks);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRaiseKilledEvent_WhenStopping()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        using var cts = new CancellationTokenSource();
        agent.RegisterCancellation(cts);
        agent.ClearDomainEvents();

        // Act
        await agent.StopAsync("Emergency stop");

        // Assert
        var domainEvent = Assert.Single(agent.DomainEvents);
        var killedEvent = Assert.IsType<AgentKilledEvent>(domainEvent);
        Assert.Equal(agent.Id, killedEvent.AgentId);
        Assert.Equal("Emergency stop", killedEvent.Reason);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenStoppingWithoutRegisteredCancellation()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.StopAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotRaiseEvent_WhenStoppingAlreadyCancelledAgent()
    {
        // Arrange
        var agent = AgentFixtures.CreateResearcherAgent();
        using var cts = new CancellationTokenSource();
        agent.RegisterCancellation(cts);
        await agent.StopAsync("First stop");
        agent.ClearDomainEvents();

        // Act
        await agent.StopAsync("Second stop");

        // Assert - no new events because already cancelled
        Assert.Empty(agent.DomainEvents);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void ShouldCreateViaOptions_WhenUsingCreateWithOptions()
    {
        // Arrange
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From(RoleAnalyst),
            Goal = AgentGoal.From(GoalAnalyzeData),
            Backstory = AgentBackstory.From("Data expert"),
            AllowDelegation = true,
            MaxIterations = 8,
            MaxRpm = 15,
            Verbose = true,
            CacheEnabled = false,
            MaxRetryLimit = 5
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.Equal(RoleAnalyst, agent.Role.Value);
        Assert.Equal(GoalAnalyzeData, agent.Goal.Value);
        Assert.Equal("Data expert", agent.Backstory?.Value);
        Assert.True(agent.AllowDelegation);
        Assert.Equal(8, agent.MaxIterations);
        Assert.Equal(15, agent.MaxRpm);
        Assert.True(agent.Verbose);
        Assert.False(agent.CacheEnabled);
        Assert.Equal(5, agent.MaxRetryLimit);
        Assert.Equal(AgentStatus.Idle, agent.Status);
    }

    [Fact]
    public void ShouldThrow_WhenCreatingWithNullOptions()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => DomainAgent.Create((AgentCreateOptions)null!));
    }

    [Fact]
    public void ShouldCreateViaParameterOverload_WhenUsingCreateWith16Params()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From("Code well");
        var backstory = AgentBackstory.From("Senior dev");
        var tool = new StubTool("CodeTool");

        // Act
        var agent = DomainAgent.Create(
            role: role,
            goal: goal,
            backstory: backstory,
            allowDelegation: true,
            maxIterations: 7,
            maxRpm: 12,
            verbose: true,
            maxExecutionTime: TimeoutStandard,
            cacheEnabled: false,
            systemTemplate: "system",
            promptTemplate: "prompt",
            responseTemplate: "response",
            maxRetryLimit: 4,
            tools: [tool]);

        // Assert
        Assert.Equal(RoleDeveloper, agent.Role.Value);
        Assert.Equal("Code well", agent.Goal.Value);
        Assert.Equal("Senior dev", agent.Backstory?.Value);
        Assert.True(agent.AllowDelegation);
        Assert.Equal(7, agent.MaxIterations);
        Assert.Equal(12, agent.MaxRpm);
        Assert.True(agent.Verbose);
        Assert.Equal(TimeoutStandard, agent.MaxExecutionTime);
        Assert.False(agent.CacheEnabled);
        Assert.Equal("system", agent.SystemTemplate);
        Assert.Equal("prompt", agent.PromptTemplate);
        Assert.Equal("response", agent.ResponseTemplate);
        Assert.Equal(4, agent.MaxRetryLimit);
        Assert.Single(agent.Tools);
        Assert.True(agent.HasTool("CodeTool"));
    }

    [Fact]
    public void ShouldRaiseCreatedEvent_WhenCreatingAgent()
    {
        // Arrange & Act
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Code")
            .Build();

        // Assert
        var createdEvent = agent.DomainEvents.OfType<AgentCreatedEvent>().Single();
        Assert.Equal(agent.Id, createdEvent.AgentId);
        Assert.Equal(agent.Role, createdEvent.Role);
        Assert.Equal(agent.Goal, createdEvent.Goal);
    }

    [Fact]
    public void ShouldCreateWithToolsViaOptions_WhenToolsProvided()
    {
        // Arrange
        var tools = new IBaseTool[] { new StubTool("A"), new StubTool("B") };
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("Dev"),
            Goal = AgentGoal.From("Code"),
            Tools = tools
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.Equal(2, agent.Tools.Count);
        Assert.True(agent.HasTool("A"));
        Assert.True(agent.HasTool("B"));
    }

    #endregion

    #region Test Doubles

    private sealed class StubTool : Orkeon.Domain.Tools.IBaseTool
    {
        public string Name { get; }
        public string Description => $"Stub tool {Name}";
        public Orkeon.Domain.Tools.Protocol.ToolSchema Schema => new(Name, Description, []);

        public StubTool(string name) => Name = name;

        public System.Threading.Tasks.Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallAsync(Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new Orkeon.Domain.Tools.Protocol.ToolCallResponse(true, "ok", null));

        public System.Threading.Tasks.Task<Orkeon.Domain.Tools.ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(Orkeon.Domain.Tools.ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private sealed class StubLlmProvider : ILlmProvider
    {
        public string Name => "StubLlm";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub response" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub chat response" });
    }

    private sealed class StubToolRegistry : IToolRegistry
    {
        public System.Threading.Tasks.Task<bool> RegisterToolAsync(IBaseTool tool) => System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Tasks.Task<bool> UnregisterToolAsync(string toolId) => System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Tasks.Task<IBaseTool?> GetToolAsync(string toolId) => System.Threading.Tasks.Task.FromResult<IBaseTool?>(null);
        public System.Threading.Tasks.Task<IBaseTool?> GetToolByNameAsync(string name) => System.Threading.Tasks.Task.FromResult<IBaseTool?>(null);
        public System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync() => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IBaseTool>>(Array.Empty<IBaseTool>());
        public System.Threading.Tasks.Task<bool> IsRegisteredAsync(string toolId) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task ClearAsync() => System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class StubCrewTask : ICrewTask
    {
        public TaskId TaskId { get; } = TaskId.Create();
        public TaskDescription Description { get; } = TaskDescription.From("Stub task description");
        public ExpectedOutput ExpectedOutput { get; } = ExpectedOutput.From("Expected output");
        public AgentId? AssignedAgent => null;
        public Orkeon.Domain.Task.ValueObjects.TaskStatus Status => Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending;
        public TaskOutput? Output => null;
        public IReadOnlyList<TaskId> Dependencies => Array.Empty<TaskId>();
        public DateTime CreatedAt => DateTime.UtcNow;
        public DateTime? StartedAt => null;
        public DateTime? CompletedAt => null;
        public bool AsyncExecution => false;
        public JsonSchema? OutputJson => null;
        public Type? OutputPydantic => null;
        public string? OutputFile => null;
        public bool HumanInput => false;

        public void AssignTo(AgentId agentId) { }
        public void Start(AgentId agentId) { }
        public void Complete(AgentId agentId, TaskOutput output) { }
        public void Fail(string errorMessage, Exception? exception = null) { }
        public bool CanExecute(Func<TaskId, bool> isTaskCompleted) => true;
        public ValidationResult ValidateOutput(TaskOutput output) => ValidationResult.Success();
        public string GetContextSummary() => "Stub context";
        public TimeSpan GetExecutionTime() => TimeSpan.Zero;
    }

    #endregion
}
