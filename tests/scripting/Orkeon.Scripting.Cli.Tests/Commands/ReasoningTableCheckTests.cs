using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The campaign checks the model capability table (#356, item 10): each step Orkeon judged (M15-M17) is
/// confronted with what the vendor does with the same value sent unjudged (M18).
/// </summary>
public sealed class ReasoningTableCheckTests
{
    private static LlmReasoningStep Step(
        string name, LlmReasoningOutcome outcome, string? wire = null, string? error = null,
        bool trace = false, long? reasoningTokens = null, IReadOnlyList<string>? warnings = null,
        VendorAnswer? vendor = null) =>
        new(name, outcome, null, wire, trace, reasoningTokens, null, null, warnings ?? [], error, vendor);

    private static LlmProbeResult Mode(LlmProbeMode mode, params LlmReasoningStep[] steps) =>
        new(mode, LlmProbeOutcome.Passed, "detail", 1, steps);

    private static LlmReasoningStep Checked(IReadOnlyList<LlmProbeResult> results, LlmProbeMode mode, string step) =>
        results.Single(r => r.Mode == mode).Reasoning!.Single(s => s.Step == step);

    [Fact]
    public void ShouldMarkAMissingRestriction_WhenTheVendorRefusedWhatOrkeonSent()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M15,
                Step("on", LlmReasoningOutcome.Sent, "{\"thinking\":{\"type\":\"enabled\"}}"),
                Step("off", LlmReasoningOutcome.Rejected, "{\"thinking\":{\"type\":\"disabled\"}}",
                    "Kimi API error: BadRequest - only type=enabled is allowed")),
        ]);

        var off = Checked(results, LlmProbeMode.M15, "off");
        Assert.Equal(LlmTableCheck.MissingRestriction, off.Check);
        Assert.Equal(LlmTableCheck.Agrees, Checked(results, LlmProbeMode.M15, "on").Check);
        Assert.Equal(LlmProbeOutcome.Failed, results[0].Outcome);
        Assert.Contains("table: off: missing restriction", results[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLeaveAStepUnchecked_WhenTheErrorIsNotARefusalOfTheValue()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M16, Step("high", LlmReasoningOutcome.Rejected, "{\"reasoning_effort\":\"high\"}", "429 TooManyRequests")),
        ]);

        Assert.Equal(LlmTableCheck.Unchecked, Checked(results, LlmProbeMode.M16, "high").Check);
        Assert.Equal(LlmProbeOutcome.Passed, results[0].Outcome);
    }

    [Fact]
    public void ShouldMarkTooNarrow_WhenOrkeonLoweredAnEffortTheVendorAccepts()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M16,
                Step("high", LlmReasoningOutcome.Sent, "{\"reasoning_effort\":\"high\"}"),
                Step("xhigh", LlmReasoningOutcome.Lowered, "{\"reasoning_effort\":\"high\"}"),
                Step("max", LlmReasoningOutcome.Lowered, "{\"reasoning_effort\":\"high\"}")),
            Mode(LlmProbeMode.M18,
                Step("effort:xhigh", LlmReasoningOutcome.Accepted),
                Step("effort:max", LlmReasoningOutcome.Refused, error: "400 bad")),
        ]);

        var xhigh = Checked(results, LlmProbeMode.M16, "xhigh");
        Assert.Equal(LlmTableCheck.TooNarrow, xhigh.Check);
        Assert.Contains("effort:xhigh", xhigh.CheckNote, StringComparison.Ordinal);
        Assert.Equal(LlmTableCheck.Agrees, Checked(results, LlmProbeMode.M16, "max").Check);
        Assert.Equal(LlmTableCheck.Agrees, Checked(results, LlmProbeMode.M16, "high").Check);
        Assert.Equal(LlmProbeOutcome.Failed, results[0].Outcome);
        Assert.Null(Checked(results, LlmProbeMode.M18, "effort:xhigh").Check);
    }

    [Fact]
    public void ShouldMarkTooNarrow_WhenOrkeonDroppedAnOffSwitchTheVendorHonours()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M15,
                Step("off", LlmReasoningOutcome.Omitted, warnings: ["always reasons"])),
            Mode(LlmProbeMode.M18,
                Step("toggle:off", LlmReasoningOutcome.Accepted, reasoningTokens: 0, vendor: new VendorAnswer(false, 0, "stop"))),
        ]);

        Assert.Equal(LlmTableCheck.TooNarrow, Checked(results, LlmProbeMode.M15, "off").Check);
    }

    [Fact]
    public void ShouldAgree_WhenTheVendorAcceptsTheOffSwitchButKeepsReasoning()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M15, Step("off", LlmReasoningOutcome.Omitted, warnings: ["always reasons"])),
            Mode(LlmProbeMode.M18,
                Step("off:disabled@high", LlmReasoningOutcome.Accepted, vendor: new VendorAnswer(true, 300, "stop")),
                Step("off:between_tools@high", LlmReasoningOutcome.Refused, error: "400 bad")),
        ]);

        var off = Checked(results, LlmProbeMode.M15, "off");
        Assert.Equal(LlmTableCheck.Agrees, off.Check);
        Assert.Equal(LlmProbeOutcome.Passed, results[0].Outcome);
    }

    [Fact]
    public void ShouldAgree_WhenOnIsLeftOutWithoutWarning_OnAModelThatAlwaysReasons()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M15, Step("on", LlmReasoningOutcome.Omitted)),
            Mode(LlmProbeMode.M18, Step("toggle:on", LlmReasoningOutcome.Accepted)),
        ]);

        Assert.Equal(LlmTableCheck.Agrees, Checked(results, LlmProbeMode.M15, "on").Check);
    }

    [Fact]
    public void ShouldLeaveUnchecked_WhenNoFragmentMatches()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M17, Step("budget", LlmReasoningOutcome.Omitted, warnings: ["no budget"])),
            Mode(LlmProbeMode.M16, Step("NONE", LlmReasoningOutcome.Omitted, warnings: ["dropped"])),
            Mode(LlmProbeMode.M18, Step("effort:none", LlmReasoningOutcome.Accepted)),
        ]);

        Assert.Equal(LlmTableCheck.Unchecked, Checked(results, LlmProbeMode.M17, "budget").Check);
        Assert.Equal(LlmTableCheck.TooNarrow, Checked(results, LlmProbeMode.M16, "NONE").Check);
    }

    [Fact]
    public void ShouldWriteTheCheck_InBothReports()
    {
        var results = ReasoningTableCheck.Apply(
        [
            Mode(LlmProbeMode.M16, Step("xhigh", LlmReasoningOutcome.Lowered, "{\"reasoning_effort\":\"high\"}")),
            Mode(LlmProbeMode.M18, Step("effort:xhigh", LlmReasoningOutcome.Accepted)),
        ]);
        var context = new LlmProbeContext("grok", "grok-4.7", "api.x.ai", "1.0.0", "abc", DateTimeOffset.UnixEpoch, 0d);

        using var document = JsonDocument.Parse(LlmProbeReport.ToJson(context, results));
        var modes = document.RootElement.GetProperty("modes");
        Assert.Equal("too narrow", modes[0].GetProperty("reasoning")[0].GetProperty("tableCheck").GetString());
        Assert.False(modes[1].GetProperty("reasoning")[0].TryGetProperty("tableCheck", out _));
        Assert.Contains("| ❌ too narrow: the vendor takes effort:xhigh |", LlmProbeReport.ToMarkdown(context, results), StringComparison.Ordinal);
    }
}
