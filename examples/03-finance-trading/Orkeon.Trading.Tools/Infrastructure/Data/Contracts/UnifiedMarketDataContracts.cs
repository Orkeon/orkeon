using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Contracts;

/// <summary>
/// Typed request for the UnifiedMarketDataTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record UnifiedMarketDataRequest
{
    public string Symbol { get; init; } = "";
    public string StartDate { get; init; } = "";
    public string EndDate { get; init; } = "";
    public string Timeframe { get; init; } = "1d";
    public bool IncludeVwap { get; init; } = true;
}

/// <summary>
/// Typed response from the UnifiedMarketDataTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record UnifiedMarketDataResponse
{
    public required string Symbol { get; init; }
    public required List<MarketData> Data { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required string Timeframe { get; init; }
    public required string DataSource { get; init; }
    public required int TotalPoints { get; init; }
    public required long LatencyMs { get; init; }
    public required decimal QualityScore { get; init; }
    public required Dictionary<string, object> Metadata { get; init; }
}
