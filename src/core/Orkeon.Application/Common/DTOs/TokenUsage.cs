using System.Text.Json.Serialization;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Token usage of one operation: prompt, completion and total tokens, with the model, provider and estimated cost.
/// </summary>
public sealed record TokenUsage : ITokenUsage
{
    /// <summary>
    /// Gets the number of tokens in the prompt.
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Gets the number of tokens in the completion/response.
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Gets the total number of tokens used (prompt + completion).
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Gets the estimated cost for this token usage.
    /// </summary>
    public decimal? EstimatedCost { get; init; }

    /// <summary>
    /// Gets the model used for this operation.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the provider used (e.g., OpenAI, Anthropic).
    /// </summary>
    public string? Provider { get; init; }
}
