namespace Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;

/// <summary>
/// Typed request for the PortfolioRebalancingTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record PortfolioRebalancingRequest
{
    public Dictionary<string, PortfolioRebalancingTool.Position> CurrentPortfolio { get; init; } = new();
    public Dictionary<string, decimal> TargetWeights { get; init; } = new();
    public decimal PortfolioValue { get; init; }
    public double RebalancingThreshold { get; init; } = 0.05;
    public decimal CommissionPerTrade { get; init; } = 0.0m;
    public double TaxRate { get; init; } = 0.2;
    public bool ConsiderTaxLossHarvesting { get; init; }
    public int? MaxTrades { get; init; }
}

/// <summary>
/// Typed response from the PortfolioRebalancingTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record PortfolioRebalancingResponse
{
    public required DateTime Timestamp { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required double RebalancingThreshold { get; init; }
    public required int TradesCount { get; init; }
    public required List<Dictionary<string, object>> Trades { get; init; }
    public required decimal TotalTurnover { get; init; }
    public required Dictionary<string, object> EstimatedCosts { get; init; }
    public required Dictionary<string, object> ExecutionPlan { get; init; }
    public required Dictionary<string, decimal> CurrentWeights { get; init; }
    public required Dictionary<string, decimal> TargetWeights { get; init; }
}
