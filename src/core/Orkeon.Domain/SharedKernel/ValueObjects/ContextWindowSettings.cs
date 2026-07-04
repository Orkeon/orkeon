using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Settings for context window management.
/// </summary>
public sealed record ContextWindowSettings : ValueObjectRecord
{
    /// <summary>Gets the maximum number of tokens.</summary>
    public int MaxTokens { get; init; } = LlmDefaults.DefaultContextWindowTokens;
    /// <summary>Gets a value indicating whether auto-summarization is enabled.</summary>
    public bool AutoSummarize { get; init; } = true;
    /// <summary>Gets the compression ratio (0.0 to 1.0).</summary>
    public double CompressionRatio { get; init; } = 0.5;
    /// <summary>Gets the summary prompt template.</summary>
    public string SummaryPrompt { get; init; } = "Summarize the following while preserving key information:";

    /// <summary>Gets a default instance with standard values.</summary>
    public static ContextWindowSettings Default => new();

    /// <summary>Creates a new <see cref="ContextWindowSettings"/> with the specified values.</summary>
    public static ContextWindowSettings Create(
        int maxTokens = LlmDefaults.DefaultContextWindowTokens,
        bool autoSummarize = true,
        double compressionRatio = 0.5,
        string summaryPrompt = "Summarize the following while preserving key information:")
        => new(maxTokens, autoSummarize, compressionRatio, summaryPrompt);

    /// <summary>Initializes a new <see cref="ContextWindowSettings"/> with default values.</summary>
    private ContextWindowSettings() { }

    /// <summary>Initializes a new <see cref="ContextWindowSettings"/> with the specified values.</summary>
    private ContextWindowSettings(
        int MaxTokens,
        bool AutoSummarize,
        double CompressionRatio,
        string SummaryPrompt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTokens);
        if (CompressionRatio < 0.0 || CompressionRatio > 1.0)
            throw new ArgumentOutOfRangeException(nameof(CompressionRatio),
                $"CompressionRatio must be between 0.0 and 1.0, but was {CompressionRatio}.");
        ArgumentException.ThrowIfNullOrWhiteSpace(SummaryPrompt);

        this.MaxTokens = MaxTokens;
        this.AutoSummarize = AutoSummarize;
        this.CompressionRatio = CompressionRatio;
        this.SummaryPrompt = SummaryPrompt;
    }

    /// <summary>Deconstructs this instance into its components.</summary>
    public void Deconstruct(out int maxTokens, out bool autoSummarize, out double compressionRatio, out string summaryPrompt)
    {
        maxTokens = MaxTokens;
        autoSummarize = AutoSummarize;
        compressionRatio = CompressionRatio;
        summaryPrompt = SummaryPrompt;
    }
}
