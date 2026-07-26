using Orkeon.Domain.Configuration;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Ingestion;

/// <summary>
/// Tests for <see cref="RagCollectionsBootstrapper"/> — the YAML <c>rag:</c> block
/// mapped onto the incremental ingestion pipeline (RAG-03/C3).
/// </summary>
public class RagCollectionsBootstrapperTests
{
    [Fact]
    public async Task PrepareAsync_IngestsEveryDeclaredCollection_WithSourcesAndChunking()
    {
        var pipeline = new FakeIngestionPipeline();
        var bootstrapper = new RagCollectionsBootstrapper(pipeline);
        var config = new RagCrewConfig
        {
            Collections = new Dictionary<string, RagCollectionConfig>
            {
                ["produits"] = new()
                {
                    Sources = ["/kb/produits/**/*.md", "/kb/faq.md"],
                    Chunking = new RagChunkingConfig { Strategy = "sentence", MaxTokens = 100, Overlap = 10 },
                },
                ["support"] = new() { Sources = ["/kb/support.md"] },
            },
        };

        await bootstrapper.PrepareAsync(config, TestContext.Current.CancellationToken);

        Assert.Equal(2, pipeline.Requests.Count);

        var produits = pipeline.Requests.Single(r => r.Collection == "produits");
        Assert.Equal(["/kb/produits/**/*.md", "/kb/faq.md"], produits.Sources.Select(s => s.Location));
        Assert.Equal("sentence", produits.ChunkingStrategy);
        Assert.Equal(400, produits.Chunking.MaxChunkSize); // max_tokens × 4 chars/token
        Assert.Equal(40, produits.Chunking.Overlap);

        var support = pipeline.Requests.Single(r => r.Collection == "support");
        Assert.Null(support.ChunkingStrategy); // pipeline default
    }

    [Fact]
    public async Task PrepareAsync_SkipsCollectionsWithoutSources()
    {
        var pipeline = new FakeIngestionPipeline();
        var bootstrapper = new RagCollectionsBootstrapper(pipeline);
        var config = new RagCrewConfig
        {
            Collections = new Dictionary<string, RagCollectionConfig> { ["vide"] = new() },
        };

        await bootstrapper.PrepareAsync(config, TestContext.Current.CancellationToken);

        Assert.Empty(pipeline.Requests);
    }
}
