using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;

/// <summary>
/// Typed request for the MarketRegimeClassificationTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record MarketRegimeClassificationRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public int LookbackDays { get; init; } = 60;
}

/// <summary>
/// Typed response from the MarketRegimeClassificationTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record MarketRegimeClassificationResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string CurrentRegime { get; init; }
    public required double Confidence { get; init; }
    public required Dictionary<string, decimal> RegimeProbabilities { get; init; }
    public required Dictionary<string, object> Metrics { get; init; }
    public required List<string> Characteristics { get; init; }
    public required List<string> TradingImplications { get; init; }
}
