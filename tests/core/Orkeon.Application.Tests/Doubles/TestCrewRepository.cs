using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewStatus = Orkeon.Domain.Crew.ValueObjects.CrewStatus;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// In-memory test double for ICrewRepository.
/// </summary>
public sealed class TestCrewRepository : ICrewRepository
{
    private readonly List<DomainCrew> _crews = [];

    /// <summary>Gets the list of stored crews for assertion.</summary>
    public IReadOnlyList<DomainCrew> StoredCrews => _crews.AsReadOnly();

    /// <summary>Tracks whether AddAsync was called.</summary>
    public int AddAsyncCallCount { get; private set; }

    public System.Threading.Tasks.Task AddAsync(DomainCrew aggregate, CancellationToken cancellationToken = default)
    {
        AddAsyncCallCount++;
        _crews.Add(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken cancellationToken = default)
    {
        DomainCrew? result = _crews.FirstOrDefault(c => c.Id == id);
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task UpdateAsync(DomainCrew aggregate, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task DeleteAsync(CrewId id, CancellationToken cancellationToken = default)
    {
        _crews.RemoveAll(c => c.Id == id);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> ExistsAsync(CrewId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_crews.Any(c => c.Id == id));

    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_crews.Count);

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(IEnumerable<CrewId> ids, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_crews.Where(c => ids.Contains(c.Id)).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(DomainCrewStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_crews.Where(c => c.Status == status).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(ProcessType processType, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_crews.Where(c => c.ProcessType == processType).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(new List<DomainCrew>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(TaskId taskId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(new List<DomainCrew>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(DateTime since, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(new List<DomainCrew>().AsReadOnly());

    public System.Threading.Tasks.Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(CrewId crewId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(new CrewExecutionStatistics(0, 0, 0, 0.0, 0.0, null));

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_crews.Where(c => specification.IsSatisfiedBy(c)).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> specification, int skip, int take, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainCrew>>(_crews.Where(c => specification.IsSatisfiedBy(c)).Skip(skip).Take(take).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_crews.Count(c => specification.IsSatisfiedBy(c)));

    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainCrew> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_crews.Any(c => specification.IsSatisfiedBy(c)));
}
