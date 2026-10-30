using Microsoft.Data.Sqlite;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Cache;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeCacheTests
{
    private const string Key = "key-1";

    [Fact]
    public async Task SaveAsync_writes_the_database_and_a_gitignore_keeping_the_folder_out_of_git()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var saved = await BuildCache(root).SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx-1", Tree()), CancellationToken.None);

            Assert.True(saved);
            Assert.True(File.Exists(DatabasePath(root)));
            Assert.Equal("*\n", await File.ReadAllTextAsync(Path.Combine(CacheDir(root), ".gitignore"), TestContext.Current.CancellationToken));
            Assert.Equal(3L, Scalar(root, "PRAGMA user_version"));
            Assert.Equal(
                "cache_entries,indexed_files,node_groups,node_runs,package_markers",
                Scalar(root, "SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name)"));
            Assert.Equal("cache_entries,node_groups", Scalar(root,
                "SELECT group_concat(t, ',') FROM (SELECT DISTINCT \"table\" AS t FROM pragma_foreign_key_list('node_runs') ORDER BY t)"));
            Assert.Equal("CASCADE", Scalar(root, "SELECT on_delete FROM pragma_foreign_key_list('node_groups')"));
            Assert.Equal("CASCADE", Scalar(root, "SELECT on_delete FROM pragma_foreign_key_list('indexed_files')"));
            Assert.Equal("CASCADE", Scalar(root, "SELECT on_delete FROM pragma_foreign_key_list('package_markers')"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_roundtrips_the_saved_entry_in_its_node_order()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            // a, b, a: the nodes of one file are not contiguous, the runs must give this order back.
            var entry = Entry("idx-rt", Tree(("a1", "/src/a.ts"), ("b1", "/src/b.ts"), ("a2", "/src/a.ts")));
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, entry, CancellationToken.None);

            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("idx-rt", loaded.IndexId);
            Assert.Equal(Key, loaded.CacheKey);
            Assert.Equal(["a1", "b1", "a2"], loaded.Tree.Nodes.Select(n => n.Id));
            Assert.Equal(entry.Tree.Edges, loaded.Tree.Edges);
            Assert.Same(loaded.Tree.Nodes[1], loaded.Tree.Index["pkg::b1"]);
            Assert.Equal(entry.Files, loaded.Files);
            Assert.Equal(entry.Markers, loaded.Markers);
            Assert.Equal(2L, Scalar(root, "SELECT count(*) FROM node_groups"));
            Assert.Equal(3L, Scalar(root, "SELECT count(*) FROM node_runs"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_rewrites_only_the_groups_whose_nodes_changed()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx-1", Tree(("a1", "/src/a.ts"), ("b1", "/src/b.ts"), ("c1", "/src/c.ts"))), CancellationToken.None);

            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx-2", Tree(("a1", "/src/a.ts"), ("b2", "/src/b.ts"))), CancellationToken.None);

            Assert.Equal(2L, Scalar(root, "SELECT generation FROM cache_entries"));
            Assert.Equal("/src/a.ts=1,/src/b.ts=2", Scalar(root,
                "SELECT group_concat(virtual_path || '=' || generation, ',') FROM (SELECT * FROM node_groups ORDER BY virtual_path)"));
            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(["a1", "b2"], loaded.Tree.Nodes.Select(n => n.Id));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_under_another_key_keeps_both_entries()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx-1", Tree(("a1", "/src/a.ts"))), CancellationToken.None);

            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx-2", Tree(("b1", "/src/b.ts"))) with { CacheKey = "key-2" }, CancellationToken.None);

            var first = await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None);
            var second = await cache.LoadAsync(TestFixtures.TestVirtualRoot, "key-2", CancellationToken.None);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(["a1"], first.Tree.Nodes.Select(n => n.Id));
            Assert.Equal(["b1"], second.Tree.Nodes.Select(n => n.Id));
            Assert.Equal(2L, Scalar(root, "SELECT count(*) FROM cache_entries"));
            Assert.Equal(1L, Scalar(root, "SELECT max(generation) FROM cache_entries"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_past_MaxEntries_evicts_the_least_recently_used_entry_and_its_rows()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            for (var i = 1; i <= RaggableTreeCache.MaxEntries; i++)
            {
                await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                    Entry("idx", Tree(($"n{i}", $"/src/f{i}.ts"))) with { CacheKey = $"k{i}" }, CancellationToken.None);
            }
            // k2 becomes the least recently used, whatever order the clock gave the saves.
            Scalar(root, "UPDATE cache_entries SET used_at = CASE cache_key WHEN 'k2' THEN '2000-01-01' ELSE '2001-01-01' END");

            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx", Tree(("n4", "/src/f4.ts"))) with { CacheKey = "k4" }, CancellationToken.None);

            Assert.Equal((long)RaggableTreeCache.MaxEntries, Scalar(root, "SELECT count(*) FROM cache_entries"));
            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "k2", CancellationToken.None));
            Assert.NotNull(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "k1", CancellationToken.None));
            Assert.NotNull(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "k4", CancellationToken.None));
            // The cascade took the evicted entry's rows with it.
            Assert.Equal(0L, Scalar(root, "SELECT count(*) FROM node_groups WHERE virtual_path = '/src/f2.ts'"));
            Assert.Equal(0L, Scalar(root,
                "SELECT count(*) FROM node_runs WHERE entry_id NOT IN (SELECT entry_id FROM cache_entries)"));
            Assert.Equal((long)RaggableTreeCache.MaxEntries, Scalar(root, "SELECT count(DISTINCT entry_id) FROM indexed_files"));
            Assert.Equal((long)RaggableTreeCache.MaxEntries, Scalar(root, "SELECT count(DISTINCT entry_id) FROM package_markers"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_drops_the_entries_of_another_runtime_and_keeps_its_own()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx", Tree(("old", "/src/old.ts"))) with { CacheKey = "k-old", Runtime = "analysis:1.0.0" }, CancellationToken.None);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx", Tree(("n1", "/src/f1.ts"))) with { CacheKey = "k1", Runtime = "analysis:2.0.0" }, CancellationToken.None);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx", Tree(("n2", "/src/f2.ts"))) with { CacheKey = "k2", Runtime = "analysis:2.0.0" }, CancellationToken.None);

            // The entry no key of the current runtime can hit is gone with its rows, before MaxEntries is reached.
            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "k-old", CancellationToken.None));
            Assert.Equal(0L, Scalar(root, "SELECT count(*) FROM node_groups WHERE virtual_path = '/src/old.ts'"));
            Assert.Equal(2L, Scalar(root, "SELECT count(*) FROM cache_entries"));
            var kept = await cache.LoadAsync(TestFixtures.TestVirtualRoot, "k1", CancellationToken.None);
            Assert.NotNull(kept);
            Assert.Equal("analysis:2.0.0", kept.Runtime);
            Assert.NotNull(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "k2", CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_gives_the_pages_of_a_large_dropped_entry_back_to_the_disk()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            var large = Tree([.. Enumerable.Range(0, 2000).Select(i => ($"n{i}-{Guid.NewGuid():N}", $"/src/f{i}.ts"))]);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx", large) with { CacheKey = "k-old", Runtime = "analysis:1.0.0" }, CancellationToken.None);
            var pagesBefore = (long)Scalar(root, "SELECT page_count FROM pragma_page_count")!;

            await cache.SaveAsync(TestFixtures.TestVirtualRoot,
                Entry("idx", Tree(("n", "/src/a.ts"))) with { CacheKey = "k-new", Runtime = "analysis:2.0.0" }, CancellationToken.None);

            var pagesAfter = (long)Scalar(root, "SELECT page_count FROM pragma_page_count")!;
            Assert.True(pagesAfter * 2 < pagesBefore, $"{pagesAfter} pages after the drop, {pagesBefore} before");
            Assert.Equal(0L, Scalar(root, "SELECT freelist_count FROM pragma_freelist_count"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_marks_the_entry_as_used()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx", Tree()), CancellationToken.None);
            Scalar(root, "UPDATE cache_entries SET used_at = '2000-01-01'");

            await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None);

            Assert.Equal(0L, Scalar(root, "SELECT count(*) FROM cache_entries WHERE used_at = '2000-01-01'"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_null_when_nothing_was_saved()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            Assert.Null(await BuildCache(root).LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_null_under_another_key()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx", Tree()), CancellationToken.None);

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "key-2", CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_file_that_is_not_a_database_is_a_miss_then_replaced_by_the_next_save()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            Directory.CreateDirectory(CacheDir(root));
            await File.WriteAllTextAsync(DatabasePath(root), "not a database, only text", TestContext.Current.CancellationToken);

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
            Assert.True(await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx", Tree()), CancellationToken.None));
            Assert.NotNull(await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_null_when_a_run_names_more_nodes_than_its_group_holds()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx", Tree()), CancellationToken.None);
            Scalar(root, "UPDATE node_runs SET node_count = 5");

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SaveAsync_returns_false_when_the_cache_folder_cannot_be_created()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;"), (".orkeon", "a file, not a folder")]);
        try
        {
            Assert.False(await BuildCache(root).SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx", Tree()), CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InvalidateAsync_makes_the_next_load_a_miss()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx", Tree()), CancellationToken.None);

            await cache.InvalidateAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
            Assert.False(File.Exists(DatabasePath(root)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ----- Helpers -----

    private static RaggableTreeCache BuildCache(string dir) => new(TestFixtures.CreateFs(dir));

    private static string CacheDir(string root) => Path.Combine(root, ".orkeon", "cache", "raggable-tree");

    private static string DatabasePath(string root) => Path.Combine(CacheDir(root), RaggableTreeCache.DatabaseFileName);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100", Justification = "Every caller passes a constant query of this test.")]
    private static object? Scalar(string root, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath(root)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static RaggableTreeCacheEntry Entry(string indexId, RaggableTree tree) => new(
        tree,
        indexId,
        Key,
        new Dictionary<string, FileStamp>
        {
            ["a.ts"] = new("sha-a", 19, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            ["b.ts"] = new("sha-b", 7, null),
        },
        new Dictionary<string, string> { ["package.json"] = "sha-p" });

    private static RaggableTree Tree(params (string Id, string File)[] nodes)
    {
        if (nodes.Length == 0) nodes = [("x", "/src/x.ts")];
        var built = nodes.Select(n => new RaggableNode
        {
            Id = n.Id,
            Kind = UniversalNodeKind.Method,
            Name = n.Id,
            VirtualFilePath = n.File,
            Range = new NodeRange(0, 5, 1, 2, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "fn",
            Sha256 = "abc",
            Fqn = $"pkg::{n.Id}",
        }).ToList();
        var edges = built.Count > 1 ? [new RaggableEdge("e1", built[0].Id, built[1].Id, EdgeKind.Calls)] : new List<RaggableEdge>();
        return new RaggableTree(built, edges, built.ToDictionary(n => n.Fqn, StringComparer.Ordinal));
    }
}
