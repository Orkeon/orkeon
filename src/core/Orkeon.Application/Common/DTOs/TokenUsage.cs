using System.Text.Json.Serialization;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Mutable token usage class. Retained for backward compatibility.
/// Prefer <see cref="TokenUsageDto"/> for new code.
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

/// <summary>
/// Immutable token usage record. Consolidated from TokenUsageDto in ExecutionResultDto.
/// Implements <see cref="ITokenUsage"/> for interoperability with domain contracts.
/// </summary>
public sealed record TokenUsageDto : ITokenUsage
{
    /// <summary>
    /// Gets the number of tokens in the prompt.
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    /// <summary>
    /// Gets the number of tokens in the completion/response.
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Gets the total number of tokens used (prompt + completion).
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }

    /// <summary>
    /// Gets the estimated cost for this token usage.
    /// </summary>
    [JsonPropertyName("estimated_cost")]
    public decimal? EstimatedCost { get; init; }

    /// <summary>
    /// Gets the model used for this operation.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// Gets the provider used (e.g., OpenAI, Anthropic).
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>
    /// Token usage by model.
    /// </summary>
    [JsonPropertyName("model_usage")]
    public Dictionary<string, TokenModelUsageDto> ModelUsage { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for per-model token usage.
/// </summary>
public sealed record TokenModelUsageDto
{
    /// <summary>
    /// Model name.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Prompt tokens for this model.
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    /// <summary>
    /// Completion tokens for this model.
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Total tokens for this model.
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }

    /// <summary>
    /// Number of requests to this model.
    /// </summary>
    [JsonPropertyName("request_count")]
    public int RequestCount { get; init; }

    /// <summary>
    /// Estimated cost for this model.
    /// </summary>
    [JsonPropertyName("estimated_cost")]
    public decimal? EstimatedCost { get; init; }
}
