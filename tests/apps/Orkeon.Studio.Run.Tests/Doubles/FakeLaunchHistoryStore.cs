using Orkeon.Studio.Core.History;

namespace Orkeon.Studio.Run.Tests.Doubles;

/// <summary>
/// In-memory <see cref="ILaunchHistoryStore"/>: the recent-launch list lives in a field, so
/// replay and recording can be asserted without writing to the user's configuration directory.
/// </summary>
public sealed class FakeLaunchHistoryStore : ILaunchHistoryStore
{
    /// <summary>The stored history, readable and settable by the test.</summary>
    public LaunchHistory History { get; set; } = LaunchHistory.Empty;

    /// <summary>Entries passed to <see cref="RecordAsync"/>, in call order.</summary>
    public List<LaunchHistoryEntry> Recorded { get; } = new();

    /// <summary>Number of <see cref="SaveAsync"/> calls.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Declares a stored entry, newest last.</summary>
    public FakeLaunchHistoryStore WithEntry(LaunchHistoryEntry entry)
    {
        History = History.Add(entry);
        return this;
    }

    /// <inheritdoc />
    public Task<LaunchHistory> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(History);

    /// <inheritdoc />
    public Task SaveAsync(LaunchHistory history, CancellationToken cancellationToken = default)
    {
        History = history;
        SaveCount++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<LaunchHistory> RecordAsync(LaunchHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Recorded.Add(entry);
        History = History.Add(entry);
        return Task.FromResult(History);
    }
}
