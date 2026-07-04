namespace Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;

/// <summary>
/// Typed request for the ImplementationShortfallTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record ImplementationShortfallRequest
{
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal DecisionPrice { get; init; }
    public string Urgency { get; init; } = "medium";
    public double AlphaForecast { get; init; }
    public double Volatility { get; init; } = 0.02;
    public double RiskAversion { get; init; } = 0.000001;
}

/// <summary>
/// Typed response from the ImplementationShortfallTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record ImplementationShortfallResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Algorithm { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required decimal DecisionPrice { get; init; }
    public required string Urgency { get; init; }
    public required double AlphaForecast { get; init; }
    public required double FrontLoadFactor { get; init; }
    public required int TotalSlices { get; init; }
    public required int ExecutionDurationMinutes { get; init; }
    public required List<Dictionary<string, object>> ExecutionSchedule { get; init; }
    public required Dictionary<string, object> ExpectedCosts { get; init; }
    public required Dictionary<string, string> ExecutionStrategy { get; init; }
}
