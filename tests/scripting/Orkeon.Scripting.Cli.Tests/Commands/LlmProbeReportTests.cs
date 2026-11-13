using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The two shapes a campaign is rendered into: Markdown for a reader, JSON for the campaign
/// scripts that build the archived report and the index.
/// </summary>
public sealed class LlmProbeReportTests
{
    private static LlmProbeContext Context() => new(
        Provider: "openai",
        Model: "gpt-5.6-sol",
        EndpointHost: "api.openai.com",
        OrkeonVersion: "0.9.2-beta",
        Commit: "9cbc020",
        TimestampUtc: new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero),
        Temperature: 0d);

    private static IReadOnlyList<LlmProbeResult> Results() =>
    [
        new(LlmProbeMode.M1, LlmProbeOutcome.Passed, "12 char(s), tokens=5", 120),
        new(LlmProbeMode.M8, LlmProbeOutcome.Failed, "not JSON: oops", 90),
        new(LlmProbeMode.M9, LlmProbeOutcome.NotApplicable, "the provider declares no vision capability", 0),
    ];

    /// <summary>
    /// The report is meant to be pasted into the matrix journal, which requires the evidence
    /// level to be stated rather than implied.
    /// </summary>
    [Fact]
    public void ShouldRenderAJournalReadyReport()
    {
        var report = LlmProbeReport.ToMarkdown(Context(), Results());

        Assert.Contains("gpt-5.6-sol", report, StringComparison.Ordinal);
        Assert.Contains("2026-07-27", report, StringComparison.Ordinal);
        Assert.Contains("9cbc020", report, StringComparison.Ordinal);
        Assert.Contains("archived output", report, StringComparison.Ordinal);
        Assert.Contains("| M1 | ✅ |", report, StringComparison.Ordinal);
        Assert.Contains("| M8 | ❌ |", report, StringComparison.Ordinal);
        Assert.Contains("| M9 | ➖ |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldCountEachOutcomeSeparately_InTheJsonReport()
    {
        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(Context(), Results()));
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("passed").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("notApplicable").GetInt32());
        Assert.Equal(3, root.GetProperty("modes").GetArrayLength());
        Assert.Equal("not-applicable", root.GetProperty("modes")[2].GetProperty("outcome").GetString());
    }

    [Fact]
    public void ShouldStampTheJsonReport_WithTheIdentityOfTheRun()
    {
        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(Context(), Results()));
        var root = document.RootElement;

        Assert.Equal("openai", root.GetProperty("provider").GetString());
        Assert.Equal("api.openai.com", root.GetProperty("endpointHost").GetString());
        Assert.Equal("0.9.2-beta", root.GetProperty("orkeonVersion").GetString());
        Assert.Equal("2026-07-27T10:00:00Z", root.GetProperty("timestampUtc").GetString());
    }

    /// <summary>
    /// A report that claims reproducibility has to say what it pinned. Three consecutive M2 runs
    /// against the same llama3.2 returned ❌ ✅ ❌ at the framework default of 0.7 (2026-08-01);
    /// without this field, a reader months later cannot tell a measurement from a coin toss.
    /// </summary>
    [Fact]
    public void ShouldRecordTheSamplingTemperature_InBothRenderings()
    {
        var context = Context() with { Temperature = 0.4d };
        var results = Results();

        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(context, results));
        Assert.Equal(0.4d, document.RootElement.GetProperty("temperature").GetDouble());

        Assert.Contains("**Temperature**: 0.4", LlmProbeReport.ToMarkdown(context, results),
            StringComparison.Ordinal);
    }

    /// <summary>A pinned zero must render as a number, not vanish into an empty cell.</summary>
    [Fact]
    public void ShouldRenderAPinnedZero_RatherThanNothing()
    {
        Assert.Contains("**Temperature**: 0", LlmProbeReport.ToMarkdown(Context(), Results()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Same reproducibility contract for the M7 effort: <c>mistral-medium-2604</c> only
    /// accepts <c>high</c> (or <c>none</c>), so its M7 verdict was obtained with a
    /// non-default effort. The per-model registry promises "the report header prints the
    /// values actually used" — without this field that promise is false for exactly the
    /// models the registry exists for.
    /// </summary>
    [Fact]
    public void ShouldRecordTheM7Effort_WhenItIsNotTheDefault()
    {
        var context = Context() with { M7ThinkingEffort = "high" };
        var results = Results();

        Assert.Contains("**Reasoning effort (M7)**: high",
            LlmProbeReport.ToMarkdown(context, results), StringComparison.Ordinal);

        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(context, results));
        Assert.Equal("high", document.RootElement.GetProperty("m7_thinking_effort").GetString());
    }

    /// <summary>A run on the default effort has nothing to caveat — the line would be noise.</summary>
    [Fact]
    public void ShouldStayQuietAboutTheM7Effort_WhenTheRunUsedTheDefault()
    {
        Assert.DoesNotContain("Reasoning effort (M7)",
            LlmProbeReport.ToMarkdown(Context(), Results()), StringComparison.Ordinal);
    }

    /// <summary>
    /// LLM-08 states the constraint as non-negotiable: no API key in the repo, the matrix, or
    /// an archived trace. The report is the archived trace, so it is pinned here.
    /// </summary>
    [Fact]
    public void ShouldNeverCarryACredential_InEitherRendering()
    {
        var context = Context();
        var results = Results();

        foreach (var rendering in new[] {
            LlmProbeReport.ToMarkdown(context, results), LlmProbeReport.ToJson(context, results) })
        {
            Assert.DoesNotContain("apiKey", rendering, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", rendering, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", rendering, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ShouldResolveTheInformationalVersion_RatherThanTheFourPartAssemblyVersion()
    {
        var version = LlmProbeReport.ResolveVersion();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain("+", version, StringComparison.Ordinal);
    }

    private static IReadOnlyList<LlmProbeResult> ReasoningResults() =>
    [
        new(LlmProbeMode.M16, LlmProbeOutcome.Failed, "low ✅ · max ❌ 400 bad", 10,
        [
            new LlmReasoningStep("low", LlmReasoningOutcome.Sent, "low", "{\"reasoning_effort\":\"low\"}", true, 123, 45, "stop", [], null),
            new LlmReasoningStep("max", LlmReasoningOutcome.Rejected, null, null, false, null, null, null, ["w1"], "400 bad | value"),
        ]),
        new(LlmProbeMode.M1, LlmProbeOutcome.Passed, "ok", 5),
    ];

    [Fact]
    public void ShouldWriteTheReasoningSteps_UnderEachMode_InTheJsonReport()
    {
        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(Context(), ReasoningResults()));
        var modes = document.RootElement.GetProperty("modes");

        var step = modes[0].GetProperty("reasoning")[0];
        Assert.Equal("low", step.GetProperty("step").GetString());
        Assert.Equal("sent", step.GetProperty("outcome").GetString());
        Assert.Equal("low", step.GetProperty("sentValue").GetString());
        Assert.Equal("{\"reasoning_effort\":\"low\"}", step.GetProperty("wire").GetString());
        Assert.True(step.GetProperty("trace").GetBoolean());
        Assert.Equal(123, step.GetProperty("reasoningTokens").GetInt64());
        Assert.Equal(45, step.GetProperty("completionTokens").GetInt64());
        Assert.Equal("stop", step.GetProperty("finishReason").GetString());
        Assert.Equal(0, step.GetProperty("warnings").GetArrayLength());

        var rejected = modes[0].GetProperty("reasoning")[1];
        Assert.Equal("rejected", rejected.GetProperty("outcome").GetString());
        Assert.Equal("400 bad | value", rejected.GetProperty("error").GetString());

        Assert.False(modes[1].TryGetProperty("reasoning", out _));
    }

    [Fact]
    public void ShouldRenderAReasoningTable_OnlyWhenSomeModeHasSteps()
    {
        var with = LlmProbeReport.ToMarkdown(Context(), ReasoningResults());
        var without = LlmProbeReport.ToMarkdown(Context(), Results());

        Assert.Contains("| Mode | Step | Outcome | Sent | Trace | Reasoning tokens | Finish | Note |", with, StringComparison.Ordinal);
        Assert.Contains("| M16 | low | sent |", with, StringComparison.Ordinal);
        Assert.Contains("| M16 | max | rejected |", with, StringComparison.Ordinal);
        Assert.DoesNotContain("## Reasoning", without, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRecordTheSweepList_WhenSet()
    {
        var context = Context() with { SweepEfforts = "none,high" };

        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(context, Results()));

        Assert.Equal("none,high", document.RootElement.GetProperty("sweepEfforts").GetString());
        Assert.Contains("none,high", LlmProbeReport.ToMarkdown(context, Results()), StringComparison.Ordinal);
    }

    private static IReadOnlyList<LlmProbeResult> RawReasoningResults() =>
    [
        new(LlmProbeMode.M18, LlmProbeOutcome.Passed, "1 accepted · 1 refused (max)", 10,
        [
            new LlmReasoningStep("effort:xhigh", LlmReasoningOutcome.Accepted, null, "{\"reasoning_effort\":\"xhigh\"}", false, 12, 40, "stop", [], null),
            new LlmReasoningStep("effort:max", LlmReasoningOutcome.Refused, null, "{\"reasoning_effort\":\"max\"}", false, null, null, null, [], "400 bad"),
        ]),
    ];

    [Fact]
    public void ShouldWriteTheRawReasoningSteps_InTheJsonReport()
    {
        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(Context(), RawReasoningResults()));

        var steps = document.RootElement.GetProperty("modes")[0].GetProperty("reasoning");
        Assert.Equal("effort:xhigh", steps[0].GetProperty("step").GetString());
        Assert.Equal("accepted", steps[0].GetProperty("outcome").GetString());
        Assert.False(steps[0].TryGetProperty("sentValue", out _));
        Assert.Equal("{\"reasoning_effort\":\"xhigh\"}", steps[0].GetProperty("wire").GetString());
        Assert.Equal("refused", steps[1].GetProperty("outcome").GetString());
        Assert.Equal("400 bad", steps[1].GetProperty("error").GetString());
    }

    [Fact]
    public void ShouldRenderTheRawReasoningSteps_InTheMarkdownTable()
    {
        var markdown = LlmProbeReport.ToMarkdown(Context(), RawReasoningResults());

        Assert.Contains("| M18 | effort:xhigh | ✅ accepted |", markdown, StringComparison.Ordinal);
        Assert.Contains("| M18 | effort:max | ❌ refused |", markdown, StringComparison.Ordinal);
    }
}
