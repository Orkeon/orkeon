using Orkeon.Application.Rag;

namespace Orkeon.Application.Interfaces.Rag;

/// <summary>
/// Generates a response from an LLM using an augmented prompt.
/// </summary>
public interface IResponseGenerator
{
    /// <summary>
    /// Generates a response using the augmented prompt.
    /// </summary>
    System.Threading.Tasks.Task<GeneratedResponse> GenerateAsync(
        AugmentedPrompt prompt,
        GenerationOptions options,
        CancellationToken ct = default);
}
