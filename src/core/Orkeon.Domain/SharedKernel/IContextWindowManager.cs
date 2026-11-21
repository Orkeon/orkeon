namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Interface for managing LLM context window limits.
/// </summary>
/// <remarks>
/// Obsolete (#432): it summarises one string, not a conversation with tool turns. The agent loops
/// compact their conversation themselves (<c>Orkeon:Compaction</c>); this interface is removed in a
/// future major version.
/// </remarks>
[Obsolete("Summarises one string, not a conversation with tool turns: the agent loops compact their conversation themselves (Orkeon:Compaction, #432). Removed in a future major version.")]
public interface IContextWindowManager
{
    /// <summary>
    /// Summarizes content if it exceeds the maximum token limit.
    /// </summary>
    /// <param name="content">The content to potentially summarize.</param>
    /// <param name="maxTokens">The maximum allowed tokens.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The original content or a summarized version if needed.</returns>
    Task<string> SummarizeIfNeededAsync(string content, int maxTokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if content exceeds the context window limit.
    /// </summary>
    /// <param name="content">The content to check.</param>
    /// <param name="maxTokens">The maximum allowed tokens.</param>
    /// <returns>True if context is exceeded, false otherwise.</returns>
    bool IsContextExceeded(string content, int maxTokens);
}
