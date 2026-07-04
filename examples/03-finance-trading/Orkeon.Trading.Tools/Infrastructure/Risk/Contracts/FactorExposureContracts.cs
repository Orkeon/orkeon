using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;

/// <summary>
/// Typed request for the FactorExposureTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record FactorExposureRequest
{
    public List<PortfolioPosition> PortfolioPositions { get; init; } = [];
    public Dictionary<string, List<double>>? FactorReturns { get; init; }
    public string Benchmark { get; init; } = "SPY";
}

/// <summary>
/// Typed response from the FactorExposureTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record FactorExposureResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Benchmark { get; init; }
    public required Dictionary<string, decimal> FactorExposures { get; init; }
    public required Dictionary<string, object> RiskDecomposition { get; init; }
    public required List<Dictionary<string, object>> FactorConcentrations { get; init; }
    public required Dictionary<string, decimal> FactorCorrelationMatrix { get; init; }
    public required Dictionary<string, object> DiversificationMetrics { get; init; }
    public required List<string> Recommendations { get; init; }
}
