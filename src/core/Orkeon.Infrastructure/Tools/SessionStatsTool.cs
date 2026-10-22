using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="SessionStatsTool"/> (no parameters).</summary>
public sealed class SessionStatsRequest
{
}

/// <summary>Typed response for <see cref="SessionStatsTool"/>.</summary>
public sealed class SessionStatsResponse
{
    /// <summary>Messages in the session buffer.</summary>
    [JsonPropertyName("message_count")]
    public int MessageCount { get; set; }

    /// <summary>Estimated tokens in the buffer.</summary>
    [JsonPropertyName("estimated_tokens")]
    public int EstimatedTokens { get; set; }

    /// <summary>Cumulative cost in USD.</summary>
    [JsonPropertyName("total_cost_usd")]
    public decimal TotalCostUsd { get; set; }

    /// <summary>Total tokens consumed.</summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    /// <summary>Total LLM calls.</summary>
    [JsonPropertyName("total_calls")]
    public int TotalCalls { get; set; }

    /// <summary>Cost breakdown by model.</summary>
    [JsonPropertyName("by_model")]
    public Dictionary<string, decimal> ByModel { get; init; } = new();

    /// <summary>Token breakdown by model, cache write and read included.</summary>
    [JsonPropertyName("by_model_tokens")]
    public IReadOnlyList<ModelTokensEntry> ByModelTokens { get; init; } = [];
}

/// <summary>Token usage of one model in <c>session_stats</c>.</summary>
public sealed class ModelTokensEntry
{
    /// <summary>Model name.</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>Prompt tokens.</summary>
    [JsonPropertyName("prompt_tokens")]
    public long PromptTokens { get; init; }

    /// <summary>Completion tokens.</summary>
    [JsonPropertyName("completion_tokens")]
    public long CompletionTokens { get; init; }

    /// <summary>Cache-write tokens.</summary>
    [JsonPropertyName("cache_write_tokens")]
    public long CacheWriteTokens { get; init; }

    /// <summary>Cache-read tokens.</summary>
    [JsonPropertyName("cache_read_tokens")]
    public long CacheReadTokens { get; init; }
}

/// <summary>
/// Full session telemetry — conversation size plus cost/token/call totals (backs
/// <c>/stats</c>). Combines <see cref="ISessionBufferService"/> with the optional
/// <see cref="ICostBudgetManager"/>.
/// </summary>
public sealed class SessionStatsTool : ToolBase<SessionStatsRequest, SessionStatsResponse>
{
    /// <inheritdoc />
    public override string Name => "session_stats";
    /// <inheritdoc />
    public override string Description =>
        "Report full session telemetry: message count, estimated tokens, cost, and call totals.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly ISessionBufferService _buffer;
    private readonly ICostBudgetManager? _costManager;

    /// <summary>Creates the tool with its backing service(s).</summary>
    public SessionStatsTool(ISessionBufferService buffer, ICostBudgetManager? costManager = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _costManager = costManager;
    }

    /// <inheritdoc />
    protected override Task<SessionStatsResponse> ExecuteTypedAsync(
        SessionStatsRequest request, CancellationToken cancellationToken)
    {
        var report = _costManager?.GetReport();
        var response = new SessionStatsResponse
        {
            MessageCount = _buffer.MessageCount,
            EstimatedTokens = _buffer.EstimateTokenCount(),
            TotalCostUsd = report?.TotalCost ?? 0m,
            TotalTokens = report?.TotalTokens ?? 0,
            TotalCalls = report?.TotalCalls ?? 0,
            ByModel = report is not null
                ? new Dictionary<string, decimal>(report.ByModel)
                : new Dictionary<string, decimal>(),
            ByModelTokens = report is null
                ? []
                : report.ModelUsageByModel.Select(kv => new ModelTokensEntry
                {
                    Model = kv.Key,
                    PromptTokens = kv.Value.PromptTokens,
                    CompletionTokens = kv.Value.CompletionTokens,
                    CacheWriteTokens = kv.Value.CacheWriteTokens,
                    CacheReadTokens = kv.Value.CacheReadTokens,
                }).ToList(),
        };

        return Task.FromResult(response);
    }
}
