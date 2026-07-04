using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.Cache;
using Orkeon.Analysis.Core.Serialization;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeCacheTests
{
    [Fact]
    public async Task SaveAsync_writes_tree_and_manifest_files()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
            ("b.ts", "export const b = 2;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            var tree = BuildTinyTree();

            await cache.SaveAsync(tree, "idx-1", TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(root, ".orkeon", "raggable-tree.json")));
            Assert.True(File.Exists(Path.Combine(root, ".orkeon", "raggable-tree-manifest.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_roundtrips_saved_tree()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            var tree = BuildTinyTree();
            await cache.SaveAsync(tree, "idx-rt", TestFixtures.TestVirtualRoot, CancellationToken.None);

            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.True(loaded.Hit);
            Assert.Equal("idx-rt", loaded.IndexId);
            Assert.Equal(tree.Nodes.Count, loaded.Tree!.Nodes.Count);
            Assert.Equal(tree.Edges.Count, loaded.Tree!.Edges.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_miss_when_no_cache_exists()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.False(loaded.Hit);
            Assert.Null(loaded.Tree);
            Assert.Null(loaded.IndexId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_a_file_is_modified()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
            ("b.ts", "export const b = 2;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.True(await cache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));

            await File.WriteAllTextAsync(Path.Combine(root, "a.ts"), "export const a = 999;", TestContext.Current.CancellationToken);

            Assert.False(await cache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_a_file_is_added()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(root, "b.ts"), "export const b = 2;", TestContext.Current.CancellationToken);

            Assert.False(await cache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_a_file_is_removed()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
            ("b.ts", "export const b = 2;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            File.Delete(Path.Combine(root, "b.ts"));

            Assert.False(await cache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_embedding_profile_changes()
    {
        // Acceptance criterion for LE-10: a cache produced with one embedding provider
        // (e.g. OpenAI 1536-dim) MUST be considered stale when reloaded with a different
        // provider (e.g. local 384-dim). Otherwise vectors of mixed dimensions would
        // coexist in the in-memory store and corrupt cosine similarity search.
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var openAiProfile = new EmbeddingCacheProfile
            {
                Provider = "OpenAI",
                Model = "text-embedding-3-small",
                Dimensions = 1536,
            };
            var localProfile = new EmbeddingCacheProfile
            {
                Provider = "LocalSmartComponents",
                Model = "bge-micro-v2",
                Dimensions = 384,
            };

            var saveCache = BuildCache(root, embeddingProfile: openAiProfile);
            await saveCache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.True(await saveCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));

            // Reload with a different embedding profile → must miss cleanly (no exception).
            var loadCache = BuildCache(root, embeddingProfile: localProfile);
            Assert.False(await loadCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));

            var loadResult = await loadCache.LoadAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.False(loadResult.Hit);
            Assert.Null(loadResult.Tree);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_dimensions_change_only()
    {
        // Same provider + model but a different dimension override (e.g. OpenAI's
        // dimensions request param shrinking 1536 → 512) must also invalidate.
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var fullDim = new EmbeddingCacheProfile
            {
                Provider = "OpenAI",
                Model = "text-embedding-3-small",
                Dimensions = 1536,
            };
            var truncated = fullDim with { Dimensions = 512 };

            var saveCache = BuildCache(root, embeddingProfile: fullDim);
            await saveCache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            var loadCache = BuildCache(root, embeddingProfile: truncated);
            Assert.False(await loadCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_true_when_embedding_profile_matches()
    {
        // Identity check: same triplet on both sides → cache is valid.
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var profile = new EmbeddingCacheProfile
            {
                Provider = "OpenAI",
                Model = "text-embedding-3-small",
                Dimensions = 1536,
            };

            var saveCache = BuildCache(root, embeddingProfile: profile);
            await saveCache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            var loadCache = BuildCache(root, embeddingProfile: profile);
            Assert.True(await loadCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_active_profile_set_but_cache_is_legacy()
    {
        // Backward-compat: a cache created without a profile (legacy, pre-LE-10) must
        // be treated as stale once a caller starts passing an explicit profile, to
        // force a clean re-index that records the new profile in the manifest.
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            // Save without profile (legacy path).
            var saveCache = BuildCache(root, embeddingProfile: null);
            await saveCache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            // Load with an explicit profile → cache is stale.
            var profile = new EmbeddingCacheProfile
            {
                Provider = "OpenAI",
                Model = "text-embedding-3-small",
                Dimensions = 1536,
            };
            var loadCache = BuildCache(root, embeddingProfile: profile);
            Assert.False(await loadCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_adapter_versions_mismatch()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var saveCache = BuildCache(root, new() { ["typescript"] = "1.0" });
            await saveCache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            var loadCache = BuildCache(root, new() { ["typescript"] = "2.0" });
            Assert.False(await loadCache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InvalidateAsync_removes_both_cache_files()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.True(File.Exists(Path.Combine(root, ".orkeon", "raggable-tree.json")));

            await cache.InvalidateAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(root, ".orkeon", "raggable-tree.json")));
            Assert.False(File.Exists(Path.Combine(root, ".orkeon", "raggable-tree-manifest.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_miss_when_file_changed_after_save()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(root, "a.ts"), "export const a = 999;", TestContext.Current.CancellationToken);

            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.False(loaded.Hit);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task LoadAsync_returns_miss_when_manifest_is_corrupted()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            await File.WriteAllTextAsync(
                Path.Combine(root, ".orkeon", "raggable-tree-manifest.json"),
                "not json at all {{{", TestContext.Current.CancellationToken);

            var loaded = await cache.LoadAsync(TestFixtures.TestVirtualRoot, CancellationToken.None);
            Assert.False(loaded.Hit);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Manifest_contains_indexId_and_file_hashes()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
            ("sub/b.ts", "export const b = 2;"),
        ]);
        try
        {
            var cache = BuildCache(root, new() { ["typescript"] = "1.0" });
            await cache.SaveAsync(BuildTinyTree(), "idx-meta", TestFixtures.TestVirtualRoot, CancellationToken.None);

            var json = await File.ReadAllTextAsync(Path.Combine(root, ".orkeon", "raggable-tree-manifest.json"), TestContext.Current.CancellationToken);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            Assert.Equal("idx-meta", r.GetProperty("indexId").GetString());
            var files = r.GetProperty("files");
            Assert.True(files.TryGetProperty("a.ts", out _));
            Assert.True(files.TryGetProperty("sub/b.ts", out _));
            var versions = r.GetProperty("adapterVersions");
            Assert.Equal("1.0", versions.GetProperty("typescript").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task IsValidAsync_returns_false_when_tree_file_missing_but_manifest_present()
    {
        var root = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export const a = 1;"),
        ]);
        try
        {
            var cache = BuildCache(root);
            await cache.SaveAsync(BuildTinyTree(), "idx", TestFixtures.TestVirtualRoot, CancellationToken.None);

            File.Delete(Path.Combine(root, ".orkeon", "raggable-tree.json"));
            Assert.False(await cache.IsValidAsync(TestFixtures.TestVirtualRoot, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ----- Helpers -----

    private static RaggableTreeCache BuildCache(
        string dir,
        Dictionary<string, string>? adapterVersions = null,
        EmbeddingCacheProfile? embeddingProfile = null)
    {
        var fs = TestFixtures.CreateFs(dir);
        return new RaggableTreeCache(
            new RaggableTreeSerializer(),
            new FileSystemDiscoverer(fs),
            fs,
            adapterVersions ?? new Dictionary<string, string> { ["typescript"] = "1.0" },
            embeddingProfile);
    }

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
