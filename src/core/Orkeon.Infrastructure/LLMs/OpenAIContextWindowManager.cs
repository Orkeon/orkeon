using Microsoft.Extensions.Logging;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// OpenAI-based context window manager implementation.
/// </summary>
public sealed partial class OpenAIContextWindowManager : IContextWindowManager
{
    private readonly ITokenCounter _tokenCounter;
    private readonly ILlmProvider _llm;
    private readonly ILogger<OpenAIContextWindowManager> _logger;
    private readonly ContextWindowSettings _settings;

    /// <summary>Initializes a new instance of <see cref="OpenAIContextWindowManager"/>.</summary>
    /// <param name="tokenCounter">The token counter used to estimate token usage.</param>
    /// <param name="llm">The LLM provider used for summarization.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="settings">Optional context window settings.</param>
    public OpenAIContextWindowManager(
        ITokenCounter tokenCounter,
        ILlmProvider llm,
        ILogger<OpenAIContextWindowManager> logger,
        ContextWindowSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);
        _tokenCounter = tokenCounter;
        ArgumentNullException.ThrowIfNull(llm);
        _llm = llm;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _settings = settings ?? ContextWindowSettings.Default;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Summarization fault barrier: if the LLM summarization call fails the content is deterministically truncated to the token limit so context-window management never throws.")]
    public Task<string> SummarizeIfNeededAsync(string content, int maxTokens)
    {
        // Handle null content
        ArgumentNullException.ThrowIfNull(content);

        // Handle zero or negative maxTokens
        if (maxTokens <= 0)
        {
            return Task.FromResult(TruncateToTokenLimit(content, maxTokens));
        }

        var tokenCount = _tokenCounter.CountTokens(content);
        if (tokenCount <= maxTokens)
        {
            LogContentWithinTokenLimit(tokenCount, maxTokens);
            return Task.FromResult(content);
        }

        if (!_settings.AutoSummarize)
        {
            LogContentExceedsTokenLimitBut();
            return Task.FromResult(content);
        }

        var targetTokens = (int)(maxTokens * _settings.CompressionRatio);
        LogSummarizingContentFromToTokens(tokenCount, targetTokens);

        return SummarizeIfNeededCoreAsync(content, maxTokens, targetTokens);

        async Task<string> SummarizeIfNeededCoreAsync(string content, int maxTokens, int targetTokens)
        {
            var prompt = $"{_settings.SummaryPrompt}\n\nTarget length: approximately {targetTokens} tokens\n\nContent:\n{content}";

            try
            {
                var config = LlmConfig.Default() with
                {
                    Temperature = 0.3, // Lower temperature for more consistent summaries
                    MaxTokens = targetTokens
                };

                var response = await _llm.GenerateAsync(prompt, config).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    LogSuccessfullySummarizedContent();
                    return response.Content;
                }
                else
                {
                    LogSummarizationReturnedEmptyContentReturning();
                    return TruncateToTokenLimit(content, maxTokens);
                }
            }
            catch (Exception ex)
            {
                LogErrorDuringSummarizationReturningTruncated(ex);
                return TruncateToTokenLimit(content, maxTokens);
            }
        }
    }

    /// <inheritdoc />
    public bool IsContextExceeded(string content, int maxTokens)
    {
        // Handle null content
        ArgumentNullException.ThrowIfNull(content);

        var tokenCount = _tokenCounter.CountTokens(content);
        return tokenCount > maxTokens;
    }

    private static string TruncateToTokenLimit(string content, int maxTokens)
    {
        // Handle zero or negative maxTokens - return just ellipsis
        if (maxTokens <= 0)
            return "...";

        // Simple truncation by character approximation
        var avgCharsPerToken = 4; // Rough approximation
        var maxChars = maxTokens * avgCharsPerToken;

        if (content.Length <= maxChars)
            return content;

        return string.Concat(content.AsSpan(0, maxChars - 3), "...");
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Content within token limit ({TokenCount}/{MaxTokens})")]
    private partial void LogContentWithinTokenLimit(int tokenCount, int maxTokens);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Content exceeds token limit but auto-summarize is disabled")]
    private partial void LogContentExceedsTokenLimitBut();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Summarizing content from {TokenCount} to ~{TargetTokens} tokens")]
    private partial void LogSummarizingContentFromToTokens(int tokenCount, int targetTokens);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Successfully summarized content")]
    private partial void LogSuccessfullySummarizedContent();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Summarization returned empty content, returning truncated content")]
    private partial void LogSummarizationReturnedEmptyContentReturning();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error during summarization, returning truncated content")]
    private partial void LogErrorDuringSummarizationReturningTruncated(Exception ex);

}
