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
        var tool = new RagEvalTool(harness);

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

        var text = Assert.IsType<string>(response.Result);
        Assert.Contains("RAG evaluation — dataset 'golden'", text, StringComparison.Ordinal);
        Assert.Contains("Profile 'balanced' — judge: heuristic", text, StringComparison.Ordinal);
        Assert.Contains("recall@3: 0.90", text, StringComparison.Ordinal);
        Assert.Contains("MRR: 0.80", text, StringComparison.Ordinal);
        Assert.Contains("/output/rag/eval/golden-balanced.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_CompareMode_OverridesProfile_AndIncludesTheComparisonTable()
    {
        var harness = new FakeRagEvalHarness();
        var tool = new RagEvalTool(harness);

        var response = await tool.CallAsync(Request(new Dictionary<string, object?>
        {
            ["dataset"] = "/workspace/eval/golden.yaml",
            ["profile"] = "ignored",
            ["compare"] = "fast, balanced, quality",
        }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Equal(["fast", "balanced", "quality"], Assert.Single(harness.Requests).Profiles);

        var text = Assert.IsType<string>(response.Result);
        Assert.Contains("| profile | recall@5 | MRR |", text, StringComparison.Ordinal);
        Assert.Contains("| fast |", text, StringComparison.Ordinal);
        Assert.Contains("/output/rag/eval/golden-compare.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_WithoutDataset_Fails()
    {
        var tool = new RagEvalTool(new FakeRagEvalHarness());

        var response = await tool.CallAsync(
            Request([]), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("dataset parameter is required", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_LegacyStringEntrypoint_IsRejected()
    {
        var tool = new RagEvalTool(new FakeRagEvalHarness());

        var result = await tool.ExecuteAsync("golden.yaml", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("structured parameters", result.Error, StringComparison.Ordinal);
        Assert.False(tool.ValidateInput("golden.yaml"));
    }

    [Fact]
    public void Schema_DeclaresTheAgentFacingContract()
    {
        var schema = new RagEvalTool(new FakeRagEvalHarness()).Schema;

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
