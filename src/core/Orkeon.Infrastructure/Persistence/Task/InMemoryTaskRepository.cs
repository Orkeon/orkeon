using System.Collections.Concurrent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Application.Interfaces.Ports;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Infrastructure.Persistence.Task;

/// <summary>In-memory implementation of <see cref="ITaskRepository"/> for testing and development.</summary>
public sealed class InMemoryTaskRepository : ITaskRepository
{
    private readonly ConcurrentDictionary<TaskId, CrewTask> _store = new();
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryTaskRepository"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work used to track aggregates for domain event dispatch.</param>
    public InMemoryTaskRepository(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<CrewTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(id, out var task) ? task : null);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToHashSet();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => idSet.Contains(t.Id)).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => t.AssignedAgent == agentId).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByStatusAsync(TaskStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => t.Status == status).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => t.Dependencies.Contains(taskId)).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => t.Status == TaskStatus.Pending &&
                t.Dependencies.All(dep => _store.TryGetValue(dep, out var depTask) && depTask.Status == TaskStatus.Completed))
            .ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => t.Priority == priority).ToList());

    /// <inheritdoc cref="ITaskRepository.AddAsync" />
    public System.Threading.Tasks.Task<CrewTask> AddAsync(CrewTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        _store.TryAdd(task.Id, task);
        _unitOfWork.Track(task);
        return System.Threading.Tasks.Task.FromResult(task);
    }

    /// <summary>Explicit implementation of <see cref="IRepository{TAggregate, TId}.AddAsync"/>.</summary>
    System.Threading.Tasks.Task IRepository<CrewTask, TaskId>.AddAsync(CrewTask aggregate, CancellationToken cancellationToken)
    {
        _store.TryAdd(aggregate.Id, aggregate);
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task UpdateAsync(CrewTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        _store[task.Id] = task;
        _unitOfWork.Track(task);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc cref="ITaskRepository.DeleteAsync" />
    public System.Threading.Tasks.Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.TryRemove(id, out _));

    /// <summary>Explicit implementation of <see cref="IRepository{TAggregate, TId}.DeleteAsync"/>.</summary>
    System.Threading.Tasks.Task IRepository<CrewTask, TaskId>.DeleteAsync(TaskId id, CancellationToken cancellationToken)
    {
        _store.TryRemove(id, out _);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.ContainsKey(id));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Count);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> IsCompletedAsync(TaskId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(
            _store.TryGetValue(id, out var task) && task.Status == TaskStatus.Completed);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(
            _store.Values.Where(t => t.CreatedAt >= startDate && t.CreatedAt <= endDate).ToList());

    // ISpecificationRepository methods

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> FindAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(            _store.Values.Where(specification.IsSatisfiedBy).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> FindAsync(ISpecification<CrewTask> specification, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<CrewTask>>(            _store.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Count(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<CrewTask> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Any(specification.IsSatisfiedBy));
    }
}
