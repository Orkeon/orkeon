using System.Collections.Concurrent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Interfaces.Ports;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Persistence.Crew;

/// <summary>In-memory implementation of <see cref="ICrewRepository"/> for testing and development.</summary>
public sealed class InMemoryCrewRepository : ICrewRepository
{
    private readonly ConcurrentDictionary<CrewId, DomainCrew> _store = new();
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryCrewRepository"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work used to track aggregates for domain event dispatch.</param>
    public InMemoryCrewRepository(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(id, out var crew) ? crew : null);

    /// <inheritdoc />
    public System.Threading.Tasks.Task AddAsync(DomainCrew aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store.TryAdd(aggregate.Id, aggregate);
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task UpdateAsync(DomainCrew aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store[aggregate.Id] = aggregate;
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task DeleteAsync(CrewId id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> ExistsAsync(CrewId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.ContainsKey(id));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Count);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(specification.IsSatisfiedBy).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Count(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Any(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(IEnumerable<CrewId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToHashSet();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(c => idSet.Contains(c.Id)).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(CrewStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(c => c.Status == status).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(ProcessType processType, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(c => c.ProcessType == processType).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(c => c.Agents.Contains(agentId)).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_store.Values.Where(c => c.Tasks.Contains(taskId)).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(DateTime since, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>([]);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(CrewId crewId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new CrewExecutionStatistics(0, 0, 0, 0.0, 0.0, null));
}
