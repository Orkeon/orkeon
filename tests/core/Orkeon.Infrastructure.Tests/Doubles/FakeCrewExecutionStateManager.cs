using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Application.Interfaces.Services;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>Hand-written in-memory <see cref="ICrewExecutionStateManager"/>.</summary>
public sealed class FakeCrewExecutionStateManager : ICrewExecutionStateManager
{
    private readonly Dictionary<Ulid, CrewExecutionState> _byCrew = [];
    private readonly Dictionary<string, CrewExecutionState> _byExec = [];
    public int CompleteExecutionCount { get; private set; }

    public Task<CrewExecutionState> GetStateAsync(CrewId crewId, CancellationToken ct = default)
    {
        _byCrew.TryGetValue(crewId.Value, out var s);
        return Task.FromResult(s ?? new CrewExecutionState(crewId, ExecutionId.New(), CrewInput.Empty()));
    }

    public Task SaveStateAsync(CrewExecutionState state, CancellationToken ct = default) { _byCrew[state.CrewId.Value] = state; return Task.CompletedTask; }
    public Task ClearStateAsync(CrewId crewId, CancellationToken ct = default) { _byCrew.Remove(crewId.Value); return Task.CompletedTask; }

    public Task<CrewExecutionState> CreateStateAsync(CrewId crewId, CancellationToken ct = default)
    {
        var exec = ExecutionId.New();
        var s = new CrewExecutionState(crewId, exec, CrewInput.Empty());
        _byCrew[crewId.Value] = s;
        _byExec[exec.AsString()] = s;
        return Task.FromResult(s);
    }

    public Task<CrewExecutionState> CreateStateAsync(CrewId crewId, ExecutionId executionId, CrewInput input, CancellationToken ct = default)
    {
        var s = new CrewExecutionState(crewId, executionId, input);
        _byCrew[crewId.Value] = s;
        _byExec[executionId.AsString()] = s;
        return Task.FromResult(s);
    }

    public Task<CrewExecutionState?> GetStateAsync(ExecutionId executionId, CancellationToken ct = default)
    {
        _byExec.TryGetValue(executionId.AsString(), out var s);
        return Task.FromResult(s);
    }

    public Task UpdateStateAsync(ExecutionId executionId, Action<CrewExecutionState> update, CancellationToken ct = default)
    {
        // Register the execution id lazily so KickoffAsyncNoWait updates land somewhere.
        if (!_byExec.TryGetValue(executionId.AsString(), out var s))
        {
            s = new CrewExecutionState(CrewId.Create(), executionId, CrewInput.Empty());
            _byExec[executionId.AsString()] = s;
        }
        update(s);
        return Task.CompletedTask;
    }

    public Task CompleteExecutionAsync(ExecutionId executionId, CancellationToken ct = default)
    {
        CompleteExecutionCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrewExecutionState>> GetActiveExecutionsAsync(CrewId crewId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CrewExecutionState>>([]);

    public Task CleanupExpiredExecutionsAsync(TimeSpan maxAge, CancellationToken ct = default) => Task.CompletedTask;
}
