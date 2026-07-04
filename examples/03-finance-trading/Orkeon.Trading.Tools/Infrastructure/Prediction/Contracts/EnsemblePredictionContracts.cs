using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;

/// <summary>
/// Typed request for the EnsemblePredictionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record EnsemblePredictionRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public int ForecastPeriods { get; init; } = 5;
    public List<string> ModelsToInclude { get; init; } = ["all"];
    public string WeightingMethod { get; init; } = "performance";
}

/// <summary>
/// Typed response from the EnsemblePredictionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record EnsemblePredictionResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string WeightingMethod { get; init; }
    public required List<string> ModelsIncluded { get; init; }
    public required List<Dictionary<string, object>> EnsembleForecast { get; init; }
    public required Dictionary<string, object> IndividualForecasts { get; init; }
    public required Dictionary<string, object> ModelWeights { get; init; }
    public required decimal ModelAgreement { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required string ForecastDirection { get; init; }
    public required string ConsensusStrength { get; init; }
}
