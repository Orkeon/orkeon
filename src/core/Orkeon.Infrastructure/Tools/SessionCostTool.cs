using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="SessionCostTool"/> (no parameters).</summary>
public sealed class SessionCostRequest
{
}

/// <summary>Typed response for <see cref="SessionCostTool"/>.</summary>
public sealed class SessionCostResponse
{
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
}

/// <summary>
/// Reports cumulative session cost (USD), token usage, and per-model breakdown (exp 07 SPEC §7.3;
/// backs <c>/cost</c>). Backed by <see cref="ICostBudgetManager"/>; reports zeros when unwired.
/// </summary>
public sealed class SessionCostTool : ToolBase<SessionCostRequest, SessionCostResponse>
{
    /// <inheritdoc />
    public override string Name => "session_cost";
    /// <inheritdoc />
    public override string Description =>
        "Report cumulative session cost in USD, token usage, and a per-model breakdown.";

    private readonly ICostBudgetManager? _costManager;

    /// <summary>Creates the tool with its backing service(s).</summary>
    public SessionCostTool(ICostBudgetManager? costManager = null)
    {
        _costManager = costManager;
    }

    /// <inheritdoc />
    protected override Task<SessionCostResponse> ExecuteTypedAsync(
        SessionCostRequest request, CancellationToken cancellationToken)
    {
        if (_costManager is null)
            return Task.FromResult(new SessionCostResponse());

        var report = _costManager.GetReport();
        return Task.FromResult(new SessionCostResponse
        {
            TotalCostUsd = report.TotalCost,
            TotalTokens = report.TotalTokens,
            TotalCalls = report.TotalCalls,
            ByModel = new Dictionary<string, decimal>(report.ByModel),
        });
    }
}
