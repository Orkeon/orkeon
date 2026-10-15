using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tests.Fixtures;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Tests.Entities;

/// <summary>
/// Tests for Crew entity following Clean Architecture principles.
/// Tests the business rules and domain logic of the Crew aggregate root.
/// </summary>
public class CrewTests
{
    #region Test Doubles

    private sealed class StubLlmProvider : ILlmProvider
    {
        public string Name => "StubLlm";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });
    }

    #endregion

    #region Create

    [Fact]
    public void ShouldCreateCrew_WhenCreatingWithValidGoal()
    {
        // Arrange
        var goal = "Build an AI-powered application";

        // Act
        var crew = DomainCrew.Create(goal);

        // Assert
        Assert.NotNull(crew);
        Assert.NotEqual<object>(Guid.Empty, crew.Id.Value);
        Assert.Equal(goal, crew.Goal.Value);
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
        Assert.Equal(CrewStatus.Idle, crew.Status);
        Assert.False(crew.Verbose);
        Assert.False(crew.Planning);
        // No limit of its own unless it declares one, as CrewAI's max_rpm (GAP-38).
        Assert.Null(crew.MaxRpm);
        Assert.False(crew.MemoryEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShouldThrowArgumentException_WhenCreatingWithEmptyOrNullGoal(string? goal)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DomainCrew.Create(goal!)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithInvalidMaxRpm()
    {
        // Arrange
        var goal = "Valid goal";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DomainCrew.Create(goal, maxRpm: 0)
        );
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("must be positive", exception.Message);
    }

    [Theory]
    [InlineData("Sequential")]
    [InlineData("Parallel")]
    [InlineData("Consensual")]
    public void ShouldSetProcessType_WhenCreatingWithDifferentProcessTypes(string processTypeStr)
    {
        // Arrange
        var processType = ProcessType.From(processTypeStr);
        var goal = "Test different process types";

        // Act
        var crew = DomainCrew.Create(goal, processType);

        // Assert
        Assert.Equal(processType, crew.ProcessType);
    }

    [Fact]
    public void ShouldSetAllParameters_WhenCreatingWithCustomParameters()
    {
        // Arrange
        var goal = "Complex project goal";
        var processType = ProcessType.Sequential;
        var verbose = true;
        var planning = true;
        var maxRpm = 50;
        var memoryEnabled = true;

        // Act
        var crew = DomainCrew.Create(
            goal: goal,
            processType: processType,
            verbose: verbose,
            planning: planning,
            maxRpm: maxRpm,
            memoryEnabled: memoryEnabled
        );

        // Assert
        Assert.Equal(goal, crew.Goal.Value);
        Assert.Equal(processType, crew.ProcessType);
        Assert.Equal(verbose, crew.Verbose);
        Assert.Equal(planning, crew.Planning);
        Assert.Equal(maxRpm, crew.MaxRpm);
        Assert.Equal(memoryEnabled, crew.MemoryEnabled);
    }

    [Fact]
    public void ShouldGenerateUniqueCrewIds_WhenCreating()
    {
        // Arrange
        var goal = "Test unique IDs";

        // Act
        var crew1 = DomainCrew.Create(goal);
        var crew2 = DomainCrew.Create(goal);

        // Assert
        Assert.NotEqual(crew1.Id.Value, crew2.Id.Value);
    }

    [Fact]
    public void ShouldReturnCrewIdAsString_WhenUsingIdProperty()
    {
        // Arrange
        var crew = CrewFixtures.CreateSequentialCrew();

        // Act
        var id = crew.Id;
        var stringId = crew.Id.ToString();

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(stringId);
        // StringId should contain the GUID value
        Assert.Contains(crew.Id.Value.ToString(), stringId);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingAgentsCollection()
    {
        // Arrange
        var crew = CrewFixtures.CreateSequentialCrew();

        // Act
        var agents = crew.Agents;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<AgentId>>(agents);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingTasksCollection()
    {
        // Arrange
        var crew = CrewFixtures.CreateSequentialCrew();

        // Act
        var tasks = crew.Tasks;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<TaskId>>(tasks);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingExecutionsCollection()
    {
        // Arrange
        var crew = CrewFixtures.CreateSequentialCrew();

        // Act
        var executions = crew.Executions;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<CrewExecution>>(executions);
    }

    [Fact]
    public void ShouldBeNull_WhenUsingCurrentProcessIdUsingInitially()
    {
        // Arrange & Act
        var crew = CrewFixtures.CreateSequentialCrew();

        // Assert
        Assert.Null(crew.CurrentProcessId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void ShouldAcceptValue_WhenCreatingWithValidMaxRpm(int maxRpm)
    {
        // Arrange
        var goal = "Test RPM values";

        // Act
        var crew = DomainCrew.Create(goal, maxRpm: maxRpm);

        // Assert
        Assert.Equal(maxRpm, crew.MaxRpm);
    }

    [Fact]
    public void ShouldEnablePlanning_WhenCreatingWithPlanningEnabled()
    {
        // Arrange
        var goal = "Plan-enabled project";

        // Act
        var crew = DomainCrew.Create(goal, planning: true);

        // Assert
        Assert.True(crew.Planning);
    }

    [Fact]
    public void ShouldRequireManager_WhenCreatingWithHierarchicalProcess()
    {
        // Arrange
        var goal = "Hierarchical project";
        var processType = ProcessType.Hierarchical;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => DomainCrew.Create(goal, processType)
        );
        Assert.Contains("Hierarchical process requires either a manager agent or manager LLM", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingWithNullOptions()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => DomainCrew.Create((CrewCreateOptions)null!));
    }

    [Fact]
    public void ShouldRaiseCrewCreatedEvent_WhenCreating()
    {
        // Arrange & Act
        var crew = DomainCrew.Create("Test goal");

        // Assert
        var createdEvent = Assert.Single(crew.DomainEvents.OfType<CrewCreatedEvent>());
        Assert.Equal(crew.Id, createdEvent.CrewId);
        Assert.Equal("Test goal", createdEvent.Goal);
        Assert.Equal(ProcessType.Sequential, createdEvent.ProcessType);
    }

    [Fact]
    public void ShouldCreateHierarchicalCrew_WhenProvidingManagerAgentId()
    {
        // Arrange
        var managerAgentId = AgentId.Create();

        // Act
        var crew = DomainCrew.Create("Hierarchical goal", ProcessType.Hierarchical, managerAgentId: managerAgentId);

        // Assert
        Assert.Equal(ProcessType.Hierarchical, crew.ProcessType);
        Assert.Equal(managerAgentId, crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldCreateHierarchicalCrew_WhenProvidingManagerLlm()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();

        // Act
        var crew = DomainCrew.Create("Hierarchical goal", ProcessType.Hierarchical, managerLlm: managerLlm);

        // Assert
        Assert.Equal(ProcessType.Hierarchical, crew.ProcessType);
        Assert.Same(managerLlm, crew.ManagerLlm);
    }

    #endregion

    #region AddAgent

    [Fact]
    public void ShouldAddAgent_WhenCrewIsIdle()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();

        // Act
        crew.AddAgent(agentId);

        // Assert
        Assert.Single(crew.Agents);
        Assert.Equal(agentId, crew.Agents[0]);
    }

    [Fact]
    public void ShouldRaiseAgentJoinedCrewEvent_WhenAddingAgent()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.ClearDomainEvents();
        var agentId = AgentId.Create();

        // Act
        crew.AddAgent(agentId);

        // Assert
        var joinEvent = Assert.Single(crew.DomainEvents.OfType<AgentJoinedCrewEvent>());
        Assert.Equal(crew.Id, joinEvent.CrewId);
        Assert.Equal(agentId, joinEvent.AgentId);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingDuplicateAgent()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.AddAgent(agentId));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingAgentDuringExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId1 = AgentId.Create();
        var taskId = TaskId.Create();
        crew.AddAgent(agentId1);
        crew.AddTask(taskId);
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.AddAgent(AgentId.Create()));
    }

    [Fact]
    public void ShouldKeepEveryAgentAWorker_WhenAddingAgentsToACrewItsManagerLlmManages()
    {
        // GAP-19: a crew given a manager LLM (WithManagerLlm, CrewAI's manager_llm) needs no manager
        // agent. The first agent added used to be made manager — and was lost as a worker.
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: new StubLlmProvider());
        var first = AgentId.Create();
        var second = AgentId.Create();

        crew.AddAgent(first);
        crew.AddAgent(second);

        Assert.Null(crew.ManagerAgentId);
        Assert.Equal([first, second], crew.Agents);
    }

    #endregion

    #region AddTask

    [Fact]
    public void ShouldAddTask_WhenCrewIsIdle()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var taskId = TaskId.Create();

        // Act
        crew.AddTask(taskId);

        // Assert
        Assert.Single(crew.Tasks);
        Assert.Equal(taskId, crew.Tasks[0]);
    }

    [Fact]
    public void ShouldRaiseTaskAddedToCrewEvent_WhenAddingTask()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.ClearDomainEvents();
        var taskId = TaskId.Create();

        // Act
        crew.AddTask(taskId);

        // Assert
        var addedEvent = Assert.Single(crew.DomainEvents.OfType<TaskAddedToCrewEvent>());
        Assert.Equal(crew.Id, addedEvent.CrewId);
        Assert.Equal(taskId, addedEvent.TaskId);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingDuplicateTask()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var taskId = TaskId.Create();
        crew.AddTask(taskId);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.AddTask(taskId));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenAddingTaskDuringExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.AddTask(TaskId.Create()));
    }

    #endregion

    #region StartExecution

    [Fact]
    public void ShouldStartExecution_WhenCrewHasAgentsAndTasks()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        // Act
        var processId = crew.StartExecution();

        // Assert
        Assert.NotNull(processId);
        Assert.Equal(CrewStatus.Executing, crew.Status);
        Assert.Equal(processId, crew.CurrentProcessId);
        Assert.Single(crew.Executions);
    }

    [Fact]
    public void ShouldRaiseCrewExecutionStartedEvent_WhenStartingExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.ClearDomainEvents();

        // Act
        var processId = crew.StartExecution();

        // Assert
        var startedEvent = Assert.Single(crew.DomainEvents.OfType<CrewExecutionStartedEvent>());
        Assert.Equal(crew.Id, startedEvent.CrewId);
        Assert.Equal(processId, startedEvent.ProcessId);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenStartingExecutionWhileAlreadyExecuting()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.StartExecution());
        Assert.Contains("already executing", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenStartingExecutionWithoutAgents()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddTask(TaskId.Create());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.StartExecution());
        Assert.Contains("without agents", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenStartingExecutionWithoutTasks()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.StartExecution());
        Assert.Contains("without tasks", ex.Message);
    }

    [Fact]
    public void ShouldStartHierarchicalExecution_WhenItsManagerLlmManagesWithoutAManagerAgent()
    {
        // GAP-19: the test this replaces could not build the crew it was named after — AddAgent
        // made the first agent manager. A crew managed by its manager LLM validates and starts.
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: new StubLlmProvider());
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        Assert.True(crew.Validate().IsValid, crew.Validate().ToString());
        crew.ValidateCanKickoff();
        crew.StartExecution();

        Assert.Equal(CrewStatus.Executing, crew.Status);
        Assert.Null(crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldRefuseHierarchicalExecution_WhenItsManagerAgentIsNoMember()
    {
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerAgentId: AgentId.Create());
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        var validation = crew.Validate();

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.ErrorMessage.Contains("member", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(crew.ValidateCanKickoff);
    }

    #endregion

    #region CompleteExecution

    [Fact]
    public void ShouldCompleteExecution_WhenExecutionIsInProgress()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act
        crew.CompleteExecution(5);

        // Assert
        Assert.Equal(CrewStatus.Idle, crew.Status);
        Assert.Null(crew.CurrentProcessId);
        Assert.Equal(5, crew.Executions[0].CompletedTasks);
        Assert.Equal(ExecutionStatus.Succeeded, crew.Executions[0].Status);
    }

    [Fact]
    public void ShouldRaiseCrewExecutionCompletedEvent_WhenCompletingExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();
        crew.ClearDomainEvents();

        // Act
        crew.CompleteExecution(3);

        // Assert
        var completedEvent = Assert.Single(crew.DomainEvents.OfType<CrewExecutionCompletedEvent>());
        Assert.Equal(crew.Id, completedEvent.CrewId);
        Assert.Equal(3, completedEvent.CompletedTasks);
        Assert.Empty(crew.DomainEvents.OfType<CrewExecutionFailedEvent>());
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCompletingExecutionNotInProgress()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.CompleteExecution(0));
    }

    #endregion

    #region FailExecution

    [Fact]
    public void ShouldFailExecution_WhenExecutionIsInProgress()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act
        crew.FailExecution("Something went wrong");

        // Assert
        Assert.Equal(CrewStatus.Failed, crew.Status);
        Assert.Null(crew.CurrentProcessId);
        Assert.Equal("Something went wrong", crew.Executions[0].FailureReason);
    }

    [Fact]
    public void ShouldRaiseCrewExecutionFailedEvent_WhenFailingExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();
        crew.ClearDomainEvents();
        var exception = new InvalidOperationException("test error");

        // Act
        crew.FailExecution("Catastrophic failure", exception);

        // Assert
        var failedEvent = Assert.Single(crew.DomainEvents.OfType<CrewExecutionFailedEvent>());
        Assert.Equal(crew.Id, failedEvent.CrewId);
        Assert.Equal("Catastrophic failure", failedEvent.Reason);
        Assert.Same(exception, failedEvent.Exception);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenFailingExecutionNotInProgress()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.FailExecution("reason"));
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenFailingExecutionWithEmptyReason()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => crew.FailExecution(""));
    }

    #endregion

    #region Validate

    [Fact]
    public void ShouldReturnInvalid_WhenValidatingCrewWithNoAgents()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddTask(TaskId.Create());

        // Act
        var result = crew.Validate();

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one agent"));
    }

    [Fact]
    public void ShouldReturnInvalid_WhenValidatingCrewWithNoTasks()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());

        // Act
        var result = crew.Validate();

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one task"));
    }

    [Fact]
    public void ShouldReturnValid_WhenValidatingCrewWithAgentsAndTasks()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        // Act
        var result = crew.Validate();

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateCanKickoff

    [Fact]
    public void ShouldNotThrow_WhenValidatingCanKickoffWithValidCrew()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        // Act & Assert (no exception)
        crew.ValidateCanKickoff();
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenValidatingCanKickoffWithInvalidCrew()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.ValidateCanKickoff());
        Assert.Contains("validation failed", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenValidatingCanKickoffWhileExecuting()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.ValidateCanKickoff());
        Assert.Contains("already executing", ex.Message);
    }

    #endregion

    #region Create(CrewCreateOptions)

    [Fact]
    public void ShouldCreateCrewViaOptions_WhenUsingCrewCreateOptions()
    {
        // Arrange
        var options = new CrewCreateOptions
        {
            Goal = "Options-based creation",
            ProcessType = ProcessType.Parallel,
            Verbose = true,
            Planning = true,
            MaxRpm = 75,
            MemoryEnabled = true
        };

        // Act
        var crew = DomainCrew.Create(options);

        // Assert
        Assert.Equal("Options-based creation", crew.Goal.Value);
        Assert.Equal(ProcessType.Parallel, crew.ProcessType);
        Assert.True(crew.Verbose);
        Assert.True(crew.Planning);
        Assert.Equal(75, crew.MaxRpm);
        Assert.True(crew.MemoryEnabled);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCreatingWithNegativeMaxRpm()
    {
        // Arrange & Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => DomainCrew.Create("Test", maxRpm: -1));
        Assert.Contains("must be positive", ex.Message);
    }

    #endregion
}
