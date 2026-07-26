namespace Orkeon.Rag.Validation;

/// <summary>
/// Full, auditable result of a prompt-injection analysis: verdict, cumulative risk
/// score, one reason per heuristic that fired, and the exact incriminated spans.
/// </summary>
public sealed record PromptInjectionAnalysis
{
    /// <summary>Shared clean result (no heuristic fired).</summary>
    public static PromptInjectionAnalysis CleanResult { get; } = new()
    {
        Verdict = PromptInjectionVerdict.Clean,
        RiskScore = 0.0,
    };

    /// <summary>The per-document verdict.</summary>
    public required PromptInjectionVerdict Verdict { get; init; }

    /// <summary>Cumulative risk score in [0, 1].</summary>
    public required double RiskScore { get; init; }

    /// <summary>One human-readable reason per heuristic rule that fired.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>The content regions that triggered the heuristics.</summary>
    public IReadOnlyList<InjectionSpan> Spans { get; init; } = [];
}
