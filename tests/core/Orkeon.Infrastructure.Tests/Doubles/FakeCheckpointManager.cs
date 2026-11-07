using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ICheckpointManager"/> whose checkpoint write fails, to see what a run does
/// when its state cannot be saved. Only the checkpoint methods are meaningful.
/// </summary>
public sealed class FakeCheckpointManager : ICheckpointManager
{
    /// <summary>The failure the checkpoint write throws.</summary>
    public Exception? CheckpointFailure { get; set; }

    /// <summary>The task keys whose failure was marked.</summary>
    public List<string> MarkedFailed { get; } = [];

    public System.Threading.Tasks.Task<string> StartSessionAsync(ExecutionId sessionId, string crewKey, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.FromResult(sessionId.ToString());

    public System.Threading.Tasks.Task CheckpointAsync(string sessionId, string taskId, string? output, CancellationToken ct = default) =>
        CheckpointFailure is null ? System.Threading.Tasks.Task.CompletedTask : System.Threading.Tasks.Task.FromException(CheckpointFailure);

    public System.Threading.Tasks.Task MarkFailedAsync(string sessionId, string taskId, Exception ex, CancellationToken ct = default)
    {
        MarkedFailed.Add(taskId);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task CompleteSessionAsync(string sessionId, CancellationToken ct = default) =>
        System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task<SessionState?> GetLatestCheckpointAsync(string crewKey, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<SessionState?> RestoreToVersionAsync(string sessionId, string versionId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<VersionedState> ForkFromAsync(string sessionId, string versionId, string? label = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
