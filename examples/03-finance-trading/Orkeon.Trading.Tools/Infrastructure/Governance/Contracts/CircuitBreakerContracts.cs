namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the CircuitBreakerTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record CircuitBreakerRequest
{
    public Dictionary<string, object> PortfolioMetrics { get; init; } = new();
    public Dictionary<string, object> RiskLimits { get; init; } = new();
}

/// <summary>
/// Typed response from the CircuitBreakerTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record CircuitBreakerResponse
{
    public required DateTime Timestamp { get; init; }
    public required bool BreakerTriggered { get; init; }
    public required List<Dictionary<string, object>> Violations { get; init; }
    public required string Action { get; init; }
    public required string Message { get; init; }
}
