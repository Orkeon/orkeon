namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the DrawdownMonitoringTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record DrawdownMonitoringRequest
{
    public List<decimal> PortfolioValues { get; init; } = [];
    public double MaxDrawdownLimit { get; init; } = 0.15;
}

/// <summary>
/// Typed response from the DrawdownMonitoringTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record DrawdownMonitoringResponse
{
    public required DateTime Timestamp { get; init; }
    public required decimal CurrentDrawdownPct { get; init; }
    public required decimal MaxDrawdownPct { get; init; }
    public required double LimitPct { get; init; }
    public required bool LimitBreached { get; init; }
    public required string AlertLevel { get; init; }
    public required int DaysAnalyzed { get; init; }
}
