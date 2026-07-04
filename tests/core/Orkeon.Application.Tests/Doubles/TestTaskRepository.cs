using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// In-memory test double for ITaskRepository.
/// </summary>
public sealed class TestTaskRepository : ITaskRepository
{
    private readonly List<CrewTask> _tasks = [];

    /// <summary>Gets the list of stored tasks for assertion.</summary>
    public IReadOnlyList<CrewTask> StoredTasks => _tasks.AsReadOnly();

    /// <summary>Tracks whether AddAsync was called.</summary>
    public int AddAsyncCallCount { get; private set; }

    // ITaskRepository.AddAsync (new overload returning CrewTask)
    public System.Threading.Tasks.Task<CrewTask> AddAsync(CrewTask task, CancellationToken cancellationToken = default)
    {
        AddAsyncCallCount++;
        _tasks.Add(task);
        return System.Threading.Tasks.Task.FromResult(task);
    }

    // IRepository<CrewTask, TaskId>.AddAsync (base)
    System.Threading.Tasks.Task IRepository<CrewTask, TaskId>.AddAsync(CrewTask aggregate, CancellationToken cancellationToken)
    {
        AddAsyncCallCount++;
        _tasks.Add(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<CrewTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.FirstOrDefault(t => t.Id == id));

    public System.Threading.Tasks.Task UpdateAsync(CrewTask aggregate, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    // ITaskRepository.DeleteAsync (overload returning bool)
    public System.Threading.Tasks.Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        var removed = _tasks.RemoveAll(t => t.Id == id);
        return System.Threading.Tasks.Task.FromResult(removed > 0);
    }

    // IRepository<CrewTask, TaskId>.DeleteAsync (base)
    System.Threading.Tasks.Task IRepository<CrewTask, TaskId>.DeleteAsync(TaskId id, CancellationToken cancellationToken)
    {
        _tasks.RemoveAll(t => t.Id == id);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Any(t => t.Id == id));

    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Count);

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(_tasks.Where(t => ids.Contains(t.Id)).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(_tasks.Where(t => t.AssignedAgent == agentId).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByStatusAsync(TaskStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(new List<CrewTask>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(new List<CrewTask>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(new List<CrewTask>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(new List<CrewTask>().AsReadOnly());

    public System.Threading.Tasks.Task<bool> IsCompletedAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(false);

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(new List<CrewTask>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> FindAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(_tasks.Where(specification.IsSatisfiedBy).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> FindAsync(ISpecification<CrewTask> specification, int skip, int take, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(_tasks.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Count(specification.IsSatisfiedBy));

    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Any(specification.IsSatisfiedBy));
}
