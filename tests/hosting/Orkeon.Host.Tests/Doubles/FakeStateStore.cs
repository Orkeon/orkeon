using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>A checkpoint store that holds nothing, and fails every read once <see cref="Throws"/> is set.</summary>
internal sealed class FakeStateStore : IStateStore
{
    public bool Throws { get; set; }

    public Task SaveAsync(SessionState state, CancellationToken ct = default) => Task.CompletedTask;

    public Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default) =>
        Throws ? throw new IOException("the store is down") : Task.FromResult<SessionState?>(null);

    public Task<SessionState?> GetLatestForCrewAsync(string crewKey, CancellationToken ct = default) =>
        Task.FromResult<SessionState?>(null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SessionSummary>>([]);

    public Task DeleteAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
}
