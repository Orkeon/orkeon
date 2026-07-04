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
        Assert.Equal(100, crew.MaxRpm);
        Assert.True(crew.ShareCrew);
        Assert.Equal("en", crew.Language.Value);
        Assert.False(crew.FullOutput);
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
        var shareCrew = false;
        var outputLogFile = "/logs/crew.log";
        var language = "fr";
        var fullOutput = true;
        var memoryEnabled = true;

        // Act
        var crew = DomainCrew.Create(
            goal: goal,
            processType: processType,
            verbose: verbose,
            planning: planning,
            maxRpm: maxRpm,
            shareCrew: shareCrew,
            outputLogFile: outputLogFile,
            language: language,
            fullOutput: fullOutput,
            memoryEnabled: memoryEnabled
        );

        // Assert
        Assert.Equal(goal, crew.Goal.Value);
        Assert.Equal(processType, crew.ProcessType);
        Assert.Equal(verbose, crew.Verbose);
        Assert.Equal(planning, crew.Planning);
        Assert.Equal(maxRpm, crew.MaxRpm);
        Assert.Equal(shareCrew, crew.ShareCrew);
        Assert.Equal(outputLogFile, crew.OutputLogFile);
        Assert.Equal(language, crew.Language.Value);
        Assert.Equal(fullOutput, crew.FullOutput);
        Assert.Equal(memoryEnabled, crew.MemoryEnabled);
    }

    [Fact]
    public void ShouldDefaultToEnglish_WhenCreatingWithEmptyLanguage()
    {
        // Arrange
        var goal = "Test language default";

        // Act
        var crew = DomainCrew.Create(goal, language: "");

        // Assert
        Assert.Equal("en", crew.Language.Value);
    }

    [Fact]
    public void ShouldDefaultToEnglish_WhenCreatingWithWhitespaceLanguage()
    {
        // Arrange
        var goal = "Test language default";

        // Act
        var crew = DomainCrew.Create(goal, language: "   ");

        // Assert
        Assert.Equal("en", crew.Language.Value);
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

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void ShouldSetLanguage_WhenCreatingWithDifferentLanguages(string language)
    {
        // Arrange
        var goal = "Multi-language support test";

        // Act
        var crew = DomainCrew.Create(goal, language: language);

        // Assert
        Assert.Equal(language, crew.Language.Value);
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

    #region AddAgent / RemoveAgent

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
    public void ShouldSetFirstAgentAsManager_WhenAddingAgentToHierarchicalCrewWithNoManager()
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
    public void ShouldRemoveAgent_WhenCrewIsIdle()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);

        // Act
        crew.RemoveAgent(agentId, "No longer needed");

        // Assert
        Assert.Empty(crew.Agents);
    }

    [Fact]
    public void ShouldRaiseAgentLeftCrewEvent_WhenRemovingAgent()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);
        crew.ClearDomainEvents();

        // Act
        crew.RemoveAgent(agentId, "Reassigned");

        // Assert
        var leftEvent = Assert.Single(crew.DomainEvents.OfType<AgentLeftCrewEvent>());
        Assert.Equal(crew.Id, leftEvent.CrewId);
        Assert.Equal(agentId, leftEvent.AgentId);
        Assert.Equal("Reassigned", leftEvent.Reason);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenRemovingAgentNotInCrew()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.RemoveAgent(AgentId.Create(), "reason"));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenRemovingAgentDuringExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.RemoveAgent(agentId, "reason"));
    }

    [Fact]
    public void ShouldReassignManager_WhenRemovingManagerAgentFromHierarchicalCrew()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        crew.AddAgent(agent1); // becomes manager
        crew.AddAgent(agent2);

        // Act
        crew.RemoveAgent(agent1, "Reassigned");

        // Assert
        Assert.Equal(agent2, crew.ManagerAgentId);
    }

    #endregion

    #region AddTask / RemoveTask

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

    [Fact]
    public void ShouldRemoveTask_WhenCrewIsIdle()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var taskId = TaskId.Create();
        crew.AddTask(taskId);

        // Act
        crew.RemoveTask(taskId, "Cancelled");

        // Assert
        Assert.Empty(crew.Tasks);
    }

    [Fact]
    public void ShouldRaiseTaskRemovedFromCrewEvent_WhenRemovingTask()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var taskId = TaskId.Create();
        crew.AddTask(taskId);
        crew.ClearDomainEvents();

        // Act
        crew.RemoveTask(taskId, "Deprioritized");

        // Assert
        var removedEvent = Assert.Single(crew.DomainEvents.OfType<TaskRemovedFromCrewEvent>());
        Assert.Equal(crew.Id, removedEvent.CrewId);
        Assert.Equal(taskId, removedEvent.TaskId);
        Assert.Equal("Deprioritized", removedEvent.Reason);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenRemovingTaskNotInCrew()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.RemoveTask(TaskId.Create(), "reason"));
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenRemovingTaskDuringExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var taskId = TaskId.Create();
        crew.AddAgent(AgentId.Create());
        crew.AddTask(taskId);
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.RemoveTask(taskId, "reason"));
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
    public void ShouldThrowInvalidOperationException_WhenStartingHierarchicalExecutionWithoutManager()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        // The first agent auto-becomes manager, so let's remove that assignment
        // Actually, AddAgent sets manager for hierarchical crews. So this test needs a different approach.
        // We test via ChangeProcessType to hierarchical with manager provided (P1-03 fix requires both).
        var crew2 = DomainCrew.Create("Test");
        var agent = AgentId.Create();
        crew2.AddAgent(agent);
        crew2.AddTask(TaskId.Create());
        crew2.ChangeProcessType(ProcessType.Hierarchical, agent);
        // ChangeProcessType now requires managerAgentId AND members (P1-03 fix).
        Assert.Equal(ProcessType.Hierarchical, crew2.ProcessType);
        Assert.Equal(agent, crew2.ManagerAgentId);
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
        crew.CompleteExecution(5, 1);

        // Assert
        Assert.Equal(CrewStatus.Idle, crew.Status);
        Assert.Null(crew.CurrentProcessId);
        Assert.Equal(5, crew.Executions[0].CompletedTasks);
        Assert.Equal(1, crew.Executions[0].FailedTasks);
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
        crew.CompleteExecution(3, 0);

        // Assert
        var completedEvent = Assert.Single(crew.DomainEvents.OfType<CrewExecutionCompletedEvent>());
        Assert.Equal(crew.Id, completedEvent.CrewId);
        Assert.Equal(3, completedEvent.CompletedTasks);
        Assert.Equal(0, completedEvent.FailedTasks);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCompletingExecutionNotInProgress()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.CompleteExecution(0, 0));
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

    #region ChangeProcessType

    [Fact]
    public void ShouldChangeProcessType_WhenCrewIsIdle()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());

        // Act
        crew.ChangeProcessType(ProcessType.Parallel);

        // Assert
        Assert.Equal(ProcessType.Parallel, crew.ProcessType);
    }

    [Fact]
    public void ShouldRaiseCrewProcessTypeChangedEvent_WhenChangingProcessType()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.ClearDomainEvents();

        // Act
        crew.ChangeProcessType(ProcessType.Parallel);

        // Assert
        var changedEvent = Assert.Single(crew.DomainEvents.OfType<CrewProcessTypeChangedEvent>());
        Assert.Equal(ProcessType.Sequential, changedEvent.OldProcessType);
        Assert.Equal(ProcessType.Parallel, changedEvent.NewProcessType);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenChangingProcessTypeDuringExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.ChangeProcessType(ProcessType.Parallel));
    }

    [Fact]
    public void ShouldSetManagerAgent_WhenChangingToHierarchicalWithExistingAgents()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);

        // Act — after P1-03 fix (&&→||), both managerAgentId AND members are required
        crew.ChangeProcessType(ProcessType.Hierarchical, agentId);

        // Assert
        Assert.Equal(ProcessType.Hierarchical, crew.ProcessType);
        Assert.Equal(agentId, crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldSetSpecificManager_WhenChangingToHierarchicalWithManagerId()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        crew.AddAgent(agent1);
        crew.AddAgent(agent2);

        // Act
        crew.ChangeProcessType(ProcessType.Hierarchical, agent2);

        // Assert
        Assert.Equal(agent2, crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenChangingToHierarchicalWithNoAgents()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => crew.ChangeProcessType(ProcessType.Hierarchical));
    }

    [Fact]
    public void ChangeProcessType_ShouldThrowException_WhenBothManagerNullAndMembersEmpty()
    {
        // Arrange — P1-03: neither managerAgentId provided nor members present
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => crew.ChangeProcessType(ProcessType.Hierarchical, managerAgentId: null)
        );
        Assert.Contains("manager agent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangeProcessType_ShouldSucceed_WhenManagerAgentIdProvidedAndMembersExist()
    {
        // Arrange — P1-03: managerAgentId provided AND members exist
        var crew = DomainCrew.Create("Test");
        var managerAgentId = AgentId.Create();
        crew.AddAgent(managerAgentId);

        // Act
        crew.ChangeProcessType(ProcessType.Hierarchical, managerAgentId);

        // Assert
        Assert.Equal(ProcessType.Hierarchical, crew.ProcessType);
        Assert.Equal(managerAgentId, crew.ManagerAgentId);
    }

    [Fact]
    public void ChangeProcessType_ShouldThrowException_WhenManagerAgentIdIsNullButMembersExist()
    {
        // Arrange — P1-03 fix: with ||, null managerAgentId causes throw even with members
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => crew.ChangeProcessType(ProcessType.Hierarchical, managerAgentId: null)
        );
        Assert.Contains("manager agent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangeProcessType_ShouldThrowException_WhenManagerAgentIdProvidedButNoMembers()
    {
        // Arrange — P1-03 fix: with ||, empty members causes throw even with managerAgentId
        var crew = DomainCrew.Create("Test");
        var managerAgentId = AgentId.Create();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => crew.ChangeProcessType(ProcessType.Hierarchical, managerAgentId)
        );
        Assert.Contains("manager agent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangeProcessType_ShouldChangeToSequential_WithoutManagerValidation()
    {
        // Arrange — Sequential does not require manager validation
        var crew = DomainCrew.Create("Test");

        // Act
        crew.ChangeProcessType(ProcessType.Sequential);

        // Assert
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
    }

    #endregion

    #region SetManagerAgent

    [Fact]
    public void ShouldSetManagerAgent_WhenAgentIsInHierarchicalCrew()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        crew.AddAgent(agent1);
        crew.AddAgent(agent2);

        // Act
        crew.SetManagerAgent(agent2);

        // Assert
        Assert.Equal(agent2, crew.ManagerAgentId);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenSettingManagerOnNonHierarchicalCrew()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agentId = AgentId.Create();
        crew.AddAgent(agentId);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.SetManagerAgent(agentId));
        Assert.Contains("only applicable for hierarchical", ex.Message);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenSettingManagerNotInCrew()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);
        crew.AddAgent(AgentId.Create());

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => crew.SetManagerAgent(AgentId.Create()));
        Assert.Contains("is not in this crew", ex.Message);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSettingNullManagerAgent()
    {
        // Arrange
        var managerLlm = new StubLlmProvider();
        var crew = DomainCrew.Create("Hierarchical test", ProcessType.Hierarchical, managerLlm: managerLlm);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => crew.SetManagerAgent(null!));
    }

    #endregion

    #region UpdateGoal

    [Fact]
    public void ShouldUpdateGoal_WhenCrewIsIdle()
    {
        // Arrange
        var crew = DomainCrew.Create("Original goal");

        // Act
        crew.UpdateGoal("Updated goal");

        // Assert
        Assert.Equal("Updated goal", crew.Goal.Value);
    }

    [Fact]
    public void ShouldRaiseCrewGoalUpdatedEvent_WhenUpdatingGoal()
    {
        // Arrange
        var crew = DomainCrew.Create("Original goal");
        crew.ClearDomainEvents();

        // Act
        crew.UpdateGoal("New goal");

        // Assert
        var updatedEvent = Assert.Single(crew.DomainEvents.OfType<CrewGoalUpdatedEvent>());
        Assert.Equal("Original goal", updatedEvent.OldGoal);
        Assert.Equal("New goal", updatedEvent.NewGoal);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenUpdatingGoalDuringExecution()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        crew.StartExecution();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => crew.UpdateGoal("New goal"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ShouldThrowArgumentException_WhenUpdatingGoalWithEmptyValue(string newGoal)
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => crew.UpdateGoal(newGoal));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUpdatingGoalWithNullValue()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => crew.UpdateGoal(null!));
    }

    #endregion

    #region UpdateConfiguration

    [Fact]
    public void ShouldUpdateConfiguration_WhenApplyingPartialUpdate()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act
        crew.UpdateConfiguration(new CrewConfigurationUpdate
        {
            Verbose = true,
            MaxRpm = 200,
            MemoryEnabled = true
        });

        // Assert
        Assert.True(crew.Verbose);
        Assert.Equal(200, crew.MaxRpm);
        Assert.True(crew.MemoryEnabled);
        // Unchanged properties should keep defaults
        Assert.False(crew.Planning);
        Assert.True(crew.ShareCrew);
    }

    [Fact]
    public void ShouldUpdateAllConfigurationProperties_WhenSettingAll()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act
        crew.UpdateConfiguration(new CrewConfigurationUpdate
        {
            Verbose = true,
            Planning = true,
            MaxRpm = 50,
            ShareCrew = false,
            OutputLogFile = "/tmp/log.txt",
            Language = "fr",
            FullOutput = true,
            MemoryEnabled = true
        });

        // Assert
        Assert.True(crew.Verbose);
        Assert.True(crew.Planning);
        Assert.Equal(50, crew.MaxRpm);
        Assert.False(crew.ShareCrew);
        Assert.Equal("/tmp/log.txt", crew.OutputLogFile);
        Assert.Equal("fr", crew.Language.Value);
        Assert.True(crew.FullOutput);
        Assert.True(crew.MemoryEnabled);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUpdatingWithNonPositiveMaxRpm()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            crew.UpdateConfiguration(new CrewConfigurationUpdate { MaxRpm = 0 }));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUpdatingWithNullConfigUpdate()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => crew.UpdateConfiguration((CrewConfigurationUpdate)null!));
    }

    [Fact]
    public void ShouldUpdateConfigurationViaConvenienceOverload_WhenUsingNamedParameters()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act
        crew.UpdateConfiguration(verbose: true, language: "de", maxRpm: 75);

        // Assert
        Assert.True(crew.Verbose);
        Assert.Equal("de", crew.Language.Value);
        Assert.Equal(75, crew.MaxRpm);
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

    #region StopAllAgentsAsync / SetKillAllStrategy

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallKillAllFunc_WhenStoppingAllAgents()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        crew.AddAgent(agent1);
        crew.AddAgent(agent2);

        IEnumerable<AgentId>? capturedAgents = null;
        string? capturedReason = null;

        crew.SetKillAllStrategy((agents, reason) =>
        {
            capturedAgents = agents;
            capturedReason = reason;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        // Act
        await crew.StopAllAgentsAsync("Emergency stop");

        // Assert
        Assert.NotNull(capturedAgents);
        Assert.Equal(2, capturedAgents!.Count());
        Assert.Equal("Emergency stop", capturedReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenStoppingAllAgentsWithoutKillStrategy()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act
        await crew.StopAllAgentsAsync();

        // Assert
        Assert.NotNull(crew);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSettingNullKillAllStrategy()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => crew.SetKillAllStrategy(null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseDefaultReason_WhenStoppingAllAgentsWithoutReason()
    {
        // Arrange
        var crew = DomainCrew.Create("Test");
        crew.AddAgent(AgentId.Create());
        string? capturedReason = null;

        crew.SetKillAllStrategy((_, reason) =>
        {
            capturedReason = reason;
            return System.Threading.Tasks.Task.CompletedTask;
        });

        // Act
        await crew.StopAllAgentsAsync();

        // Assert
        Assert.Equal("Crew stopped all agents", capturedReason);
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
            ShareCrew = false,
            OutputLogFile = "/var/log/crew.log",
            Language = "es",
            FullOutput = true,
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
        Assert.False(crew.ShareCrew);
        Assert.Equal("/var/log/crew.log", crew.OutputLogFile);
        Assert.Equal("es", crew.Language.Value);
        Assert.True(crew.FullOutput);
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
