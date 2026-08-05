using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// Dirty marking + lazy freshness (PLAN phase B): the write-side contract on the store,
/// and the single-flight / degradation behaviour of the service. The full
/// write→search round-trip runs live in exp07's probe crew (B-14 discipline) — here the
/// pieces are pinned with hand-written doubles per the repo rule.
/// </summary>
public class IndexInvalidationTests
{
    private static RaggableNode Root(string virtualRoot) => new()
    {
        Id = "root",
        Kind = UniversalNodeKind.Module,
        Name = "root",
        Fqn = virtualRoot,
        VirtualFilePath = virtualRoot,
        Range = new NodeRange(0, 0, 0, 0, 0),
        Level = NodeLevel.L0_Monorepo,
        Language = "typescript",
        SourceSnippet = "",
        Sha256 = "root",
    };

    private static InMemoryRaggableStore StoreWithRoot(string root = "/workspace")
        => new([Root(root)], [], new FakeFileSystemService());

    [Fact]
    public void MarkDirty_records_paths_under_an_indexed_root()
    {
        var store = StoreWithRoot();
        store.MarkDirty("/workspace/src/a.ts");
        Assert.Contains("/workspace/src/a.ts", store.DirtyPaths);
    }

    [Fact]
    public void MarkDirty_outside_every_indexed_root_is_a_noop()
    {
        // Nothing stale to report about a file the index never covered — a scratch
        // write in /output must not schedule a reindex.
        var store = StoreWithRoot();
        store.MarkDirty("/output/report.md");
        Assert.Empty(store.DirtyPaths);
    }

    [Fact]
    public void MarkDirty_with_no_index_at_all_is_a_noop()
    {
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        store.MarkDirty("/workspace/src/a.ts");
        Assert.Empty(store.DirtyPaths);
    }

    [Fact]
    public void ClearDirty_removes_exactly_the_given_paths()
    {
        var store = StoreWithRoot();
        store.MarkDirty("/workspace/a.ts");
        store.MarkDirty("/workspace/b.ts");
        store.ClearDirty(["/workspace/a.ts"]);
        Assert.Equal(["/workspace/b.ts"], store.DirtyPaths);
    }

    [Fact]
    public void Replace_keeps_the_dirty_set()
    {
        // A write can race a reindex pass: clearing everything on Replace would drop a
        // path that changed AFTER the pass parsed it. Only the covered snapshot is
        // cleared, by the service.
        var store = StoreWithRoot();
        store.MarkDirty("/workspace/a.ts");
        store.Replace([Root("/workspace")], []);
        Assert.Contains("/workspace/a.ts", store.DirtyPaths);
    }

    [Fact]
    public void MarkDirty_is_idempotent()
    {
        var store = StoreWithRoot();
        store.MarkDirty("/workspace/a.ts");
        store.MarkDirty("/workspace/a.ts");
        Assert.Single(store.DirtyPaths);
    }
}

public class IndexFreshnessServiceTests
{
    private sealed class RecordingGitDiff : IGitDiffProvider
    {
        public int WorkingTreeCalls;
        public IReadOnlyList<string> WorkingTreeChanges { get; set; } = [];

        public Task<IReadOnlyList<string>> GetChangedFilesAsync(string rootPath, string fromCommit, string toCommit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetWorkingTreeChangesAsync(string rootPath, CancellationToken ct)
        {
            Interlocked.Increment(ref WorkingTreeCalls);
            return Task.FromResult(WorkingTreeChanges);
        }
    }

    private static RaggableNode Root(string virtualRoot) => new()
    {
        Id = "root",
        Kind = UniversalNodeKind.Module,
        Name = "root",
        Fqn = virtualRoot,
        VirtualFilePath = virtualRoot,
        Range = new NodeRange(0, 0, 0, 0, 0),
        Level = NodeLevel.L0_Monorepo,
        Language = "typescript",
        SourceSnippet = "",
        Sha256 = "root",
    };

    [Fact]
    public async Task No_index_means_no_work_and_no_git_probe()
    {
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        var git = new RecordingGitDiff();
        using var service = new IndexFreshnessService(
            store, () => throw new InvalidOperationException("engine must not be built"),
            new FakeFileSystemService(), git);

        Assert.Equal(0, await service.EnsureFreshAsync(CancellationToken.None));
        Assert.Equal(0, git.WorkingTreeCalls);
    }

    [Fact]
    public async Task Clean_probe_is_debounced_inside_the_window()
    {
        // Without the debounce every search in a burst pays a `git status` subprocess.
        // A clean probe is trusted for the window; a hooked write bypasses it (below).
        var store = new InMemoryRaggableStore([Root("/workspace")], [], new FakeFileSystemService());
        var git = new RecordingGitDiff();
        using var service = new IndexFreshnessService(
            store, () => throw new InvalidOperationException("nothing changed — engine must not run"),
            new FakeFileSystemService(), git);

        await service.EnsureFreshAsync(CancellationToken.None);
        await service.EnsureFreshAsync(CancellationToken.None);
        await service.EnsureFreshAsync(CancellationToken.None);

        Assert.Equal(1, git.WorkingTreeCalls);
    }

    [Fact]
    public async Task Engine_failure_degrades_to_the_current_index_and_keeps_the_debt()
    {
        // Stale results beat no results: a parser/git hiccup must not take the search
        // down, and the dirty path must SURVIVE so the next search retries and
        // index_status keeps reporting it.
        var store = new InMemoryRaggableStore([Root("/workspace")], [], new FakeFileSystemService());
        store.MarkDirty("/workspace/src/a.ts");
        using var service = new IndexFreshnessService(
            store, () => throw new InvalidOperationException("engine exploded"),
            new FakeFileSystemService());

        var refreshed = await service.EnsureFreshAsync(CancellationToken.None);

        Assert.Equal(0, refreshed);
        Assert.Contains("/workspace/src/a.ts", store.DirtyPaths);
    }

    [Fact]
    public async Task Search_still_answers_when_freshness_degrades()
    {
        // End-to-end shape of the failure barrier through the store: dirty debt +
        // failing engine, and SemanticSearchAsync still serves the (stale) index.
        var store = new InMemoryRaggableStore([Root("/workspace")], [], new FakeFileSystemService());
        store.MarkDirty("/workspace/src/a.ts");
        using var service = new IndexFreshnessService(
            store, () => throw new InvalidOperationException("engine exploded"),
            new FakeFileSystemService());

        await service.EnsureFreshAsync(CancellationToken.None);
        var hits = await store.SemanticSearchAsync(
            new SemanticQuery { Text = "root", TopK = 3 }, CancellationToken.None);

        Assert.NotEmpty(hits); // the L0 node matches lexically — the index still serves
    }
}
