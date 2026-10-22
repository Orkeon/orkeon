using System.Collections.Immutable;
using System.Text.Json;
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
        using var tool = new RagSearchTool(new FakeRagPipeline());

        Assert.Equal("rag_search", tool.Name);
        Assert.True(tool.Schema.Parameters["question"].Required);
        Assert.False(tool.Schema.Parameters["top_k"].Required);
        Assert.False(tool.Schema.Parameters["collection"].Required);
    }

    [Fact]
    public async Task CallAsync_FormatsAnswer_AndSourcesBlock_LikeTheLegacyTool()
    {
        var pipeline = new FakeRagPipeline { Answer = AnswerWithCitations() };
        using var tool = new RagSearchTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "refund policy?" }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var typed = RagSearchResultView.From(response.Result);
        Assert.Equal("Refunds are allowed within 30 days [1].", typed.Answer);
        var citation = Assert.Single(typed.Citations);
        Assert.Equal("policy", RagSearchResultView.Prop(citation, "SourceId").GetString());
        Assert.Equal(0.91, RagSearchResultView.Prop(citation, "Score").GetDouble());
        Assert.Contains("full refund within 30 days", RagSearchResultView.Prop(citation, "Snippet").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallAsync_Fails_WhenQuestionIsMissing()
    {
        using var tool = new RagSearchTool(new FakeRagPipeline());

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?>()), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("question", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_PropagatesTopK_Collection_AndDefaults()
    {
        var pipeline = new FakeRagPipeline();
        using var tool = new RagSearchTool(pipeline);

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
        using var tool = new RagSearchTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "refund policy?" }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Equal("Refunds are allowed within 30 days [1].", RagSearchResultView.From(response.Result).Answer);
        Assert.Equal("refund policy?", pipeline.LastQuery?.Text);
    }

    [Fact]
    public async Task CallAsync_WithoutCitations_OmitsTheSourcesBlock()
    {
        var pipeline = new FakeRagPipeline
        {
            Answer = new RagAnswer { Text = "no context", Citations = ImmutableList<Citation>.Empty },
        };
        using var tool = new RagSearchTool(pipeline);

        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "anything" }),
            TestContext.Current.CancellationToken);

        var typed = RagSearchResultView.From(response.Result);
        Assert.Equal("no context", typed.Answer);
        Assert.Empty(typed.Citations);
    }

    [Fact]
    [Trait("BR", "RT-25")]
    public async Task ShouldExposeTypedResult_WhenRagSearchCalled()
    {
        var pipeline = new FakeRagPipeline { Answer = AnswerWithCitations() };
        using var tool = new RagSearchTool(pipeline);

        Assert.IsType<Orkeon.Tools.Abstractions.Base.ToolBase<RagSearchRequest, RagSearchResponse>>(tool, exactMatch: false);
        var response = await tool.CallAsync(new ToolCallRequest("rag_search",
            new Dictionary<string, object?> { ["question"] = "refund policy?" }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var typed = RagSearchResultView.From(response.Result);
        Assert.Equal("Refunds are allowed within 30 days [1].", typed.Answer);
        var citation = Assert.Single(typed.Citations);
        Assert.Equal("policy", RagSearchResultView.Prop(citation, "SourceId").GetString());
    }
}

/// <summary>
/// Reads the runtime result of a <c>ToolBase&lt;,&gt;</c> tool: a dictionary (the response record
/// serialised) whose <c>answer</c> and <c>citations</c> entries are read through JSON, names
/// compared ignoring case and underscores.
/// </summary>
internal sealed record RagSearchResultView(string? Answer, IReadOnlyList<JsonElement> Citations)
{
    public static RagSearchResultView From(object? result)
    {
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.True(dict.TryGetValue("answer", out var answer));
        Assert.True(dict.TryGetValue("citations", out var citations));
        var items = JsonSerializer.SerializeToElement(citations).EnumerateArray().ToList();
        return new RagSearchResultView(answer?.ToString(), items);
    }

    public static JsonElement Prop(JsonElement element, string name)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name.Replace("_", "", StringComparison.Ordinal), name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        }

        throw new Xunit.Sdk.XunitException($"property '{name}' not found in {element}");
    }
}
