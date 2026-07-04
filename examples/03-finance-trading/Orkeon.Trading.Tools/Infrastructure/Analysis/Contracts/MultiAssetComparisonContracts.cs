using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;

/// <summary>
/// Typed request for the MultiAssetComparisonTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record MultiAssetComparisonRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<MarketData>> PriceData { get; init; } = new();
    public string ComparisonPeriod { get; init; } = "1Y";
    public List<string> Metrics { get; init; } = ["all"];
}

/// <summary>
/// Typed response from the MultiAssetComparisonTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record MultiAssetComparisonResponse
{
    public required DateTime Timestamp { get; init; }
    public required List<string> Symbols { get; init; }
    public required string ComparisonPeriod { get; init; }
    public required Dictionary<string, object> ComparisonTable { get; init; }
    public required Dictionary<string, object> Rankings { get; init; }
    public required Dictionary<string, object> BestPerformers { get; init; }
    public required Dictionary<string, object> CorrelationMatrix { get; init; }
    public required Dictionary<string, object> SummaryStatistics { get; init; }
}
