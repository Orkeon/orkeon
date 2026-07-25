using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Turns the knowledge collections attached to an agent into a prompt-ready context
/// block with citations (RAG-03/C4). Called at task-execution-context assembly time:
/// the task input is embedded once, each attached collection is queried through the
/// <see cref="IDocumentStore"/> with the attachment's retrieval parameters
/// (TopK / MinScore / MaxContextTokens), and the retained chunks are assembled into
/// a bounded, numbered block. Retrieval only — no nested LLM generation.
/// </summary>
public interface IKnowledgeContextAugmenter
{
    /// <summary>
    /// Builds the knowledge context block for <paramref name="attachments"/> using
    /// <paramref name="taskInput"/> as the retrieval query.
    /// </summary>
    /// <param name="attachments">Knowledge attachments of the executing agent.</param>
    /// <param name="taskInput">Task input (description + execution context) used as the query text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The formatted block with its citations, or <c>null</c> when there is nothing
    /// to inject (no attachments, blank input, or no chunk retained after filtering).
    /// </returns>
    Task<KnowledgeContextBlock?> BuildContextAsync(
        IReadOnlyList<KnowledgeAttachment> attachments,
        string taskInput,
        CancellationToken cancellationToken = default);
}
