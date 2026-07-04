namespace Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;

/// <summary>
/// Typed request for the MeanVarianceOptimizationTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record MeanVarianceOptimizationRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<double>> ReturnsData { get; init; } = new();
    public double RiskFreeRate { get; init; } = 0.02;
    public double? TargetReturn { get; init; }
    public double? TargetVolatility { get; init; }
    public double MinWeight { get; init; }
    public double MaxWeight { get; init; } = 1.0;
    public Dictionary<string, double>? CurrentWeights { get; init; }
    public double? MaxTurnover { get; init; }
}

/// <summary>
/// Typed response from the MeanVarianceOptimizationTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record MeanVarianceOptimizationResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Method { get; init; }
    public required List<string> Symbols { get; init; }
    public required Dictionary<string, decimal> OptimalWeights { get; init; }
    public required decimal ExpectedReturn { get; init; }
    public required decimal ExpectedVolatility { get; init; }
    public required decimal SharpeRatio { get; init; }
    public required double RiskFreeRate { get; init; }
    public required Dictionary<string, object> AlternativePortfolios { get; init; }
    public required List<Dictionary<string, object>> EfficientFrontier { get; init; }
    public required Dictionary<string, object> DiversificationMetrics { get; init; }
    public required List<string> ConstraintsApplied { get; init; }
}
