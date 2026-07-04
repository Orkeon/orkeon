using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;

/// <summary>
/// Typed request for the CorrelationAnalysisTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record CorrelationAnalysisRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<MarketData>> PriceData { get; init; } = new();
    public string BenchmarkSymbol { get; init; } = "SPY";
    public int LookbackDays { get; init; } = 252;
}

/// <summary>
/// Typed response from the CorrelationAnalysisTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record CorrelationAnalysisResponse
{
    public required DateTime Timestamp { get; init; }
    public required List<string> Symbols { get; init; }
    public required int LookbackDays { get; init; }
    public required Dictionary<string, Dictionary<string, decimal>> CorrelationMatrix { get; init; }
    public required Dictionary<string, decimal> BetaValues { get; init; }
    public required string BenchmarkSymbol { get; init; }
    public required Dictionary<string, object> DiversificationMetrics { get; init; }
    public required List<List<string>> CorrelationClusters { get; init; }
}
