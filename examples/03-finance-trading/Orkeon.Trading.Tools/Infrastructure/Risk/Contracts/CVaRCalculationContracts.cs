namespace Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;

/// <summary>
/// Typed request for the CVaRCalculationTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record CVaRCalculationRequest
{
    public decimal PortfolioValue { get; init; }
    public List<double> ReturnsData { get; init; } = [];
    public double ConfidenceLevel { get; init; } = 0.95;
    public int TimeHorizon { get; init; } = 1;
    public string Method { get; init; } = "historical";
}

/// <summary>
/// Typed response from the CVaRCalculationTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record CVaRCalculationResponse
{
    public required decimal PortfolioValue { get; init; }
    public required double ConfidenceLevel { get; init; }
    public required int TimeHorizonDays { get; init; }
    public required string Method { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal CvarAmount { get; init; }
    public required decimal CvarPercentage { get; init; }
    public required decimal VarAmount { get; init; }
    public required decimal VarPercentage { get; init; }
    public required decimal TailRiskPremium { get; init; }
    public required decimal TailRiskPremiumPct { get; init; }
    public required int TailObservations { get; init; }
    public required List<double> WorstLosses { get; init; }
    public required double CvarToVarRatio { get; init; }
    public required string Interpretation { get; init; }
    public required Dictionary<string, object> DistributionStats { get; init; }
}
