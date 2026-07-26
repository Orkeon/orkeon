using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Anti-regression gate helpers (plan §9.3): aggregate a metric over the cases of
/// a report while excluding a tag — typically <see cref="RagEvalCase.CorrectiveTag"/>,
/// whose cases are SEEDED to fail plain retrieval and must not sink the CI gate.
/// </summary>
public static class RagEvalGate
{
    /// <summary>Mean recall@K over cases not tagged <paramref name="excludedTag"/> (<c>null</c> when none contributes).</summary>
    public static double? MeanRecallExcluding(RagEvalReport report, string excludedTag)
        => MeanExcluding(report, excludedTag, c => c.RecallAtK);

    /// <summary>Mean reciprocal rank over cases not tagged <paramref name="excludedTag"/> (<c>null</c> when none contributes).</summary>
    public static double? MeanReciprocalRankExcluding(RagEvalReport report, string excludedTag)
        => MeanExcluding(report, excludedTag, c => c.ReciprocalRank);

    private static double? MeanExcluding(
        RagEvalReport report,
        string excludedTag,
        Func<RagEvalCaseResult, double?> selector)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(excludedTag);

        var values = report.Cases
            .Where(c => !c.Tags.Contains(excludedTag, StringComparer.OrdinalIgnoreCase))
            .Select(selector)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        return values.Count > 0 ? values.Average() : null;
    }
}
