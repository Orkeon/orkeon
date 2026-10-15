using System.Text.Json.Serialization;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Mutable token usage class.
/// </summary>
public class TokenUsage : ITokenUsage
{
    /// <summary>
    /// Gets or sets the number of tokens in the prompt.
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of tokens in the completion/response.
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Gets or sets the total number of tokens used (prompt + completion).
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the estimated cost for this token usage.
    /// </summary>
    public decimal? EstimatedCost { get; set; }

    /// <summary>
    /// Gets or sets the model used for this operation.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the provider used (e.g., OpenAI, Anthropic).
    /// </summary>
    public string? Provider { get; set; }
}
