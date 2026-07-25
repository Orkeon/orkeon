using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;
using Orkeon.Infrastructure.Knowledge.Chunking;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using RagToolImpl = Orkeon.Infrastructure.Knowledge.RagTool;
using KnowledgeServiceImpl = Orkeon.Infrastructure.Knowledge.KnowledgeService;

namespace Orkeon.Infrastructure.Tests.Knowledge;

/// <summary>
/// RAG-01/C5: <see cref="RagToolQuery.Filters"/> must be propagated down the retrieval chain
/// (RagTool → pipeline options → retriever → knowledge service → provider) instead of being
/// silently dropped.
/// </summary>
public class RagToolFiltersPropagationTests
{
    [Fact]
    public async Task SearchAsync_ShouldMapFiltersToRetrievalMetadataFilter()
    {
        // Arrange — the pipeline spy records the options built by RagTool.
        var pipeline = new MockRagPipeline();
        var tool = new RagToolImpl(pipeline);

        var query = new RagToolQuery
        {
            Text = "quantum",
            TopK = 4,
            Filters = ImmutableDictionary.CreateRange(new Dictionary<string, object>
            {
                ["source"] = "manual",
                ["category"] = "physics",
            }),
        };

        // Act
        await tool.SearchAsync(query, TestContext.Current.CancellationToken);

        // Assert
        var options = pipeline.LastExecuteOptions;
        Assert.NotNull(options);
        Assert.Equal(4, options!.Retrieval.TopK);
        Assert.NotNull(options.Retrieval.MetadataFilter);
        Assert.Equal("manual", options.Retrieval.MetadataFilter!["source"]);
        Assert.Equal("physics", options.Retrieval.MetadataFilter["category"]);
    }

    [Fact]
    public async Task SearchAsync_WithoutFilters_ShouldPassNullMetadataFilter()
    {
        // Arrange
        var pipeline = new MockRagPipeline();
        var tool = new RagToolImpl(pipeline);

        // Act
        await tool.SearchAsync(new RagToolQuery { Text = "quantum" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(pipeline.LastExecuteOptions);
        Assert.Null(pipeline.LastExecuteOptions!.Retrieval.MetadataFilter);
    }

    [Fact]
    public async Task SearchAsync_ShouldPropagateFiltersToKnowledgeService_ThroughRealPipeline()
    {
        // Arrange — real pipeline + real retriever, hand-written spy at the service boundary
        // (the deepest seam before the memory provider, which the service reaches in C2).
        var spyService = new MockKnowledgeService();
        spyService.SetSearchResult(
        [
            KnowledgeItem.Create("quantum entanglement basics", "manual") with { SimilarityScore = 0.9 },
        ]);

        var pipeline = new RagPipeline(
            new KnowledgeRetriever(spyService),
            new StubContextAugmenter(),
            new StubResponseGenerator(),
            NullLogger<RagPipeline>.Instance);

        var tool = new RagToolImpl(pipeline);

        var query = new RagToolQuery
        {
            Text = "quantum",
            TopK = 3,
            Filters = ImmutableDictionary.CreateRange(new Dictionary<string, object>
            {
                ["category"] = "physics",
            }),
        };

        // Act
        var hits = await tool.SearchAsync(query, TestContext.Current.CancellationToken);

        // Assert — the filters reached the knowledge service intact.
        Assert.Equal(1, spyService.SearchCallCount);
        Assert.NotNull(spyService.LastSearchFilters);
        Assert.Equal("physics", spyService.LastSearchFilters!["category"]);
        Assert.Single(hits);
        Assert.Equal("manual", hits[0].SourceId);
    }

    [Fact]
    public async Task SearchAsync_EndToEnd_FiltersRestrictResults_WithRealKnowledgeService()
    {
        // Arrange — fully real chain: RagTool → RagPipeline → KnowledgeRetriever →
        // KnowledgeService. Two items match the query text; the filter must keep only one.
        var service = new KnowledgeServiceImpl(new RecursiveTextChunker(), new FakeFileSystemService());
        await service.AddKnowledgeAsync(
            "Alpha guide to quantum computing.",
            metadata: new Dictionary<string, object> { ["category"] = "computing" },
            source: "alpha",
            cancellationToken: TestContext.Current.CancellationToken);
        await service.AddKnowledgeAsync(
            "Beta primer on quantum physics.",
            metadata: new Dictionary<string, object> { ["category"] = "physics" },
            source: "beta",
            cancellationToken: TestContext.Current.CancellationToken);

        var pipeline = new RagPipeline(
            new KnowledgeRetriever(service),
            new StubContextAugmenter(),
            new StubResponseGenerator(),
            NullLogger<RagPipeline>.Instance);

        var tool = new RagToolImpl(pipeline);

        // Act — filter on the item source.
        var bySource = await tool.SearchAsync(new RagToolQuery
        {
            Text = "quantum",
            TopK = 5,
            Filters = ImmutableDictionary.CreateRange(
                new Dictionary<string, object> { ["source"] = "beta" }),
        }, TestContext.Current.CancellationToken);

        // Act — filter on a custom metadata key.
        var byMetadata = await tool.SearchAsync(new RagToolQuery
        {
            Text = "quantum",
            TopK = 5,
            Filters = ImmutableDictionary.CreateRange(
                new Dictionary<string, object> { ["category"] = "computing" }),
        }, TestContext.Current.CancellationToken);

        // Act — no filter: both items are eligible.
        var unfiltered = await tool.SearchAsync(new RagToolQuery
        {
            Text = "quantum",
            TopK = 5,
        }, TestContext.Current.CancellationToken);

        // Assert
        var sourceHit = Assert.Single(bySource);
        Assert.Equal("beta", sourceHit.SourceId);

        var metadataHit = Assert.Single(byMetadata);
        Assert.Equal("alpha", metadataHit.SourceId);

        Assert.Equal(2, unfiltered.Count);
    }
}
