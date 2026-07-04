using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Contracts;

/// <summary>
/// Typed request for the AlternativeDataTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record AlternativeDataRequest
{
    public string Symbol { get; init; } = "";
    public List<string> DataTypes { get; init; } = ["all"];
    public int LookbackDays { get; init; } = 7;
    public bool Aggregate { get; init; } = true;
}

/// <summary>
/// Typed response from the AlternativeDataTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record AlternativeDataResponse
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required int LookbackDays { get; init; }
    public required List<string> DataSources { get; init; }
    public required List<SentimentData> SentimentData { get; init; }
    public required int TotalSignals { get; init; }
    public required Dictionary<string, object> AggregatedSentiment { get; init; }
    public required Dictionary<string, object> Metadata { get; init; }
}
