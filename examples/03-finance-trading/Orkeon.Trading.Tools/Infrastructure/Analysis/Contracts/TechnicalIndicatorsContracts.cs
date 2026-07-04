using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;

/// <summary>
/// Typed request for the TechnicalIndicatorsTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record TechnicalIndicatorsRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public List<string>? Indicators { get; init; }
}

/// <summary>
/// Typed response from the TechnicalIndicatorsTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record TechnicalIndicatorsResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required Dictionary<string, object> Indicators { get; init; }
    public required Dictionary<string, object> Signals { get; init; }
    public required Dictionary<string, object> Metadata { get; init; }
}
