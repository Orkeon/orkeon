using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Persistence.Agent;

/// <summary>
/// Scoped <see cref="IAgentRepository"/> backed by the singleton
/// <see cref="IAgentRegistrationStore"/> (R4.6 / ANT-001 — decision "scoped per request").
/// <para>
/// Each A2A request (and each pipeline scope) gets its own repository instance with the
/// scoped <see cref="IUnitOfWork"/> for domain-event tracking, while the agent data lives
/// in the shared singleton store: every call hydrates from the current shared state, so
/// agents registered in one scope are visible to all other scopes (in particular to the
/// per-request scopes opened by <c>A2AServer</c>/<c>A2ATaskRouter</c>).
/// </para>
/// </summary>
public sealed class SharedStoreAgentRepository : IAgentRepository
{
    private readonly IAgentRegistrationStore _store;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="SharedStoreAgentRepository"/>.
    /// </summary>
    /// <param name="store">The singleton backing store shared across scopes.</param>
    /// <param name="unitOfWork">The scoped unit of work used to track aggregates for domain event dispatch.</param>
    public SharedStoreAgentRepository(IAgentRegistrationStore store, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _store = store;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.GetById(id));

    /// <inheritdoc />
    public System.Threading.Tasks.Task AddAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store.TryAdd(aggregate);
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task UpdateAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store.Save(aggregate);
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task DeleteAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        _store.Remove(id);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> ExistsAsync(AgentId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Contains(id));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Count);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Snapshot().Where(specification.IsSatisfiedBy).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Snapshot().Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Snapshot().Count(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Snapshot().Any(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(IEnumerable<AgentId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToHashSet();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Snapshot().Where(a => idSet.Contains(a.Id)).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Snapshot().Where(a => a.Role == role).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Snapshot().Where(a => a.Status == status).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Snapshot().Where(a => a.Status != AgentStatus.Deactivated).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(IEnumerable<ToolId> toolIds, CancellationToken cancellationToken = default)
    {
        // ToolId is Guid-based but ITool uses string Name — return agents that have any tools
        // as a reasonable approximation for the in-memory implementation
        // (mirrors InMemoryAgentRepository).
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(
            _store.Snapshot().Where(a => a.Tools.Count > 0).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(CrewId crewId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
}
