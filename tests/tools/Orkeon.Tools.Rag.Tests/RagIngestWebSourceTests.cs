using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Rag.Tests.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// GAP-19, decision 5 — an http(s) address is not a glob. The <c>?</c> of a query string, or a
/// <c>*</c> in an address, made <c>SourceGlobExpander</c> take the address for a pattern:
/// <c>rag_ingest</c> tried to expand it through the virtual file system and failed on the
/// unmounted root, while the web loader takes such sources. The address now reaches the loaders
/// exactly as written, and the file system is never asked about it.
/// </summary>
public sealed class RagIngestWebSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The real ingestion pipeline, with the web loader replaced by a recording double.</summary>
    private static DefaultIngestionPipeline Pipeline(RecordingWebLoader web)
    {
        var options = new RagIngestionOptions();
        var manifests = new FakeFileSystemService().AddMount(
            "/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create);

        return new DefaultIngestionPipeline(
            new IngestionPipelineDependencies
            {
                LoaderFactory = new DocumentLoaderFactory([web]),
                ChunkingFactory = ChunkingStrategyFactoryDefaults.CreateDefault(),
                EmbeddingProvider = new StubEmbeddingProvider(),
                Store = new FakeDocumentStore(),
                Validation = new DataValidationPipeline(
                    [new PromptInjectionDocumentValidator(), new ContentIntegrityValidator()],
                    new InMemoryQuarantineStore(),
                    new ProvenanceTracker()),
                ManifestStore = new FileIngestionManifestStore(manifests, options),
            },
            options);
    }

    private static ToolCallRequest Ingest(params string[] sources) => new("rag_ingest", new Dictionary<string, object?>
    {
        ["collection"] = "web",
        ["sources"] = sources.Cast<object?>().ToList(),
    });

    [Fact]
    public async Task An_address_with_a_query_string_reaches_the_web_loader_without_the_file_system()
    {
        var web = new RecordingWebLoader();
        var fileSystem = new ThrowingFileSystemService();
        var tool = new RagIngestTool(Pipeline(web), fileSystem);

        var response = await tool.CallAsync(Ingest("https://exemple.test/page?id=1"), Ct);

        Assert.True(response.Success, response.Error);
        Assert.Equal(["https://exemple.test/page?id=1"], web.Loaded);
        Assert.Equal(0, fileSystem.CallCount);
    }

    [Fact]
    public async Task An_address_with_a_star_is_an_address_too()
    {
        var web = new RecordingWebLoader();
        var fileSystem = new ThrowingFileSystemService();
        var tool = new RagIngestTool(Pipeline(web), fileSystem);

        var response = await tool.CallAsync(
            Ingest("http://exemple.test/search?q=*", "HTTPS://exemple.test/a*b/page"), Ct);

        Assert.True(response.Success, response.Error);
        Assert.Equal(["http://exemple.test/search?q=*", "HTTPS://exemple.test/a*b/page"], web.Loaded);
        Assert.Equal(0, fileSystem.CallCount);
    }

    [Fact]
    public async Task A_glob_beside_an_address_is_still_expanded_through_the_file_system()
    {
        var pipeline = new FakeIngestionPipeline();
        var fileSystem = new FakeFileSystemService()
            .AddMount("/kb")
            .AddFile("/kb/a.md", "a")
            .AddFile("/kb/b.md", "b");
        var tool = new RagIngestTool(pipeline, fileSystem);

        var response = await tool.CallAsync(Ingest("https://exemple.test/page?id=1", "/kb/*.md"), Ct);

        Assert.True(response.Success, response.Error);
        Assert.Equal(
            ["https://exemple.test/page?id=1", "/kb/a.md", "/kb/b.md"],
            pipeline.LastRequest!.Sources.Select(s => s.Location));
    }
}
