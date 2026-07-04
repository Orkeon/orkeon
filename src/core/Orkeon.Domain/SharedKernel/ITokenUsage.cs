namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Represents token usage information from an LLM provider.
/// Provides a strongly-typed contract for token metrics across
/// different provider implementations and DTOs.
/// </summary>
public interface ITokenUsage
{
    /// <summary>
    /// Gets the number of tokens in the prompt/input.
    /// </summary>
    int PromptTokens { get; }

    /// <summary>
    /// Gets the number of tokens in the completion/output.
    /// </summary>
    int CompletionTokens { get; }

    /// <summary>
    /// Gets the total number of tokens used (prompt + completion).
    /// </summary>
    int TotalTokens { get; }
}
