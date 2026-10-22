using System.Collections.Immutable;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Stores;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Rag;

public sealed class SqliteDocumentStoreScopeTests : IDisposable
{
    private const string Collection = "docs";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "orkeon-rag-scope-" + Guid.NewGuid().ToString("N"));
    private readonly List<MemoryProviderFactory> _factories = [];

    public SqliteDocumentStoreScopeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        foreach (var factory in _factories)
            factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    [Trait("BR", "RT-21")]
    public async Task ShouldRetrieveChunkFromOtherWorkspace_WhenStoreIsSqliteAndHostGlobal()
    {
        var databaseFile = Path.Combine(_directory, "rag.db").Replace('\\', '/');
        var workspaceA = new FakeFileSystemService().AddMount(_directory.Replace('\\', '/'), FileAccessRights.ReadWrite).AddMount("/workspace-a", FileAccessRights.ReadWrite);
        var workspaceB = new FakeFileSystemService().AddMount(_directory.Replace('\\', '/'), FileAccessRights.ReadWrite).AddMount("/workspace-b", FileAccessRights.ReadWrite);
        var storeA = CreateStore(workspaceA, databaseFile);
        var storeB = CreateStore(workspaceB, databaseFile);

        var chunk = new Chunk
        {
            Id = "chunk-1",
            DocumentId = "doc-1",
            SourceId = "/workspace-a/notes.txt",
            Content = "ingested through workspace A",
            Index = 0,
            StartOffset = 0,
            EndOffset = 28,
        };
        await storeA.UpsertAsync(Collection, [new EmbeddedChunk { Chunk = chunk, Embedding = [1f, 0f, 0f, 0f] }], TestContext.Current.CancellationToken);

        var manifestA = new FileIngestionManifestStore(workspaceA, new Orkeon.Rag.Pipeline.RagIngestionOptions { ManifestDirectory = "/workspace-a/manifests" });
        var manifestB = new FileIngestionManifestStore(workspaceB, new Orkeon.Rag.Pipeline.RagIngestionOptions { ManifestDirectory = "/workspace-b/manifests" });
        await manifestA.SaveAsync(new IngestionManifest
        {
            Collection = Collection,
            Embedding = new ManifestEmbeddingProfile { Provider = "fake", Model = "fake", Dimensions = 4 },
        }, TestContext.Current.CancellationToken);

        var hits = await storeB.SearchAsync(
            Collection,
            new RetrievalQuery { Text = "notes", Embedding = ImmutableArray.Create(1f, 0f, 0f, 0f), TopK = 3 },
            TestContext.Current.CancellationToken);

        var hit = Assert.Single(hits);
        Assert.Equal("chunk-1", hit.Chunk.Id);
        Assert.Equal("ingested through workspace A", hit.Chunk.Content);
        Assert.Equal("/workspace-a/notes.txt", hit.Chunk.SourceId);
        Assert.NotNull(await manifestA.LoadAsync(Collection, TestContext.Current.CancellationToken));
        Assert.Null(await manifestB.LoadAsync(Collection, TestContext.Current.CancellationToken));
    }

    private MemoryProviderDocumentStore CreateStore(IFileSystemService fileSystem, string databaseFile)
    {
        var settings = new MemoryProviderSettings
        {
            Sqlite = new SqliteMemoryOptions { ConnectionString = $"Data Source={databaseFile}" },
        };
        var factory = new MemoryProviderFactory(fileSystem, new StubHttpClientFactory(), settings);
        _factories.Add(factory);
        return new MemoryProviderDocumentStore(factory.GetProvider("sqlite"));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
