namespace Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;

/// <summary>
/// Typed request for the HierarchicalRiskParityTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record HierarchicalRiskParityRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<double>> ReturnsData { get; init; } = new();
    public string LinkageMethod { get; init; } = "ward";
    public double RiskFreeRate { get; init; } = 0.02;
}

/// <summary>
/// Typed response from the HierarchicalRiskParityTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record HierarchicalRiskParityResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Method { get; init; }
    public required List<string> Symbols { get; init; }
    public required Dictionary<string, decimal> OptimalWeights { get; init; }
    public required decimal ExpectedReturn { get; init; }
    public required decimal ExpectedVolatility { get; init; }
    public required decimal SharpeRatio { get; init; }
    public required Dictionary<string, object> ClusterStructure { get; init; }
    public required List<string> SortedOrder { get; init; }
    public required string LinkageMethod { get; init; }
    public required decimal DiversificationRatio { get; init; }
}
