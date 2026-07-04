using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Contracts;

/// <summary>
/// Typed request for the HistoricalDataFetchTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record HistoricalDataFetchRequest
{
    public List<string> Symbols { get; init; } = [];
    public string StartDate { get; init; } = "";
    public string EndDate { get; init; } = "";
    public string Timeframe { get; init; } = "1d";
    public bool UseCache { get; init; } = true;
    public int CacheTtlHours { get; init; }
}

/// <summary>
/// Typed response from the HistoricalDataFetchTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record HistoricalDataFetchResponse
{
    public required Dictionary<string, MarketDataCollection> Data { get; init; }
    public required int TotalSymbols { get; init; }
    public required int SuccessfulSymbols { get; init; }
    public required int FailedSymbols { get; init; }
    public required int CacheHits { get; init; }
    public required int CacheMisses { get; init; }
    public required long TotalLatencyMs { get; init; }
    public required double AverageLatencyPerSymbolMs { get; init; }
    public required string Timeframe { get; init; }
    public required List<string> Errors { get; init; }
}
