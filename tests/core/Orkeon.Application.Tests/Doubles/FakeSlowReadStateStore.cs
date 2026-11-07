using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IStateStore"/> without time travel whose read hands the thread back before
/// it answers, so two writers that read, change and save the same session interleave on purpose.
/// </summary>
public sealed class FakeSlowReadStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    public System.Threading.Tasks.Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        _sessions[state.SessionId] = state;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public async System.Threading.Tasks.Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var found = _sessions.TryGetValue(sessionId, out var state) ? state : null;
        await System.Threading.Tasks.Task.Delay(1, ct).ConfigureAwait(false);
        return found;
    }

    public System.Threading.Tasks.Task<SessionState?> GetLatestForCrewAsync(string crewKey, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.FromResult(_sessions.Values
            .Where(s => s.CrewKey == crewKey)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefault());

    public System.Threading.Tasks.Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
