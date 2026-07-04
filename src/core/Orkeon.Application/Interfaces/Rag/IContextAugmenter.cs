using Orkeon.Application.Rag;

namespace Orkeon.Application.Interfaces.Rag;

/// <summary>
/// Augments a user question with retrieved context chunks to form a complete prompt.
/// </summary>
public interface IContextAugmenter
{
    /// <summary>
    /// Builds an augmented prompt by combining the question with relevant chunks.
    /// </summary>
    System.Threading.Tasks.Task<AugmentedPrompt> AugmentAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        AugmentationOptions options,
        CancellationToken ct = default);
}
