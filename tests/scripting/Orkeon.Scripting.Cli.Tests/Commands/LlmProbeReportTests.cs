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
        Assert.Contains("sortie archivée", report, StringComparison.Ordinal);
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

        Assert.Contains("**Température** : 0.4", LlmProbeReport.ToMarkdown(context, results),
            StringComparison.Ordinal);
    }

    /// <summary>A pinned zero must render as a number, not vanish into an empty cell.</summary>
    [Fact]
    public void ShouldRenderAPinnedZero_RatherThanNothing()
    {
        Assert.Contains("**Température** : 0", LlmProbeReport.ToMarkdown(Context(), Results()),
            StringComparison.Ordinal);
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
}
