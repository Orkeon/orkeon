using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Pipeline;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Ingestion;

/// <summary>
/// <see cref="FileIngestionManifestStore"/>: VFS-backed JSON persistence,
/// resilient loading (absent/corrupt/unmounted → <c>null</c>, never a crash),
/// and collision-free file naming.
/// </summary>
public class FileIngestionManifestStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeFileSystemService NewFs() => new FakeFileSystemService().AddMount("/output");

    private static IngestionManifest NewManifest(string collection = "kb") => new()
    {
        Collection = collection,
        UpdatedAt = DateTimeOffset.UtcNow,
        Embedding = new ManifestEmbeddingProfile
        {
            Provider = "fake",
            Model = "fake-model",
            Dimensions = 4,
        },
        Sources = new Dictionary<string, ManifestSourceEntry>(StringComparer.Ordinal)
        {
            ["/kb/a.txt"] = new ManifestSourceEntry
            {
                ContentHash = new string('a', 64),
                IngestedAt = DateTimeOffset.UtcNow,
                Chunker = new ManifestChunkerProfile
                {
                    Name = "recursive",
                    Version = "1",
                    MaxChunkSize = 1000,
                    Overlap = 200,
                    Extensions = new Dictionary<string, string> { ["separator"] = "\n\n" },
                },
            },
        },
    };

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var store = new FileIngestionManifestStore(NewFs());

        await store.SaveAsync(NewManifest(), Ct);
        var loaded = await store.LoadAsync("kb", Ct);

        Assert.NotNull(loaded);
        Assert.Equal("kb", loaded.Collection);
        Assert.Equal("fake", loaded.Embedding.Provider);
        Assert.Equal("fake-model", loaded.Embedding.Model);
        Assert.Equal(4, loaded.Embedding.Dimensions);
        var entry = Assert.Single(loaded.Sources);
        Assert.Equal("/kb/a.txt", entry.Key);
        Assert.Equal(new string('a', 64), entry.Value.ContentHash);
        Assert.Equal("recursive", entry.Value.Chunker.Name);
        Assert.Equal("1", entry.Value.Chunker.Version);
        Assert.Equal(1000, entry.Value.Chunker.MaxChunkSize);
        Assert.Equal(200, entry.Value.Chunker.Overlap);
        Assert.Equal("\n\n", entry.Value.Chunker.Extensions["separator"]);
    }

    [Fact]
    public async Task Load_AbsentManifest_ReturnsNull()
    {
        var store = new FileIngestionManifestStore(NewFs());

        Assert.Null(await store.LoadAsync("nothing-here", Ct));
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsNull()
    {
        var fs = NewFs();
        var store = new FileIngestionManifestStore(fs);
        fs.AddFile(store.GetManifestPath("kb"), "not json at all {{{");

        Assert.Null(await store.LoadAsync("kb", Ct));
    }

    [Fact]
    public async Task Load_ValidJsonMissingEmbeddingProfile_TreatedAsCorrupt()
    {
        var fs = NewFs();
        var store = new FileIngestionManifestStore(fs);
        fs.AddFile(store.GetManifestPath("kb"), """{ "collection": "kb", "sources": {} }""");

        Assert.Null(await store.LoadAsync("kb", Ct));
    }

    [Fact]
    public async Task Load_ManifestDirectoryOutsideAnyMount_DegradesToNull()
    {
        var fs = new FakeFileSystemService().AddMount("/kb"); // no /output mount
        var store = new FileIngestionManifestStore(fs);

        Assert.Null(await store.LoadAsync("kb", Ct));
    }

    [Fact]
    public async Task Save_ManifestDirectoryOutsideAnyMount_DoesNotThrow()
    {
        var fs = new FakeFileSystemService().AddMount("/kb"); // no /output mount
        var store = new FileIngestionManifestStore(fs);

        await store.SaveAsync(NewManifest(), Ct); // degraded, surfaced by logs only
    }

    [Fact]
    public async Task Save_HonorsConfiguredManifestDirectory()
    {
        var fs = new FakeFileSystemService().AddMount("/custom");
        var options = new RagIngestionOptions { ManifestDirectory = "/custom/manifests/" };
        var store = new FileIngestionManifestStore(fs, options);

        await store.SaveAsync(NewManifest(), Ct);

        Assert.Equal("/custom/manifests/kb.json", store.GetManifestPath("kb"));
        Assert.True(await fs.ExistsAsync("/custom/manifests/kb.json", Ct));
    }

    [Fact]
    public void GetManifestPath_UnsafeCollectionNames_AreSanitizedAndDisambiguated()
    {
        var store = new FileIngestionManifestStore(NewFs());

        var slashed = store.GetManifestPath("a/b");
        var underscored = store.GetManifestPath("a_b");

        Assert.StartsWith("/output/rag/manifests/a_b-", slashed, StringComparison.Ordinal);
        Assert.EndsWith(".json", slashed, StringComparison.Ordinal);
        Assert.Equal("/output/rag/manifests/a_b.json", underscored);
        Assert.NotEqual(slashed, underscored); // 'a/b' and 'a_b' never collide
    }

    [Fact]
    public async Task Save_Overwrites_PreviousVersion()
    {
        var store = new FileIngestionManifestStore(NewFs());
        await store.SaveAsync(NewManifest(), Ct);

        var updated = NewManifest() with
        {
            Embedding = new ManifestEmbeddingProfile { Provider = "p2", Model = "m2", Dimensions = 8 },
            Sources = [],
        };
        await store.SaveAsync(updated, Ct);

        var loaded = await store.LoadAsync("kb", Ct);
        Assert.NotNull(loaded);
        Assert.Equal("m2", loaded.Embedding.Model);
        Assert.Empty(loaded.Sources);
    }
}
