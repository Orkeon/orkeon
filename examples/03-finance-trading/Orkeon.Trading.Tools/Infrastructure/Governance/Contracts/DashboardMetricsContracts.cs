namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the DashboardMetricsTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record DashboardMetricsRequest
{
    public Dictionary<string, object> PortfolioData { get; init; } = new();
    public Dictionary<string, object> MarketData { get; init; } = new();
}

/// <summary>
/// Typed response from the DashboardMetricsTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record DashboardMetricsResponse
{
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object> Portfolio { get; init; }
    public required Dictionary<string, object> Risk { get; init; }
    public required Dictionary<string, object> Trading { get; init; }
    public required Dictionary<string, object> Alerts { get; init; }
    public Dictionary<string, object>? Market { get; init; }
}
