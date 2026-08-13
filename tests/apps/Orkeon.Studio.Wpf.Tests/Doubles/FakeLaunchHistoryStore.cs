using Orkeon.Studio.Core.History;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="ILaunchHistoryStore"/> kept entirely in memory.</summary>
public sealed class FakeLaunchHistoryStore : ILaunchHistoryStore
{
    public LaunchHistory History { get; set; } = LaunchHistory.Empty;

    public List<LaunchHistoryEntry> Recorded { get; } = [];

    public Task<LaunchHistory> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(History);

    public Task SaveAsync(LaunchHistory history, CancellationToken cancellationToken = default)
    {
        History = history;
        return Task.CompletedTask;
    }

    public Task<LaunchHistory> RecordAsync(LaunchHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        Recorded.Add(entry);
        History = History.Add(entry);
        return Task.FromResult(History);
    }
}
