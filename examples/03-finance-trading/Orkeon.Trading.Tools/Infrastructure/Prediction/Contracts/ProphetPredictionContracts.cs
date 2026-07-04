using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;

/// <summary>
/// Typed request for the ProphetPredictionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record ProphetPredictionRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public int ForecastPeriods { get; init; } = 5;
    public string SeasonalityMode { get; init; } = "additive";
    public bool IncludeWeeklySeasonality { get; init; } = true;
    public bool IncludeYearlySeasonality { get; init; }
    public double ChangepointPriorScale { get; init; } = 0.05;
}

/// <summary>
/// Typed response from the ProphetPredictionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record ProphetPredictionResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object> ModelConfig { get; init; }
    public required List<Dictionary<string, object>> Forecast { get; init; }
    public required Dictionary<string, object> Components { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required string ForecastDirection { get; init; }
    public required string TrendStrength { get; init; }
    public required string SeasonalityStrength { get; init; }
}
