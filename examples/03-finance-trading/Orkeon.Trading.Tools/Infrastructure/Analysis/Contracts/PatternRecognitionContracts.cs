using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Analysis.Contracts;

/// <summary>
/// Typed request for the PatternRecognitionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record PatternRecognitionRequest
{
    public string Symbol { get; init; } = "";
    public List<MarketData> PriceData { get; init; } = [];
    public List<string> PatternTypes { get; init; } = ["all"];
    public double MinConfidence { get; init; } = 0.6;
}

/// <summary>
/// Typed response from the PatternRecognitionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record PatternRecognitionResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required List<DetectedPattern> Patterns { get; init; }
    public required int TotalPatterns { get; init; }
    public required Dictionary<string, object> PatternSummary { get; init; }
    public Dictionary<string, object>? PrimaryPattern { get; init; }
}
