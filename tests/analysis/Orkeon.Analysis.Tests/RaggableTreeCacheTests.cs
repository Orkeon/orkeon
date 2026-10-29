using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Cache;
using Orkeon.Analysis.Core.Serialization;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeCacheTests
{
    private const string Key = "key-1";

    [Fact]
    public async Task SaveAsync_writes_tree_manifest_and_a_gitignore_keeping_the_folder_out_of_git()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var saved = await BuildCache(root).SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx-1"), CancellationToken.None);

            var dir = Path.Combine(root, ".orkeon", "cache", "raggable-tree");
            Assert.True(saved);
            Assert.True(File.Exists(Path.Combine(dir, "tree.json")));
            Assert.True(File.Exists(Path.Combine(dir, "manifest.json")));
            Assert.Equal("*\n", await File.ReadAllTextAsync(Path.Combine(dir, ".gitignore"), TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_roundtrips_the_saved_entry_under_its_key()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            var entry = Entry("idx-rt");
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, entry, CancellationToken.None);

            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("idx-rt", loaded.IndexId);
            Assert.Equal(Key, loaded.CacheKey);
            Assert.Equal(entry.Tree.Nodes.Select(n => n.Id), loaded.Tree.Nodes.Select(n => n.Id));
            Assert.Equal(entry.Files, loaded.Files);
            Assert.Equal(entry.Markers, loaded.Markers);
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
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx"), CancellationToken.None);

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, "key-2", CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_null_when_the_manifest_is_corrupted()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx"), CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(root, ".orkeon", "cache", "raggable-tree", "manifest.json"), "{ not json", TestContext.Current.CancellationToken);

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_null_when_the_tree_file_does_not_match_its_manifest()
    {
        var root = await TestFixtures.WriteDirectoryAsync([("a.ts", "export const a = 1;")]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx"), CancellationToken.None);
            var treePath = Path.Combine(root, ".orkeon", "cache", "raggable-tree", "tree.json");
            var text = await File.ReadAllTextAsync(treePath, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(treePath, text.Replace("sym::x", "sym::y", StringComparison.Ordinal), TestContext.Current.CancellationToken);

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
            Assert.False(await BuildCache(root).SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx"), CancellationToken.None));
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
            await cache.SaveAsync(TestFixtures.TestVirtualRoot, Entry("idx"), CancellationToken.None);

            await cache.InvalidateAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.Null(await cache.LoadAsync(TestFixtures.TestVirtualRoot, Key, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(root, ".orkeon", "cache", "raggable-tree", "tree.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ----- Helpers -----

    private static RaggableTreeCache BuildCache(string dir) =>
        new(new RaggableTreeSerializer(), TestFixtures.CreateFs(dir));

    private static RaggableTreeCacheEntry Entry(string indexId) => new(
        BuildTinyTree(),
        indexId,
        Key,
        new Dictionary<string, string> { ["a.ts"] = "sha-a" },
        new Dictionary<string, string> { ["package.json"] = "sha-p" });

    private static RaggableTree BuildTinyTree()
    {
        var node = new RaggableNode
        {
            Id = "sym::x",
            Kind = UniversalNodeKind.Method,
            Name = "x",
            VirtualFilePath = "/tmp/x.ts",
            Range = new NodeRange(0, 5, 1, 2, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "fn",
            Sha256 = "abc",
            Fqn = "pkg::x",
        };
        return new RaggableTree([node], [], new Dictionary<string, RaggableNode> { [node.Fqn] = node });
    }
}
