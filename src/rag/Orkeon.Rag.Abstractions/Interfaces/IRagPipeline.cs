using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Query façade of the RAG subsystem: answers a <see cref="RagQuery"/> with a
/// cited, traced <see cref="RagAnswer"/>. The linear pipeline and the corrective
/// graph engine both live behind this interface; the configured profile selects
/// the executor.
/// </summary>
public interface IRagPipeline
{
    /// <summary>Answers <paramref name="query"/> from the configured collection(s).</summary>
    Task<RagAnswer> QueryAsync(
        RagQuery query,
        CancellationToken cancellationToken = default);
}
