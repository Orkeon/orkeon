using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Opt-in capability of an <see cref="IRagPipeline"/> that can run its retrieval
/// half alone — transform → retrieve → fuse → rerank → assemble — and hand back
/// the selected passages WITHOUT paying for the generation stage.
/// </summary>
/// <remarks>
/// <para>Declared as a separate interface rather than added to
/// <see cref="IRagPipeline"/> for the same reason as
/// <c>IHybridSearchCapable</c> (Orkeon.Domain.Memory): not every executor
/// can honour it (the corrective graph interleaves evaluation with generation, so
/// "retrieval only" is not a prefix of its run), and a caller must be able to ASK
/// whether the capability is there instead of discovering it through a
/// <see cref="NotSupportedException"/>.</para>
/// <para><b>Why it exists.</b> A caller that wants the evidence rather than prose
/// was still charged for a full grounded generation. Measured on a real agent
/// run (2026-08-04): seven <c>rag.query</c> calls whose generated
/// answers were discarded by design cost 14 748 completion tokens — 68 % of them
/// reasoning tokens — and 394 s of wall time, on top of retrieval that had
/// already produced every citation the caller used. The generation stage is the
/// expensive half of a RAG call and it is optional far more often than the API
/// shape suggested.</para>
/// <para>The returned <see cref="RagAnswer"/> keeps the same shape as a full
/// query so a caller can switch between the two without reshaping its code:
/// <see cref="RagAnswer.Text"/> is empty, <see cref="RagAnswer.Citations"/> holds
/// the assembled passages, and the trace carries a <c>generate</c> step whose
/// detail says the stage was skipped on purpose — never silently absent.</para>
/// </remarks>
public interface IRagRetrievalCapable
{
    /// <summary>
    /// Retrieves and assembles the passages for <paramref name="query"/> without
    /// generating an answer.
    /// </summary>
    /// <param name="query">Question and target collection; <c>TopN</c> honoured as in a full query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An answer whose <see cref="RagAnswer.Text"/> is empty and whose
    /// <see cref="RagAnswer.Citations"/> are the passages that a full query would
    /// have grounded on.
    /// </returns>
    Task<RagAnswer> RetrieveAsync(
        RagQuery query,
        CancellationToken cancellationToken = default);
}
