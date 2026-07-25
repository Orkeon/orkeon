using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Tools.Rag.Tests.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// Behavior of <see cref="RagSearchTool"/> over the new <c>IRagPipeline</c>:
/// schema continuity (question / top_k / collection), legacy-compatible output
/// format (answer + <c>Sources:</c> block with scores), and parameter propagation.
/// </summary>
public class RagSearchToolTests
{
    private static RagAnswer AnswerWithCitations() => new()
    {
        Text = "Refunds are allowed within 30 days [1].",
        Citations =
        [
            new Citation
            {
                Marker = 1,
                ChunkId = "chunk-1",
                SourceId = "policy",
                Snippet = "Customers may request a full refund within 30 days of purchase.",
                Score = 0.91,
            },
        ],
    };

    [Fact]
    public void Tool_IsNamed_RagSearch_WithLegacySchema()
    {
        var tool = new RagSearchTool(new FakeRagPipeline());

        Assert.Equal("rag_search", tool.Name);
        Assert.True(tool.Schema.Parameters["question"].Required);
        Assert.False(tool.Schema.Parameters["top_k"].Required);
        Assert.False(tool.Schema.Parameters["collection"].Required);
    }

    [Fact]
    public async Task CallAsync_FormatsAnswer_AndSourcesBlock_LikeTheLegacyTool()
    {
        var pipeline = new FakeRagPipeline { Answer = AnswerWithCitations() };
        var tool = new RagSearchTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "refund policy?" }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var text = Assert.IsType<string>(response.Result);
        Assert.Contains("Refunds are allowed within 30 days [1].", text, StringComparison.Ordinal);
        Assert.Contains("Sources:", text, StringComparison.Ordinal);
        Assert.Contains("- [policy] (score: 0.91):", text, StringComparison.Ordinal);
        Assert.Contains("full refund within 30 days", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallAsync_Fails_WhenQuestionIsMissing()
    {
        var tool = new RagSearchTool(new FakeRagPipeline());

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>()), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("question", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_PropagatesTopK_Collection_AndDefaults()
    {
        var pipeline = new FakeRagPipeline();
        var tool = new RagSearchTool(pipeline);

        await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>
            {
                ["question"] = "q1",
                ["top_k"] = 7,
                ["collection"] = "handbook",
            }), TestContext.Current.CancellationToken);

        Assert.Equal("q1", pipeline.LastQuery?.Text);
        Assert.Equal(7, pipeline.LastQuery?.TopN);
        Assert.Equal("handbook", pipeline.LastQuery?.Collection);

        await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "q2" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(RagSearchTool.DefaultCollection, pipeline.LastQuery?.Collection);
        Assert.Equal(3, pipeline.LastQuery?.TopN);
    }

    [Fact]
    public async Task ExecuteAsync_LegacyStringInput_RoutesToThePipeline()
    {
        var pipeline = new FakeRagPipeline { Answer = AnswerWithCitations() };
        var tool = new RagSearchTool(pipeline);

        var result = await tool.ExecuteAsync("refund policy?", TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("Refunds are allowed", result.Output, StringComparison.Ordinal);
        Assert.Equal("refund policy?", pipeline.LastQuery?.Text);
    }

    [Fact]
    public async Task CallAsync_WithoutCitations_OmitsTheSourcesBlock()
    {
        var pipeline = new FakeRagPipeline
        {
            Answer = new RagAnswer { Text = "no context", Citations = ImmutableList<Citation>.Empty },
        };
        var tool = new RagSearchTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "anything" }),
            TestContext.Current.CancellationToken);

        var text = Assert.IsType<string>(response.Result);
        Assert.DoesNotContain("Sources:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateInput_RejectsBlankInput()
    {
        var tool = new RagSearchTool(new FakeRagPipeline());

        Assert.True(tool.ValidateInput("question"));
        Assert.False(tool.ValidateInput("  "));
    }
}
