using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// Additional tests for Agent aggregate root covering gaps identified in Phase 1 audit.
/// Focuses on: ToolAccessPolicy, status transitions, delegation strategy, and creation
/// with templates.
/// </summary>
public class AgentAggregateRootTests
{
    #region Test Doubles

    private sealed class StubTool : IBaseTool
    {
        public string Name { get; }
        public string Description => $"Stub tool {Name}";
        public Orkeon.Domain.Tools.Protocol.ToolSchema Schema => new(Name, Description, []);

        public StubTool(string name) => Name = name;

        public System.Threading.Tasks.Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new Orkeon.Domain.Tools.Protocol.ToolCallResponse(true, "ok", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("ok"));

        public bool ValidateInput(string input) => true;
    }

    private static DomainAgent CreateIdleAgent(
        string role = "Tester",
        string goal = "Run tests",
        bool allowDelegation = false)
    {
        return DomainAgent.Create(
            role: AgentRole.From(role),
            goal: AgentGoal.From(goal),
            allowDelegation: allowDelegation);
    }

    #endregion

    #region ToolAccessPolicy Tests

    [Fact]
    public void ShouldSetUnrestrictedPolicyByDefault_WhenCreatingAgent()
    {
        // Arrange & Act
        var agent = CreateIdleAgent();

        // Assert
        Assert.NotNull(agent.ToolAccessPolicy);
        Assert.True(agent.ToolAccessPolicy.IsUnrestricted);
    }

    [Fact]
    public void ShouldSetCustomToolAccessPolicy_WhenCreatingWithOptions()
    {
        // Arrange
        var policy = ToolAccessPolicy.CreateWhitelist("ToolA", "ToolB");
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("Dev"),
            Goal = AgentGoal.From("Code"),
            ToolAccessPolicy = policy
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.False(agent.ToolAccessPolicy.IsUnrestricted);
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed("ToolA"));
        Assert.True(agent.ToolAccessPolicy.IsToolAllowed("ToolB"));
        Assert.False(agent.ToolAccessPolicy.IsToolAllowed("ToolC"));
    }

    [Fact]
    public void ShouldUpdateToolAccessPolicy_WhenAgentIsIdle()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var newPolicy = ToolAccessPolicy.CreateBlacklist("DangerousTool");

        // Act
        agent.UpdateToolAccessPolicy(newPolicy);

        // Assert
        Assert.Equal(newPolicy, agent.ToolAccessPolicy);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingToolAccessPolicyWhileBusy()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId); // Now Busy

        var newPolicy = ToolAccessPolicy.CreateWhitelist("SafeTool");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => agent.UpdateToolAccessPolicy(newPolicy));
        Assert.Contains("while agent is working", ex.Message);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingToolAccessPolicyWithNull()
    {
        // Arrange
        var agent = CreateIdleAgent();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => agent.UpdateToolAccessPolicy(null!));
    }

    #endregion

    #region Status Transition Tests

    [Fact]
    public void ShouldTransitionFromIdleToBusy_WhenStartingTask()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);

        // Act
        agent.StartTask(taskId);

        // Assert
        Assert.Equal(AgentStatus.Busy, agent.Status);
        Assert.Equal([taskId], agent.CurrentTasks);
    }

    [Fact]
    public void ShouldTransitionFromBusyToIdle_WhenCompletingTask()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act
        agent.CompleteTask(taskId, TaskOutput.Text("Done"));

        // Assert
        Assert.Equal(AgentStatus.Idle, agent.Status);
        Assert.Empty(agent.CurrentTasks);
    }

    [Fact]
    public void ShouldTransitionFromBusyToIdle_WhenFailingTask()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var taskId = TaskId.Create();
        agent.AssignTask(taskId);
        agent.StartTask(taskId);

        // Act
        agent.FailTask(taskId, "Something went wrong");

        // Assert
        Assert.Equal(AgentStatus.Idle, agent.Status);
        Assert.Empty(agent.CurrentTasks);
    }

    [Fact]
    public void ShouldAllowReassignment_AfterCompletingTask()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();

        agent.AssignTask(taskId1);
        agent.StartTask(taskId1);
        agent.CompleteTask(taskId1, TaskOutput.Text("Done"));

        // Act - should be able to assign and start a second task
        agent.AssignTask(taskId2);
        agent.StartTask(taskId2);

        // Assert
        Assert.Equal(AgentStatus.Busy, agent.Status);
        Assert.Equal([taskId2], agent.CurrentTasks);
    }

    [Fact]
    public void ShouldAllowReassignment_AfterFailingTask()
    {
        // Arrange
        var agent = CreateIdleAgent();
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();

        agent.AssignTask(taskId1);
        agent.StartTask(taskId1);
        agent.FailTask(taskId1, "Error");

        // Act
        agent.AssignTask(taskId2);
        agent.StartTask(taskId2);

        // Assert
        Assert.Equal(AgentStatus.Busy, agent.Status);
        Assert.Equal([taskId2], agent.CurrentTasks);
    }

    #endregion

    #region Delegation Strategy Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseInjectedSelectionStrategy_WhenDelegating()
    {
        // Arrange
        var agent = CreateIdleAgent(allowDelegation: true);
        var targetAgentId = AgentId.Create();
        var otherAgentId = AgentId.Create();

        agent.SetAgentSelectionStrategy((task, agents, ct) =>
            System.Threading.Tasks.Task.FromResult<AgentId?>(targetAgentId));

        var stubTask = new StubCrewTask();

        // Act
        var decision = await agent.ShouldDelegateAsync(stubTask, [otherAgentId, targetAgentId], TestContext.Current.CancellationToken);

        // Assert
        Assert.True(decision.ShouldDelegate);
        Assert.Equal(targetAgentId, decision.DelegateToAgentId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFallbackToFirstOtherAgent_WhenNoSelectionStrategy()
    {
        // Arrange
        var agent = CreateIdleAgent(allowDelegation: true);
        var otherAgentId = AgentId.Create();
        var stubTask = new StubCrewTask();

        // Act - no strategy set, should use fallback logic
        var decision = await agent.ShouldDelegateAsync(stubTask, [otherAgentId], TestContext.Current.CancellationToken);

        // Assert
        Assert.True(decision.ShouldDelegate);
        Assert.Equal(otherAgentId, decision.DelegateToAgentId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNoDelegation_WhenStrategyReturnsNull()
    {
        // Arrange
        var agent = CreateIdleAgent(allowDelegation: true);
        agent.SetAgentSelectionStrategy((task, agents, ct) =>
            System.Threading.Tasks.Task.FromResult<AgentId?>(null));

        var stubTask = new StubCrewTask();

        // Act
        var decision = await agent.ShouldDelegateAsync(stubTask, [AgentId.Create()], TestContext.Current.CancellationToken);

        // Assert
        Assert.False(decision.ShouldDelegate);
    }

    #endregion

    #region Create with Templates Tests

    [Fact]
    public void ShouldSetAllTemplates_WhenCreatingWithOptions()
    {
        // Arrange
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("Bot"),
            Goal = AgentGoal.From("Automate"),
            SystemTemplate = "You are a bot",
            PromptTemplate = "Process: {task}",
            ResponseTemplate = "Output: {result}"
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.Equal("You are a bot", agent.SystemTemplate);
        Assert.Equal("Process: {task}", agent.PromptTemplate);
        Assert.Equal("Output: {result}", agent.ResponseTemplate);
    }

    [Fact]
    public void ShouldSetMaxExecutionTime_WhenCreatingWithOptions()
    {
        // Arrange
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From(RoleWorker),
            Goal = AgentGoal.From("Work"),
            MaxExecutionTime = TimeoutExtended
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.Equal(TimeoutExtended, agent.MaxExecutionTime);
    }

    [Fact]
    public void ShouldSetTheProviderTheAgentRunsOn_WhenCreatingWithOptions()
    {
        // Arrange
        var llmProvider = new StubLlmProvider();
        var options = new AgentCreateOptions
        {
            Role = AgentRole.From("Caller"),
            Goal = AgentGoal.From("Call functions"),
            Llm = llmProvider
        };

        // Act
        var agent = DomainAgent.Create(options);

        // Assert
        Assert.NotNull(agent.Llm);
        Assert.Equal("StubLlm", agent.Llm.Name);
    }

    #endregion

    #region Test Doubles (continued)

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

    private sealed class StubCrewTask : Domain.Task.ICrewTask
    {
        public TaskId TaskId { get; } = TaskId.Create();
        public TaskDescription Description { get; } = TaskDescription.From("Test");
        public ExpectedOutput ExpectedOutput { get; } = ExpectedOutput.From("Expected");
        public AgentId? AssignedAgent => null;
        public Domain.Task.ValueObjects.TaskStatus Status => Domain.Task.ValueObjects.TaskStatus.Pending;
        public TaskOutput? Output => null;
        public IReadOnlyList<TaskId> Dependencies => [];
        public DateTime CreatedAt => DateTime.UtcNow;
        public DateTime? StartedAt => null;
        public DateTime? CompletedAt => null;
        public bool AsyncExecution => false;
        public Domain.Task.JsonSchema? OutputJson => null;
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
