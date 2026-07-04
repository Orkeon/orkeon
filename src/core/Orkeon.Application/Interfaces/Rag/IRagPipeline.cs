using Orkeon.Application.Rag;

namespace Orkeon.Application.Interfaces.Rag;

/// <summary>
/// Retrieval-Augmented Generation pipeline that combines retrieval, context augmentation,
/// and LLM generation to produce grounded answers.
/// </summary>
public interface IRagPipeline
{
    /// <summary>
    /// Executes the full RAG pipeline with a structured query.
    /// </summary>
    System.Threading.Tasks.Task<RagResult> ExecuteAsync(RagQuery query, CancellationToken ct = default);

    /// <summary>
    /// Executes the full RAG pipeline with a simple question string.
    /// </summary>
    System.Threading.Tasks.Task<RagResult> ExecuteAsync(string question, RagOptions? options = null, CancellationToken ct = default);
}
