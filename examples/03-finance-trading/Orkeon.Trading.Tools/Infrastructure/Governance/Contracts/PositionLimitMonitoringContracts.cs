namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the PositionLimitMonitoringTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record PositionLimitMonitoringRequest
{
    public Dictionary<string, object> Positions { get; init; } = new();
    public Dictionary<string, object> Limits { get; init; } = new();
}

/// <summary>
/// Typed response from the PositionLimitMonitoringTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record PositionLimitMonitoringResponse
{
    public required DateTime Timestamp { get; init; }
    public required int PositionsMonitored { get; init; }
    public required int LimitBreaches { get; init; }
    public required List<Dictionary<string, object>> Warnings { get; init; }
    public required string Status { get; init; }
}
