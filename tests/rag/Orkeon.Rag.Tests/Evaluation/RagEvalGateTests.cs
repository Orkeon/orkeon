using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>Tests for <see cref="RagEvalGate"/>: CI-gate means computed without a tagged subset.</summary>
public class RagEvalGateTests
{
    private static RagEvalCaseResult Case(string id, double? recall, double? rr, params string[] tags) => new()
    {
        CaseId = id,
        Judge = RagJudgeMode.Heuristic,
        Tags = [.. tags],
        RecallAtK = recall,
        ReciprocalRank = rr,
    };

    private static RagEvalReport Report(params RagEvalCaseResult[] cases) => new()
    {
        DatasetName = "golden",
        Profile = "default",
        Collection = "c",
        Judge = RagJudgeMode.Heuristic,
        Aggregate = new RagEvalAggregate { CaseCount = cases.Length },
        Cases = [.. cases],
    };

    [Fact]
    public void ShouldAverageCasesWithoutExcludedTag_WhenTagMatchesCaseInsensitively()
    {
        var report = Report(
            Case("a", 1.0, 1.0),
            Case("b", 0.5, 0.25, "smoke"),
            Case("c", 0.0, 0.0, "KNOWN-GAP"),
            Case("d", null, 0.5));

        Assert.Equal(0.75, RagEvalGate.MeanRecallExcluding(report, "known-gap"));
        Assert.Equal(0.5833333333333334, RagEvalGate.MeanReciprocalRankExcluding(report, "known-gap")!.Value, 12);
    }

    [Fact]
    public void ShouldReturnNull_WhenEveryRemainingValueIsMissing()
    {
        var report = Report(Case("a", null, null), Case("b", 1.0, 1.0, "gap"));

        Assert.Null(RagEvalGate.MeanRecallExcluding(report, "gap"));
        Assert.Null(RagEvalGate.MeanReciprocalRankExcluding(report, "gap"));
    }

    [Fact]
    public void ShouldReturnNull_WhenReportHasNoCase()
    {
        Assert.Null(RagEvalGate.MeanRecallExcluding(Report(), "gap"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ShouldThrow_WhenExcludedTagIsBlank(string? tag)
    {
        Assert.ThrowsAny<ArgumentException>(() => RagEvalGate.MeanRecallExcluding(Report(), tag!));
    }

    [Fact]
    public void ShouldThrow_WhenReportIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RagEvalGate.MeanReciprocalRankExcluding(null!, "gap"));
    }
}
