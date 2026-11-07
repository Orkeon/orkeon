using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Pins <see cref="FinalAnswerPolicy.IsToolCallMimicry"/> and the wording of
/// <see cref="FinalAnswerPolicy.BuildFinalAnswerNudge"/>. Both exist to defuse the
/// round-36 / round-37 failure mode where a DeepSeek thinking-mode agent that exhausts
/// its iteration budget echoes its native tool-call markup
/// (<c>&lt;｜｜DSML｜｜tool_calls&gt;…&lt;｜｜DSML｜｜invoke name="…"&gt;…</c>) as the
/// task's final deliverable instead of plain Markdown.
/// </summary>
public class ExecutionOrchestratorMimicryDetectionTests
{
    [Fact]
    public void IsToolCallMimicry_DetectsDeepSeekDsmlMarkup()
    {
        var dsml =
            "<｜｜DSML｜｜tool_calls>\n" +
            "<｜｜DSML｜｜invoke name=\"codebase_search\">\n" +
            "<｜｜DSML｜｜parameter name=\"query\" string=\"true\">class Agent</｜｜DSML｜｜parameter>\n" +
            "</｜｜DSML｜｜invoke>\n" +
            "</｜｜DSML｜｜tool_calls>";

        Assert.True(FinalAnswerPolicy.IsToolCallMimicry(dsml));
    }

    [Fact]
    public void IsToolCallMimicry_DetectsBareInvokeOpening()
    {
        Assert.True(FinalAnswerPolicy.IsToolCallMimicry(
            "<invoke name=\"codebase_search\"><parameter name=\"q\">x</parameter></invoke>"));
    }

    [Fact]
    public void IsToolCallMimicry_DetectsBareToolCallsTag()
    {
        Assert.True(FinalAnswerPolicy.IsToolCallMimicry(
            "<tool_calls><invoke name=\"x\"/></tool_calls>"));
    }

    [Fact]
    public void IsToolCallMimicry_AcceptsLeadingWhitespace()
    {
        Assert.True(FinalAnswerPolicy.IsToolCallMimicry(
            "   \n\t<｜｜DSML｜｜tool_calls>"));
    }

    [Theory]
    [InlineData("# Architecture report\n\nThe codebase has 113 commands…")]
    [InlineData("Final answer: the package map shows a single-package layout.")]
    [InlineData("The agent runtime lives in `src/QueryEngine.ts`.")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsToolCallMimicry_AcceptsValidDeliverables(string text)
    {
        Assert.False(FinalAnswerPolicy.IsToolCallMimicry(text));
    }

    [Fact]
    public void BuildFinalAnswerNudge_FirstPass_ForbidsToolCallMarkup()
    {
        var msg = FinalAnswerPolicy.BuildFinalAnswerNudge(escalated: false);

        Assert.Contains("plain Markdown", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DSML", msg, StringComparison.Ordinal);
        Assert.Contains("Markdown heading", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFinalAnswerNudge_Escalated_PinsTheFirstCharacter()
    {
        var msg = FinalAnswerPolicy.BuildFinalAnswerNudge(escalated: true);

        // Escalation must reference the prior failure mode and force a syntactic anchor.
        Assert.Contains("DSML", msg, StringComparison.Ordinal);
        Assert.Contains("first non-whitespace character", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`#`", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFinalAnswerNudge_FirstAndEscalatedDiffer()
    {
        var first = FinalAnswerPolicy.BuildFinalAnswerNudge(escalated: false);
        var second = FinalAnswerPolicy.BuildFinalAnswerNudge(escalated: true);
        Assert.NotEqual(first, second);
    }
}
