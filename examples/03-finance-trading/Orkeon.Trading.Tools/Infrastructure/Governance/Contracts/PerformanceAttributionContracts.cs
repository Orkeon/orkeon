namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the PerformanceAttributionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record PerformanceAttributionRequest
{
    public List<Dictionary<string, object>> Positions { get; init; } = [];
    public decimal TotalReturn { get; init; }
}

/// <summary>
/// Typed response from the PerformanceAttributionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record PerformanceAttributionResponse
{
    public required DateTime Timestamp { get; init; }
    public required decimal TotalReturnPct { get; init; }
    public required List<Dictionary<string, object>> Attributions { get; init; }
    public required List<Dictionary<string, object>> Top5Contributors { get; init; }
    public required List<Dictionary<string, object>> Top5Detractors { get; init; }
    public required int PositionsAnalyzed { get; init; }
}
