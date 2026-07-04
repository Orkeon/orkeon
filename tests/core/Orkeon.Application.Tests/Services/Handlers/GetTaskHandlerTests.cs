using Orkeon.Application.Task.Queries.GetTask;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainTaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using DomainTaskPriority = Orkeon.Domain.Task.ValueObjects.TaskPriority;

namespace Orkeon.Application.Tests.Services.Handlers;

public class GetTaskHandlerTests
{
    private readonly StubTaskRepository _repository;
    private readonly TestLogger<GetTaskHandler> _logger;
    private readonly GetTaskHandler _sut;

    public GetTaskHandlerTests()
    {
        _repository = new StubTaskRepository();
        _logger = new TestLogger<GetTaskHandler>();
        _sut = new GetTaskHandler(_repository, _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnTask_WhenTaskExists()
    {
        // Arrange
        var task = CrewTask.Create(
            TaskDescription.From("Write unit tests"),
            ExpectedOutput.From("All tests passing"));
        _repository.SeedTask(task);
        var query = new GetTaskQuery(task.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(task.Id.ToString(), result!.Id);
        Assert.Equal("Write unit tests", result.Description);
        Assert.Equal("All tests passing", result.ExpectedOutput);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNull_WhenTaskNotFound()
    {
        // Arrange
        var query = new GetTaskQuery(Guid.NewGuid());

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.True(_logger.HasLoggedMessage("not found"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMapAllProperties_WhenReturningDto()
    {
        // Arrange
        var task = CrewTask.Create(
            TaskDescription.From("Analyze data patterns"),
            ExpectedOutput.From("Pattern report"),
            DomainTaskPriority.High);
        _repository.SeedTask(task);
        var query = new GetTaskQuery(task.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Analyze data patterns", result!.Description);
        Assert.Equal("Pattern report", result.ExpectedOutput);
        Assert.Equal("High", result.Priority);
        Assert.Equal("Pending", result.Status);
        Assert.NotEqual(default, result.CreatedAt);
    }

    [Theory]
    [InlineData("Low", "Low")]
    [InlineData("Normal", "Normal")]
    [InlineData("High", "High")]
    [InlineData("Critical", "Critical")]
    public async System.Threading.Tasks.Task ShouldMapPriority_WhenDifferentPrioritiesUsed(
        string domainPriorityStr,
        string expectedPriority)
    {
        // Arrange
        var domainPriority = DomainTaskPriority.From(domainPriorityStr);
        var task = CrewTask.Create(
            TaskDescription.From("Priority test task"),
            ExpectedOutput.From("Expected output"),
            domainPriority);
        _repository.SeedTask(task);
        var query = new GetTaskQuery(task.Id);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedPriority, result!.Priority);
    }

    /// <summary>
    /// Stub repository that returns a pre-seeded task by ID.
    /// </summary>
    private sealed class StubTaskRepository : ITaskRepository
    {
        private readonly Dictionary<string, CrewTask> _tasks = [];

        public void SeedTask(CrewTask task) => _tasks[task.Id.ToString()] = task;

        public System.Threading.Tasks.Task<CrewTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
        {
            _tasks.TryGetValue(id.ToString(), out var task);
            return System.Threading.Tasks.Task.FromResult(task);
        }

        // Not used by GetTaskHandler but required by interface
        public System.Threading.Tasks.Task AddAsync(CrewTask aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        System.Threading.Tasks.Task<CrewTask> ITaskRepository.AddAsync(CrewTask task, CancellationToken cancellationToken) => System.Threading.Tasks.Task.FromResult(task);
        public System.Threading.Tasks.Task UpdateAsync(CrewTask aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task DeleteAsync(TaskId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        System.Threading.Tasks.Task<bool> ITaskRepository.DeleteAsync(TaskId id, CancellationToken cancellationToken) => System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Tasks.Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> FindAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> FindAsync(ISpecification<CrewTask> specification, int skip, int take, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<int> CountAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByStatusAsync(DomainTaskStatus status, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByPriorityAsync(DomainTaskPriority priority, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
        public System.Threading.Tasks.Task<bool> IsCompletedAsync(TaskId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>([]);
    }
}
