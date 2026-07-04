namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the TransactionCostAnalysisTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record TransactionCostAnalysisRequest
{
    public List<Dictionary<string, object>> Trades { get; init; } = [];
}

/// <summary>
/// Typed response from the TransactionCostAnalysisTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record TransactionCostAnalysisResponse
{
    public required DateTime Timestamp { get; init; }
    public required int TradesAnalyzed { get; init; }
    public required decimal TotalCommission { get; init; }
    public required decimal TotalSlippage { get; init; }
    public required decimal TotalCost { get; init; }
    public required decimal AverageCostBps { get; init; }
    public required List<Dictionary<string, object>> TradeCosts { get; init; }
}
