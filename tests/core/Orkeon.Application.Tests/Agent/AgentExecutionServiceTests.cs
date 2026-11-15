using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Agent;
using Orkeon.Application.Tests.Services;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Application.Tests.Agent;

/// <summary>
/// Tests for the CastToDomainTask helper in AgentExecutionService.
/// </summary>
public class AgentExecutionServiceTests
{
    [Fact]
    public void CastToDomainTask_WithCrewTask_ReturnsCastTask()
    {
        // Arrange
        ICrewTask task = CrewTask.Create(
            TaskDescription.From("Test task"),
            ExpectedOutput.From("Expected output"));

        // Act
        var result = AgentExecutionService.CastToDomainTask(task);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<CrewTask>(result);
        Assert.Same(task, result);
    }

    [Fact]
    public void CastToDomainTask_WithNullTask_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            AgentExecutionService.CastToDomainTask(null!));
    }

    [Fact]
    public void CastToDomainTask_WithNonCrewTask_ThrowsInvalidOperationException()
    {
        // Arrange
        ICrewTask task = new FakeNonCrewTask();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AgentExecutionService.CastToDomainTask(task));
        Assert.Contains("Task must be a Domain Task", ex.Message);
    }

    [Fact]
    [Trait("BR", "CH-29")]
    public async System.Threading.Tasks.Task ShouldCarryTheCapFlag_WhenTheTypedTaskEndedOnItsCap()
    {
        var result = await RunTypedAsync(endedOnIterationCap: true);

        Assert.True(result.Success);
        Assert.True(result.EndedOnIterationCap);
    }

    [Fact]
    [Trait("BR", "CH-29")]
    public async System.Threading.Tasks.Task ShouldNotCarryTheCapFlag_WhenTheTypedTaskDidNotEndOnIt()
    {
        var plain = await RunTypedAsync(endedOnIterationCap: false);

        Assert.True(plain.Success);
        Assert.False(plain.EndedOnIterationCap);
    }

    [Fact]
    [Trait("BR", "CH-42")]
    public async System.Threading.Tasks.Task ShouldCopyTheCacheTokensAndEndTime_WhenATypedResultIsConverted()
    {
        var endedAt = new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero);

        var result = await RunTypedAsync(endedOnIterationCap: false, cacheHit: 120L, cacheMiss: 30L, endedAt: endedAt);

        Assert.Equal(120L, result.CacheHitTokens);
        Assert.Equal(30L, result.CacheMissTokens);
        Assert.Equal(endedAt, result.EndedAt);
    }

    private static async System.Threading.Tasks.Task<TaskResult<Answer>> RunTypedAsync(
        bool endedOnIterationCap, long? cacheHit = null, long? cacheMiss = null, DateTimeOffset? endedAt = null)
    {
        var orchestrator = new TestExecutionOrchestrator();
        orchestrator.SetupResult(new TaskResult(true, "raw", null, [], TimeSpan.Zero) { EndedOnIterationCap = endedOnIterationCap, CacheHitTokens = cacheHit, CacheMissTokens = cacheMiss, EndedAt = endedAt });
        var service = new AgentExecutionService(
            NullLogger<AgentExecutionService>.Instance,
            orchestrator,
            new TestCallbackOrchestrator(),
            new TestMemoryCoordinator());
        var agent = new AgentBuilder().Role("Worker").Goal("Work").Build();
        var task = new CrewTaskBuilder().WithKey("t").Description("do t").ExpectedOutput("done").Build();
        var context = new SimpleExecutionContext(CrewId.Create(), new Dictionary<string, string>(), NullMemoryScope.Instance, [], CancellationToken.None);

        return await service.ExecuteTaskAsync<Answer>(agent, task, context, TestContext.Current.CancellationToken);
    }

    private sealed record Answer(string Text);

    /// <summary>
    /// A stub implementation of ICrewTask that is NOT a CrewTask,
    /// used to verify the cast failure path.
    /// </summary>
    private sealed class FakeNonCrewTask : ICrewTask
    {
        public TaskId TaskId { get; } = TaskId.Create();
        public TaskDescription Description { get; } = TaskDescription.From("Fake");
        public ExpectedOutput ExpectedOutput { get; } = ExpectedOutput.From("Fake");
        public AgentId? AssignedAgent => null;
        public TaskStatus Status => TaskStatus.Pending;
        public TaskOutput? Output => null;
        public IReadOnlyList<TaskId> Dependencies { get; } = [];
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
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
        public string GetContextSummary() => "Fake";
        public TimeSpan GetExecutionTime() => TimeSpan.Zero;
    }
}
