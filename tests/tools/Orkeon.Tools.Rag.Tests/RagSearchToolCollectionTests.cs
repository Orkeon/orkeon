using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Tools.Rag.Tests.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// Collection routing of <see cref="RagSearchTool"/> (ported from the legacy
/// <c>RagToolCollectionTests</c>): <c>collection = "raggable-tree"</c> queries the
/// semantic code index; any other (or absent) collection goes to the RAG pipeline.
/// </summary>
public class RagSearchToolCollectionTests
{
    [Fact]
    public async Task CallAsync_routes_to_raggable_store_when_collection_matches()
    {
        var pipeline = new FakeRagPipeline();
        var store = new RecordingRaggableStore(
            [new SearchHit { Fqn = "pkg.Foo", Score = 0.91 }]);

        var tool = new RagSearchTool(pipeline, store);

        var resp = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>
            {
                ["question"] = "where is Foo declared?",
                ["top_k"] = 5,
                ["collection"] = "raggable-tree",
            }), TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal(1, store.SemanticCalls);
        Assert.Equal(0, pipeline.CallCount);
        Assert.Equal("where is Foo declared?", store.LastQuery?.Text);
        Assert.Equal(5, store.LastQuery?.TopK);
        Assert.Contains("pkg.Foo", resp.Result!.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallAsync_uses_pipeline_when_collection_is_unset()
    {
        var pipeline = new FakeRagPipeline
        {
            Answer = new RagAnswer { Text = "pipeline answer" },
        };
        var store = new RecordingRaggableStore([]);

        var tool = new RagSearchTool(pipeline, store);

        var resp = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "generic" }),
            TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal(0, store.SemanticCalls);
        Assert.Equal(1, pipeline.CallCount);
        Assert.Contains("pipeline answer", resp.Result!.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallAsync_falls_back_to_pipeline_when_no_store_is_wired()
    {
        var pipeline = new FakeRagPipeline
        {
            Answer = new RagAnswer { Text = "pipeline fallback" },
        };
        var tool = new RagSearchTool(pipeline);

        var resp = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>
            {
                ["question"] = "code question",
                ["collection"] = "raggable-tree",
            }), TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal(1, pipeline.CallCount);
        Assert.Equal("raggable-tree", pipeline.LastQuery?.Collection);
    }

    [Fact]
    public async Task CallAsync_formats_summary_or_signature_of_code_hits()
    {
        var pipeline = new FakeRagPipeline();
        var store = new RecordingRaggableStore(
            [
                new SearchHit { Fqn = "pkg.A", Score = 0.9, SummaryShort = "class A" },
                new SearchHit { Fqn = "pkg.B", Score = 0.7, Signature = "func B()" },
            ]);

        var tool = new RagSearchTool(pipeline, store);

        var resp = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>
            {
                ["question"] = "A or B",
                ["top_k"] = 8,
                ["collection"] = "raggable-tree",
            }), TestContext.Current.CancellationToken);

        var text = Assert.IsType<string>(resp.Result);
        Assert.Contains("- [pkg.A] (score: 0.90): class A", text, StringComparison.Ordinal);
        Assert.Contains("- [pkg.B] (score: 0.70): func B()", text, StringComparison.Ordinal);
    }
}
