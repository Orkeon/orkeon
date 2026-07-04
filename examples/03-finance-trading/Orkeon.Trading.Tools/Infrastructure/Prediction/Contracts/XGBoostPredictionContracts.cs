using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;

/// <summary>
/// Typed request for the XGBoostPredictionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record XGBoostPredictionRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public int ForecastPeriods { get; init; } = 5;
    public int NEstimators { get; init; } = 100;
    public double LearningRate { get; init; } = 0.1;
    public int MaxDepth { get; init; } = 6;
    public string FeatureSet { get; init; } = "comprehensive";
}

/// <summary>
/// Typed response from the XGBoostPredictionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record XGBoostPredictionResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object> ModelConfig { get; init; }
    public required List<Dictionary<string, object>> Forecast { get; init; }
    public required Dictionary<string, decimal> FeatureImportance { get; init; }
    public required Dictionary<string, object> ModelMetrics { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required string ForecastDirection { get; init; }
    public required object Confidence { get; init; }
}
