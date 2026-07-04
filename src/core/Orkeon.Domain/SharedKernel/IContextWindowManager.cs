namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Interface for managing LLM context window limits.
/// </summary>
public interface IContextWindowManager
{
    /// <summary>
    /// Summarizes content if it exceeds the maximum token limit.
    /// </summary>
    /// <param name="content">The content to potentially summarize.</param>
    /// <param name="maxTokens">The maximum allowed tokens.</param>
    /// <returns>The original content or a summarized version if needed.</returns>
    Task<string> SummarizeIfNeededAsync(string content, int maxTokens);

    /// <summary>
    /// Checks if content exceeds the context window limit.
    /// </summary>
    /// <param name="content">The content to check.</param>
    /// <param name="maxTokens">The maximum allowed tokens.</param>
    /// <returns>True if context is exceeded, false otherwise.</returns>
    bool IsContextExceeded(string content, int maxTokens);
}
