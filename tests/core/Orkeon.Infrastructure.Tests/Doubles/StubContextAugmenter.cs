using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Minimal hand-written stub of <see cref="IContextAugmenter"/>: echoes the question and
/// forwards every retrieved chunk unmodified. Used to run a real <see cref="RagPipeline"/>
/// in tests without LLM concerns.
/// </summary>
public sealed class StubContextAugmenter : IContextAugmenter
{
    public System.Threading.Tasks.Task<AugmentedPrompt> AugmentAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        AugmentationOptions options,
        CancellationToken ct = default)
    {
        return System.Threading.Tasks.Task.FromResult(new AugmentedPrompt
        {
            SystemPrompt = "stub",
            UserPrompt = question,
            UsedChunks = chunks,
        });
    }
}
