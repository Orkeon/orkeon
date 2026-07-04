using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Data.Contracts;

/// <summary>
/// Typed request for the RealTimeTickDataTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record RealTimeTickDataRequest
{
    public string Symbol { get; init; } = "";
    public int DurationSeconds { get; init; } = 60;
    public int MaxTicks { get; init; } = 1000;
    public bool Aggregate { get; init; }
}

/// <summary>
/// Typed response from the RealTimeTickDataTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record RealTimeTickDataResponse
{
    public required string Symbol { get; init; }
    public required List<TickData> Ticks { get; init; }
    public required int TotalTicks { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required double DurationMs { get; init; }
    public required double AverageTps { get; init; }
    public required Dictionary<string, object> Summary { get; init; }
}
