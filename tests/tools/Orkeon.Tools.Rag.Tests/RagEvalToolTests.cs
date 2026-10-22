using System.Text.Json;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Rag.Tests.Doubles;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// The <c>rag_eval</c> agent tool over the hand-written harness double:
/// parameter mapping, tolerant <c>compare</c> extraction, output format
/// (aggregates + labelled judge + report paths), and input validation.
/// </summary>
public sealed class RagEvalToolTests
{
    private static ToolCallRequest Request(Dictionary<string, object?> parameters)
        => new("rag_eval", parameters);

    [Fact]
    public async Task Call_MapsParameters_AndFormatsTheSummary()
    {
        var harness = new FakeRagEvalHarness();
        using var tool = new RagEvalTool(harness);

        var response = await tool.CallAsync(Request(new Dictionary<string, object?>
        {
            ["dataset"] = "/workspace/eval/golden.yaml",
            ["collection"] = "kb",
            ["profile"] = "balanced",
            ["k"] = 3,
            ["use_llm_judge"] = true,
            ["reindex"] = true,
        }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var request = Assert.Single(harness.Requests);
        Assert.Equal("/workspace/eval/golden.yaml", request.DatasetPath);
        Assert.Equal("kb", request.Collection);
        Assert.Equal(["balanced"], request.Profiles);
        Assert.Equal(3, request.K);
        Assert.True(request.UseLlmJudge);
        Assert.True(request.ReindexCorpus);

        Assert.IsType<Orkeon.Tools.Abstractions.Base.ToolBase<RagEvalRequest, RagEvalResponse>>(tool, exactMatch: false);
        var view = RagEvalResultView.From(response.Result);
        var metric = Assert.Single(view.Metrics);
        Assert.Equal("balanced", RagEvalResultView.Prop(metric, "Profile").GetString());
        Assert.Equal("heuristic", RagEvalResultView.Prop(metric, "Judge").GetString());
        Assert.Equal(0.9, RagEvalResultView.Prop(metric, "RecallAtK").GetDouble());
        Assert.Equal(0.8, RagEvalResultView.Prop(metric, "Mrr").GetDouble());
        Assert.Contains("/output/rag/eval/golden-balanced.md", view.Reports);
    }

    [Fact]
    public async Task Call_WithoutCollection_HandsTheConfiguredDefaultCollectionToTheHarness()
    {
        // GAP-02: Orkeon:Rag:Collection reaches the harness as the host default; an
        // explicit argument (and the dataset's own collection) still win there.
        var harness = new FakeRagEvalHarness();
        using var tool = new RagEvalTool(harness, defaultCollection: "produits");

        await tool.CallAsync(Request(new Dictionary<string, object?>
        {
            ["dataset"] = "/workspace/eval/golden.yaml",
        }), TestContext.Current.CancellationToken);

        var request = Assert.Single(harness.Requests);
        Assert.Null(request.Collection);
        Assert.Equal("produits", request.DefaultCollection);
    }

    [Fact]
    public async Task Call_CompareMode_OverridesProfile_AndIncludesTheComparisonTable()
    {
        var harness = new FakeRagEvalHarness();
        using var tool = new RagEvalTool(harness);

        var response = await tool.CallAsync(Request(new Dictionary<string, object?>
        {
            ["dataset"] = "/workspace/eval/golden.yaml",
            ["profile"] = "ignored",
            ["compare"] = "fast, balanced, quality",
        }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Equal(["fast", "balanced", "quality"], Assert.Single(harness.Requests).Profiles);

        var view = RagEvalResultView.From(response.Result);
        Assert.Equal(
            ["fast", "balanced", "quality"],
            view.Metrics.Select(m => RagEvalResultView.Prop(m, "Profile").GetString()));
        Assert.Contains("/output/rag/eval/golden-compare.md", view.Reports);
    }

    [Fact]
    public async Task Call_WithoutDataset_Fails()
    {
        using var tool = new RagEvalTool(new FakeRagEvalHarness());

        var response = await tool.CallAsync(
            Request([]), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("dataset parameter is required", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_LegacyStringEntrypoint_IsRejected()
    {
        using var tool = new RagEvalTool(new FakeRagEvalHarness());

        var result = await tool.ExecuteAsync("golden.yaml", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("dataset parameter is required", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_DeclaresTheAgentFacingContract()
    {
        using var tool = new RagEvalTool(new FakeRagEvalHarness());
        var schema = tool.Schema;

        Assert.Equal("rag_eval", schema.Name);
        Assert.True(schema.Parameters["dataset"].Required);
        Assert.False(schema.Parameters["compare"].Required);
        Assert.False(schema.Parameters["use_llm_judge"].Required);
        Assert.False(schema.Parameters["reindex"].Required);
    }

    private static readonly string[] TwoProfiles = ["a", "b"];

    [Fact]
    public void ExtractProfiles_ToleratesCsv_JsonArrays_AndEnumerables()
    {
        Assert.Equal(["a", "b"], RagEvalTool.ExtractProfiles("a, b"));
        Assert.Equal(["a", "b"], RagEvalTool.ExtractProfiles(TwoProfiles));
        Assert.Equal(
            ["a", "b"],
            RagEvalTool.ExtractProfiles(JsonElement.Parse("""["a","b"]""")));
        Assert.Equal(
            ["a", "b"],
            RagEvalTool.ExtractProfiles(JsonElement.Parse("\"a,b\"")));
        Assert.Empty(RagEvalTool.ExtractProfiles(null));
    }
}

/// <summary>Reads the runtime result of the <c>ToolBase&lt;,&gt;</c> rag_eval tool (a dictionary read through JSON).</summary>
internal sealed record RagEvalResultView(IReadOnlyList<JsonElement> Metrics, IReadOnlyList<JsonElement> Cases, IReadOnlyList<string> Reports)
{
    public static RagEvalResultView From(object? result)
    {
        var dict = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.True(dict.TryGetValue("metrics", out var metrics));
        Assert.True(dict.TryGetValue("cases", out var cases));
        Assert.True(dict.TryGetValue("reports", out var reports));
        return new RagEvalResultView(
            [.. JsonSerializer.SerializeToElement(metrics).EnumerateArray()],
            [.. JsonSerializer.SerializeToElement(cases).EnumerateArray()],
            [.. JsonSerializer.SerializeToElement(reports).EnumerateArray().Select(e => e.GetString()!)]);
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
