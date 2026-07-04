namespace Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;

/// <summary>
/// Typed request for the SmartOrderRoutingTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record SmartOrderRoutingRequest
{
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Quantity { get; init; }
    public string OrderType { get; init; } = "LIMIT";
    public decimal? LimitPrice { get; init; }
    public string OptimizationObjective { get; init; } = "best_price";
    public List<string>? AvailableVenues { get; init; }
}

/// <summary>
/// Typed response from the SmartOrderRoutingTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record SmartOrderRoutingResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Algorithm { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required string OrderType { get; init; }
    public required string OptimizationObjective { get; init; }
    public required int TotalVenues { get; init; }
    public required List<Dictionary<string, object>> RoutingPlan { get; init; }
    public required Dictionary<string, object> ExpectedCosts { get; init; }
    public required Dictionary<string, string> ExecutionStrategy { get; init; }
    public decimal? LimitPrice { get; init; }
}
