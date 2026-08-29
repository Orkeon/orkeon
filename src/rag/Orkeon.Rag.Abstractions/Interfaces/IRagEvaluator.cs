using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Evaluation harness facade (plan §9): runs a golden dataset against the
/// pipeline resolved for <see cref="RagEvalOptions.Profile"/> and returns a
/// <see cref="RagEvalReport"/> with deterministic retrieval metrics
/// (recall@k, precision@k, MRR) and labelled generation metrics
/// (groundedness, answer-relevance — LLM judge or deterministic heuristic).
/// </summary>
public interface IRagEvaluator
{
    /// <summary>Evaluates <paramref name="dataset"/> according to <paramref name="options"/>.</summary>
    Task<RagEvalReport> RunAsync(
        RagEvalDataset dataset,
        RagEvalOptions options,
        CancellationToken cancellationToken = default);
}
