using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core.Watcher;

[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-WATCHER-BRIDGE: wraps System.IO.FileSystemWatcher to implement ICodebaseWatcher; operates on a root path already resolved by the caller")]
public sealed class FileSystemWatcherCodebaseWatcher : ICodebaseWatcher
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FileChangeKind> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _fsWatcher;
    private Timer? _debounceTimer;
    private WatcherOptions? _options;
    private string? _rootFull;
    private bool _disposed;

    public event EventHandler<CodebaseChangeNotification>? Changed;

    public Task StartAsync(WatcherOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_fsWatcher is not null) throw new InvalidOperationException("Watcher already started.");

            _options = options;
            _rootFull = Path.GetFullPath(options.RootPath);

            var fsw = new FileSystemWatcher(_rootFull)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            };
            fsw.Created += OnFsCreated;
            fsw.Changed += OnFsChanged;
            fsw.Deleted += OnFsDeleted;
            fsw.Renamed += OnFsRenamed;
            fsw.EnableRaisingEvents = true;

            _fsWatcher = fsw;
            _debounceTimer = new Timer(OnDebounceTick, state: null, Timeout.Infinite, Timeout.Infinite);
        }
        return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849",
        Justification = "Synchronous teardown executed inside a System.Threading.Lock scope where awaiting Timer.DisposeAsync() is not permitted; the method is intentionally synchronous and returns Task.CompletedTask.")]
    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_fsWatcher is null) return Task.CompletedTask;

            _fsWatcher.EnableRaisingEvents = false;
            _fsWatcher.Created -= OnFsCreated;
            _fsWatcher.Changed -= OnFsChanged;
            _fsWatcher.Deleted -= OnFsDeleted;
            _fsWatcher.Renamed -= OnFsRenamed;
            _fsWatcher.Dispose();
            _fsWatcher = null;

            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _pending.Clear();
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void OnFsCreated(object sender, FileSystemEventArgs e) => Record(e.FullPath, FileChangeKind.Created);
    private void OnFsChanged(object sender, FileSystemEventArgs e) => Record(e.FullPath, FileChangeKind.Modified);
    private void OnFsDeleted(object sender, FileSystemEventArgs e) => Record(e.FullPath, FileChangeKind.Deleted);

    private void OnFsRenamed(object sender, RenamedEventArgs e)
    {
        Record(e.OldFullPath, FileChangeKind.Deleted);
        Record(e.FullPath, FileChangeKind.Created);
    }

    private void Record(string fullPath, FileChangeKind kind)
    {
        if (string.IsNullOrEmpty(fullPath)) return;
        if (_options is null || _rootFull is null) return;
        if (IsExcluded(fullPath)) return;

        var relative = MakeRelative(fullPath);
        lock (_gate)
        {
            _pending[relative] = CoalesceKind(_pending.TryGetValue(relative, out var existing) ? existing : (FileChangeKind?)null, kind);
            _debounceTimer?.Change(_options.DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private static FileChangeKind CoalesceKind(FileChangeKind? existing, FileChangeKind incoming)
    {
        if (existing is null) return incoming;
        // Deleted is terminal unless a subsequent Created arrives, which implies a rename-like round-trip.
        if (existing == FileChangeKind.Deleted && incoming == FileChangeKind.Created) return FileChangeKind.Modified;
        if (existing == FileChangeKind.Created && incoming == FileChangeKind.Deleted) return FileChangeKind.Deleted;
        if (existing == FileChangeKind.Created) return FileChangeKind.Created;
        if (incoming == FileChangeKind.Deleted) return FileChangeKind.Deleted;
        return FileChangeKind.Modified;
    }

    private bool IsExcluded(string fullPath)
    {
        if (_options is null || _rootFull is null) return false;
        var patterns = _options.ExcludePatterns;
        if (patterns.IsDefaultOrEmpty) return false;

        var rel = MakeRelative(fullPath);
        var normalized = rel.Replace('\\', '/');
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrEmpty(pattern)) continue;
            if (normalized.Contains($"/{pattern}/", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith($"{pattern}/", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(normalized, pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private string MakeRelative(string fullPath)
    {
        if (_rootFull is null) return fullPath;
        var abs = Path.GetFullPath(fullPath);
        if (abs.StartsWith(_rootFull, StringComparison.OrdinalIgnoreCase))
        {
            var rel = abs.Substring(_rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel;
        }
        return abs;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fire-and-forget dispatch: a faulty Changed subscriber must not break the watcher's debounce timer.")]
    private void OnDebounceTick(object? state)
    {
        ImmutableArray<FileChange> batch;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            batch = [.. _pending.Select(kv => new FileChange(kv.Key, kv.Value))];
            _pending.Clear();
        }

        var handler = Changed;
        if (handler is null) return;
        try
        {
            handler.Invoke(this, new CodebaseChangeNotification { Changes = batch });
        }
        catch
        {
            // Subscriber faults must not break the watcher.
        }
    }
}
