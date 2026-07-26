using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Deterministic judge fallback (plan §9.2, zero network — CI-safe):
/// <list type="bullet">
///   <item><description>
///     <b>answer-relevance</b> = fraction of the case's <c>expected_substrings</c>
///     present in the answer text (ordinal, case-insensitive); <c>null</c> when
///     the case declares none.
///   </description></item>
///   <item><description>
///     <b>groundedness</b> = citation coverage of the relevant refs: fraction of
///     <c>relevant</c> covered by at least one citation (0 when the answer has no
///     citation at all); <c>null</c> when the case has no <c>relevant</c> ground truth.
///   </description></item>
/// </list>
/// </summary>
public sealed class HeuristicGenerationJudge : IGenerationJudge
{
    /// <inheritdoc />
    public RagJudgeMode Mode => RagJudgeMode.Heuristic;

    /// <inheritdoc />
    public Task<GenerationJudgement> JudgeAsync(
        RagEvalCase evalCase,
        RagAnswer answer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evalCase);
        ArgumentNullException.ThrowIfNull(answer);

        return Task.FromResult(new GenerationJudgement
        {
            Mode = RagJudgeMode.Heuristic,
            AnswerRelevance = ComputeAnswerRelevance(evalCase, answer),
            Groundedness = ComputeGroundedness(evalCase, answer),
        });
    }

    private static double? ComputeAnswerRelevance(RagEvalCase evalCase, RagAnswer answer)
    {
        if (evalCase.ExpectedSubstrings.Count == 0)
            return null;

        var text = answer.Text ?? string.Empty;
        var present = evalCase.ExpectedSubstrings
            .Count(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));

        return (double)present / evalCase.ExpectedSubstrings.Count;
    }

    private static double? ComputeGroundedness(RagEvalCase evalCase, RagAnswer answer)
    {
        if (evalCase.Relevant.Count == 0)
            return null;

        if (answer.Citations.Count == 0)
            return 0.0;

        var covered = evalCase.Relevant.Count(reference => answer.Citations.Any(citation =>
            RetrievalEvalMetrics.Matches(citation.ChunkId, reference)
            || (citation.DocumentId is not null && RetrievalEvalMetrics.Matches(citation.DocumentId, reference))
            || RetrievalEvalMetrics.Matches(citation.SourceId, reference)));

        return (double)covered / evalCase.Relevant.Count;
    }
}
