using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;

/// <summary>
/// Typed request for the BacktestingTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record BacktestingRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public List<int> StrategySignals { get; init; } = [];
    public decimal InitialCapital { get; init; } = 100000m;
    public double PositionSize { get; init; } = 1.0;
    public double CommissionPct { get; init; } = 0.001;
    public double SlippagePct { get; init; } = 0.0005;
    public bool AllowShort { get; init; }
}

/// <summary>
/// Typed response from the BacktestingTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record BacktestingResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object> BacktestConfig { get; init; }
    public required Dictionary<string, object> PerformanceMetrics { get; init; }
    public required Dictionary<string, object> TradeStatistics { get; init; }
    public required List<Dictionary<string, object>> EquityCurve { get; init; }
    public required decimal FinalPortfolioValue { get; init; }
    public required string StrategyQuality { get; init; }
}
