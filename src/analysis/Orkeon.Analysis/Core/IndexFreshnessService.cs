using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// The lazy half of the freshness design (PLAN B3): before a read tool answers, this
/// service reindexes exactly the files that changed since the index was built — the
/// dirty set fed by the <c>file_write</c> hook, UNION the git working-tree changes
/// (which catch edits made through <c>shell_command</c>, invisible to the hook).
/// </summary>
/// <remarks>
/// <para><b>Cost model</b> (the user's chosen policy): marking is O(1) at write time;
/// the reindex is paid at the NEXT search, once, grouped — a turn that edits ten files
/// costs one incremental pass, and a session that never searches again costs nothing.</para>
/// <para><b>Single-flight</b>: concurrent searches during an invalidation refresh once —
/// the second caller waits on the semaphore, re-checks, finds the set empty and reads
/// fresh. Combined with the store's RW lock (safe enumeration during mutation) this is
/// the whole edit↔search coordination story for the one-process reality: "another
/// agent" is another ticket of the same host, hitting the same store singleton.</para>
/// <para><b>Failure barrier</b>: a freshness failure degrades to serving the CURRENT
/// index — stale results beat no results — and leaves the paths dirty, so the next
/// search retries and <c>index_status</c> keeps reporting the debt.</para>
/// </remarks>
public sealed partial class IndexFreshnessService : IDisposable
{
    /// <summary>Above this many changed files the pass still runs, but says so — a
    /// silent multi-second reindex inside a search reads as a hung tool.</summary>
    public const int LargePassThreshold = 40;

    private readonly InMemoryRaggableStore _store;
    private readonly Func<IncrementalReindexEngine> _engineFactory;
    private readonly IFileSystemService _fileSystem;
    private readonly IGitDiffProvider? _gitDiff;
    private readonly IRaggableTreeEventBus? _eventBus;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly TimeProvider _clock;

    // Debounce for the git probe: without it every search in a burst pays a `git
    // status` subprocess even when nothing changed. A clean probe is trusted for this
    // window; the dirty set (the write hook) bypasses it — a hooked write is certain.
    private static readonly TimeSpan CleanProbeWindow = TimeSpan.FromSeconds(2);
    private DateTimeOffset _lastCleanProbeAt = DateTimeOffset.MinValue;

    public IndexFreshnessService(
        InMemoryRaggableStore store,
        Func<IncrementalReindexEngine> engineFactory,
        IFileSystemService fileSystem,
        IGitDiffProvider? gitDiff = null,
        IRaggableTreeEventBus? eventBus = null,
        ILogger<IndexFreshnessService>? logger = null,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _gitDiff = gitDiff;
        _eventBus = eventBus;
        _logger = logger ?? NullLogger<IndexFreshnessService>.Instance;
    }

    /// <summary>
    /// Brings the index up to date with the workspace, bounded to the changed files.
    /// Returns how many files were reindexed (0 = the index was already fresh, or there
    /// is no index yet — searches on an unindexed workspace are not this service's job).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Freshness fault barrier: a failed refresh serves the current (stale) index and leaves the dirty set intact for the next attempt — a search must never die because git or a parser hiccupped.")]
    public async Task<int> EnsureFreshAsync(CancellationToken ct)
    {
        var roots = _store.GetIndexedRoots();
        if (roots.Count == 0) return 0;

        // Cheap pre-check outside the flight: most searches find nothing to do and
        // must not serialize on the semaphore for it. A hooked write (dirty set) always
        // gets through; the git probe alone is debounced by the clean window.
        if (_store.DirtyPaths.Count == 0
            && (_gitDiff is null || _clock.GetUtcNow() - _lastCleanProbeAt < CleanProbeWindow))
        {
            return 0;
        }

        await _singleFlight.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var root = roots.OrderByDescending(r => r.VirtualRoot.Length).First().VirtualRoot;
            var changed = await CollectChangedAsync(root, ct).ConfigureAwait(false);
            if (changed.Count == 0)
            {
                _lastCleanProbeAt = _clock.GetUtcNow();
                return 0;
            }

            if (changed.Count > LargePassThreshold)
            {
                LogLargePass(changed.Count, root);
            }

            var (nodes, edges) = _store.ExportSnapshot();
            var index = nodes
                .Where(n => !string.IsNullOrEmpty(n.Fqn))
                .GroupBy(n => n.Fqn, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var currentTree = new RaggableTree(nodes, edges, index);

            var result = await _engineFactory().ReindexAsync(
                currentTree,
                root,
                [.. changed],
                new IndexCodebaseRequest { RootPath = root },
                ct).ConfigureAwait(false);

            // The store keeps no index-id; a content stamp of the node count + first
            // FQNs is informative enough for subscribers to correlate before/after.
            var previousId = $"nodes:{nodes.Count}";
            _store.Replace(result.Tree.Nodes, result.Tree.Edges);
            _store.ClearDirty(changed);

            // The bus existed with no producer; the freshness pass is its first. A
            // subscriber (context provider, another agent's cache) can now react to
            // exactly what moved instead of re-reading everything.
            _eventBus?.Publish(new RaggableTreeUpdated(
                previousId,
                result.IndexId,
                AddedFqns: [.. result.Tree.Nodes.Where(n => changed.Contains(n.VirtualFilePath)).Select(n => n.Fqn).Where(f => !string.IsNullOrEmpty(f))],
                RemovedFqns: [],
                ModifiedFqns: []));

            LogRefreshed(result.ChangedFileCount, result.ReusedFileCount);
            return result.ChangedFileCount;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRefreshFailed(ex);
            return 0;
        }
        finally
        {
            _singleFlight.Release();
        }
    }

    /// <summary>Dirty set ∪ git working-tree changes, normalized to virtual paths under the root.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Git probe fault barrier: no repo / no git degrades to the dirty set alone, never fails the search.")]
    private async Task<HashSet<string>> CollectChangedAsync(string virtualRoot, CancellationToken ct)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dirty in _store.DirtyPaths)
        {
            changed.Add(dirty);
        }

        if (_gitDiff is not null)
        {
            try
            {
                // git runs on the PHYSICAL directory behind the virtual root; results
                // come back as physical paths and are mapped into virtual space. A root
                // that does not resolve (or is not a repo) contributes nothing.
                var resolution = _fileSystem.ResolveAndValidate(virtualRoot, FileAccessRights.Read);
                if (resolution.IsAllowed && resolution.ResolvedPath is { } physicalRoot)
                {
                    var files = await _gitDiff.GetWorkingTreeChangesAsync(physicalRoot, ct).ConfigureAwait(false);
                    foreach (var physical in files)
                    {
                        var virtualPath = _fileSystem.ToVirtualPath(physical);
                        if (virtualPath is not null) changed.Add(virtualPath);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogGitProbeFailed(ex);
            }
        }

        // Only paths the index covers: a scratch file outside the root is not our debt.
        changed.RemoveWhere(p =>
            !p.Equals(virtualRoot, StringComparison.Ordinal)
            && !p.StartsWith(virtualRoot.TrimEnd('/') + "/", StringComparison.Ordinal));
        return changed;
    }

    /// <summary>Releases the single-flight semaphore.</summary>
    public void Dispose() => _singleFlight.Dispose();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Freshness pass covers {Count} changed files under {Root} — the next search pays a large incremental reindex.")]
    private partial void LogLargePass(int count, string root);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Index refreshed: {Changed} file(s) reparsed, {Reused} reused.")]
    private partial void LogRefreshed(int changed, int reused);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Index freshness pass failed — serving the current index; dirty paths kept for the next attempt.")]
    private partial void LogRefreshFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Git working-tree probe failed — freshness degrades to the write-hook dirty set alone.")]
    private partial void LogGitProbeFailed(Exception ex);
}
