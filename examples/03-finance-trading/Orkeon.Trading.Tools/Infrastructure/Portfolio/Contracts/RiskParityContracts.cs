namespace Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;

/// <summary>
/// Typed request for the RiskParityTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record RiskParityRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<double>> ReturnsData { get; init; } = new();
    public Dictionary<string, double>? TargetRiskContributions { get; init; }
    public double RiskFreeRate { get; init; } = 0.02;
}

/// <summary>
/// Typed response from the RiskParityTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record RiskParityResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Method { get; init; }
    public required List<string> Symbols { get; init; }
    public required Dictionary<string, decimal> OptimalWeights { get; init; }
    public required Dictionary<string, decimal> RiskContributions { get; init; }
    public required Dictionary<string, decimal> TargetRiskContributions { get; init; }
    public required Dictionary<string, decimal> MarginalRiskContributions { get; init; }
    public required decimal ExpectedReturn { get; init; }
    public required decimal ExpectedVolatility { get; init; }
    public required decimal SharpeRatio { get; init; }
    public required decimal DiversificationRatio { get; init; }
    public required decimal RiskBalanceScore { get; init; }
}
