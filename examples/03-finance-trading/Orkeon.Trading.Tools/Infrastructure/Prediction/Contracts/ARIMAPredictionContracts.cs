using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Prediction.Contracts;

/// <summary>
/// Typed request for the ARIMAPredictionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record ARIMAPredictionRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public int ForecastPeriods { get; init; } = 5;
    public double ConfidenceLevel { get; init; } = 0.95;
    public bool AutoParams { get; init; } = true;
    public int P { get; init; } = 1;
    public int D { get; init; } = 1;
    public int Q { get; init; } = 1;
}

/// <summary>
/// Typed response from the ARIMAPredictionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record ARIMAPredictionResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object> ModelParameters { get; init; }
    public required List<Dictionary<string, object>> Forecast { get; init; }
    public required Dictionary<string, object> ModelDiagnostics { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required string ForecastDirection { get; init; }
    public required decimal ForecastChangePct { get; init; }
}
