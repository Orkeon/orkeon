using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Generation judgement produced by an <see cref="IGenerationJudge"/> for one
/// case. <see cref="Mode"/> is the mode that ACTUALLY produced the scores — an
/// LLM run that fell back to the heuristic labels the case accordingly.
/// </summary>
public sealed record GenerationJudgement
{
    /// <summary>Judge mode that produced these scores.</summary>
    public required RagJudgeMode Mode { get; init; }

    /// <summary>Groundedness in [0, 1]; <c>null</c> when not measurable.</summary>
    public double? Groundedness { get; init; }

    /// <summary>Answer-relevance in [0, 1]; <c>null</c> when not measurable.</summary>
    public double? AnswerRelevance { get; init; }
}

/// <summary>
/// Judges the generation quality (groundedness / answer-relevance) of a
/// <see cref="RagAnswer"/> for a dataset case (plan §9.2).
/// </summary>
public interface IGenerationJudge
{
    /// <summary>Mode this judge runs in (label surfaced in the report).</summary>
    RagJudgeMode Mode { get; }

    /// <summary>Judges <paramref name="answer"/> against <paramref name="evalCase"/>.</summary>
    Task<GenerationJudgement> JudgeAsync(
        RagEvalCase evalCase,
        RagAnswer answer,
        CancellationToken cancellationToken = default);
}
