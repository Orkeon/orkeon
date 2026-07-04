namespace Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;

/// <summary>
/// Typed request for the ConstraintOptimizationTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record ConstraintOptimizationRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<double>> ReturnsData { get; init; } = new();
    public Dictionary<string, object> Constraints { get; init; } = new();
    public Dictionary<string, Dictionary<string, object>>? AssetMetadata { get; init; }
    public Dictionary<string, double>? BenchmarkWeights { get; init; }
    public Dictionary<string, double>? CurrentWeights { get; init; }
    public string OptimizationObjective { get; init; } = "maximize_sharpe";
}

/// <summary>
/// Typed response from the ConstraintOptimizationTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record ConstraintOptimizationResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Method { get; init; }
    public required string Objective { get; init; }
    public required List<string> Symbols { get; init; }
    public required Dictionary<string, decimal> OptimalWeights { get; init; }
    public required decimal ExpectedReturn { get; init; }
    public required decimal ExpectedVolatility { get; init; }
    public required decimal SharpeRatio { get; init; }
    public required List<string> ConstraintsApplied { get; init; }
    public required Dictionary<string, object> ConstraintsSatisfied { get; init; }
    public decimal? TrackingError { get; init; }
    public decimal? PortfolioTurnover { get; init; }
}
