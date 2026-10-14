using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Interfaces.Services;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Hand-written in-memory <see cref="ICrewRepository"/>.</summary>
public sealed class FakeCrewRepository : ICrewRepository
{
    private readonly Dictionary<Ulid, DomainCrew> _crews = [];
    public void Add(DomainCrew crew) => _crews[crew.Id.Value] = crew;

    public Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken ct = default)
    {
        _crews.TryGetValue(id.Value, out var c);
        return Task.FromResult<DomainCrew?>(c);
    }

    public Task AddAsync(DomainCrew crew, CancellationToken ct = default) { _crews[crew.Id.Value] = crew; return Task.CompletedTask; }
    public Task UpdateAsync(DomainCrew crew, CancellationToken ct = default) { _crews[crew.Id.Value] = crew; return Task.CompletedTask; }
    public Task DeleteAsync(CrewId id, CancellationToken ct = default) { _crews.Remove(id.Value); return Task.CompletedTask; }
    public Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(IEnumerable<CrewId> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(CrewStatus status, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(ProcessType pt, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(AgentId id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(TaskId id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(DateTime since, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(CrewId id, CancellationToken ct = default) => Task.FromResult(new CrewExecutionStatistics(0, 0, 0, 0.0, 0.0, null));
    public Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> spec, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> spec, int skip, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
    public Task<int> CountAsync(ISpecification<DomainCrew> spec, CancellationToken ct = default) => Task.FromResult(0);
    public Task<bool> AnyAsync(ISpecification<DomainCrew> spec, CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> ExistsAsync(CrewId id, CancellationToken ct = default) => Task.FromResult(_crews.ContainsKey(id.Value));
    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_crews.Count);
}
