using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IContextAugmenter for testing.
/// </summary>
public sealed class MockContextAugmenter : IContextAugmenter
{
    private AugmentedPrompt _augmentResult = new()
    {
        SystemPrompt = "You are a helpful assistant.",
        UserPrompt = "mock augmented prompt",
        UsedChunks = Array.Empty<RetrievedChunk>(),
        EstimatedTokens = 50
    };

    private Func<string, IReadOnlyList<RetrievedChunk>, AugmentationOptions, AugmentedPrompt>? _augmentFunc;

    // --- Tracking ---
    public int AugmentCallCount { get; private set; }
    public string? LastAugmentQuestion { get; private set; }
    public IReadOnlyList<RetrievedChunk>? LastAugmentChunks { get; private set; }
    public AugmentationOptions? LastAugmentOptions { get; private set; }

    // --- Configuration ---
    public void SetAugmentResult(AugmentedPrompt result) => _augmentResult = result;
    public void SetAugmentFunc(
        Func<string, IReadOnlyList<RetrievedChunk>, AugmentationOptions, AugmentedPrompt> func) =>
        _augmentFunc = func;

    public System.Threading.Tasks.Task<AugmentedPrompt> AugmentAsync(
        string question,
        IReadOnlyList<RetrievedChunk> chunks,
        AugmentationOptions options,
        CancellationToken ct = default)
    {
        AugmentCallCount++;
        LastAugmentQuestion = question;
        LastAugmentChunks = chunks;
        LastAugmentOptions = options;

        var result = _augmentFunc != null ? _augmentFunc(question, chunks, options) : _augmentResult;
        return System.Threading.Tasks.Task.FromResult(result);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        AugmentCallCount = 0;
        LastAugmentQuestion = null;
        LastAugmentChunks = null;
        LastAugmentOptions = null;
    }
}
