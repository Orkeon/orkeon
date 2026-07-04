namespace Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;

/// <summary>
/// Typed request for the VaRCalculationTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record VaRCalculationRequest
{
    public decimal PortfolioValue { get; init; }
    public List<double> ReturnsData { get; init; } = [];
    public double ConfidenceLevel { get; init; } = 0.95;
    public int TimeHorizon { get; init; } = 1;
    public string Method { get; init; } = "all";
    public int MonteCarloSimulations { get; init; } = 10000;
}

/// <summary>
/// Typed response from the VaRCalculationTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record VaRCalculationResponse
{
    public required decimal PortfolioValue { get; init; }
    public required double ConfidenceLevel { get; init; }
    public required int TimeHorizonDays { get; init; }
    public required DateTime Timestamp { get; init; }
    public Dictionary<string, object>? ParametricVar { get; init; }
    public Dictionary<string, object>? HistoricalVar { get; init; }
    public Dictionary<string, object>? MonteCarloVar { get; init; }
    public required Dictionary<string, object> Summary { get; init; }
}
