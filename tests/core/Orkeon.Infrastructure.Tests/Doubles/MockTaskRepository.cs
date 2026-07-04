using Orkeon.Domain.Common;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Domain.Task.ValueObjects;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for ITaskRepository with call tracking and in-memory storage.
/// </summary>
public class MockTaskRepository : ITaskRepository
{
    private readonly Dictionary<string, DomainTask> _tasks = [];

    // --- Tracking ---
    public int GetByIdCallCount { get; private set; }
    public TaskId? LastGetByIdArg { get; private set; }

    public int GetByIdsCallCount { get; private set; }
    public int GetByAgentCallCount { get; private set; }
    public int GetByStatusCallCount { get; private set; }
    public int GetDependentTasksCallCount { get; private set; }
    public int GetReadyTasksCallCount { get; private set; }
    public int GetByPriorityCallCount { get; private set; }
    public int AddCallCount { get; private set; }
    public DomainTask? LastAddedTask { get; private set; }
    public int UpdateCallCount { get; private set; }
    public DomainTask? LastUpdatedTask { get; private set; }
    public int DeleteCallCount { get; private set; }
    public TaskId? LastDeletedId { get; private set; }
    public int ExistsCallCount { get; private set; }
    public int IsCompletedCallCount { get; private set; }
    public int GetByDateRangeCallCount { get; private set; }
    // --- Configuration ---
    public void AddTaskToStore(DomainTask task)
    {
        _tasks[task.Id] = task;
    }

    private static string ToKey(TaskId id) => id.Value.ToString();

    // --- ITaskRepository ---
    public System.Threading.Tasks.Task<DomainTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        GetByIdCallCount++;
        LastGetByIdArg = id;
        _tasks.TryGetValue(ToKey(id), out var task);
        return System.Threading.Tasks.Task.FromResult(task);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default)
    {
        GetByIdsCallCount++;
        var keys = ids.Select(ToKey).ToHashSet();
        var result = _tasks
            .Where(kv => keys.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();
        IReadOnlyList<DomainTask> readOnly = result.AsReadOnly();
        return System.Threading.Tasks.Task.FromResult(readOnly);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        GetByAgentCallCount++;
        IReadOnlyList<DomainTask> result = _tasks.Values.ToList().AsReadOnly();
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetByStatusAsync(TaskStatus status, CancellationToken cancellationToken = default)
    {
        GetByStatusCallCount++;
        IReadOnlyList<DomainTask> result = _tasks.Values.ToList().AsReadOnly();
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default)
    {
        GetDependentTasksCallCount++;
        IReadOnlyList<DomainTask> result = Array.Empty<DomainTask>();
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default)
    {
        GetReadyTasksCallCount++;
        IReadOnlyList<DomainTask> result = _tasks.Values.ToList().AsReadOnly();
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default)
    {
        GetByPriorityCallCount++;
        IReadOnlyList<DomainTask> result = _tasks.Values.ToList().AsReadOnly();
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task<DomainTask> AddAsync(DomainTask task, CancellationToken cancellationToken = default)
    {
        AddCallCount++;
        LastAddedTask = task;
        _tasks[task.Id] = task;
        return System.Threading.Tasks.Task.FromResult(task);
    }

    System.Threading.Tasks.Task IRepository<DomainTask, TaskId>.AddAsync(DomainTask aggregate, CancellationToken cancellationToken)
    {
        AddCallCount++;
        LastAddedTask = aggregate;
        _tasks[aggregate.Id] = aggregate;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task UpdateAsync(DomainTask task, CancellationToken cancellationToken = default)
    {
        UpdateCallCount++;
        LastUpdatedTask = task;
        _tasks[task.Id] = task;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;
        LastDeletedId = id;
        return System.Threading.Tasks.Task.FromResult(_tasks.Remove(ToKey(id)));
    }

    System.Threading.Tasks.Task IRepository<DomainTask, TaskId>.DeleteAsync(TaskId id, CancellationToken cancellationToken)
    {
        DeleteCallCount++;
        LastDeletedId = id;
        _tasks.Remove(ToKey(id));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        ExistsCallCount++;
        return System.Threading.Tasks.Task.FromResult(_tasks.ContainsKey(ToKey(id)));
    }

    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Count);

    public System.Threading.Tasks.Task<bool> IsCompletedAsync(TaskId id, CancellationToken cancellationToken = default)
    {
        IsCompletedCallCount++;
        return System.Threading.Tasks.Task.FromResult(false);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        GetByDateRangeCallCount++;
        IReadOnlyList<DomainTask> result = _tasks.Values.ToList().AsReadOnly();
        return System.Threading.Tasks.Task.FromResult(result);
    }

    // ISpecificationRepository methods
    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainTask>>(_tasks.Values.Where(specification.IsSatisfiedBy).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, int skip, int take, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainTask>>(_tasks.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Values.Count(specification.IsSatisfiedBy));

    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_tasks.Values.Any(specification.IsSatisfiedBy));
}
