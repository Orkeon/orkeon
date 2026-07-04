using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface ICodebaseWatcher : IAsyncDisposable
{
    Task StartAsync(WatcherOptions options, CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003", Justification = "The payload CodebaseChangeNotification is a domain notification value (immutable record), intentionally not modeled as an EventArgs subclass — consistent with the other domain events in this codebase.")]
    event EventHandler<CodebaseChangeNotification>? Changed;
}

public sealed record WatcherOptions
{
    public required string RootPath { get; init; }
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public ImmutableArray<string> ExcludePatterns { get; init; } =
        ["node_modules", "dist", ".git", "bin", "obj"];
}

public sealed record CodebaseChangeNotification
{
    public required ImmutableArray<FileChange> Changes { get; init; }
}

public sealed record FileChange(string RelativePath, FileChangeKind Kind);
