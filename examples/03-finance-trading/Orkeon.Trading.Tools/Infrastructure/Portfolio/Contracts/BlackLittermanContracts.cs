namespace Orkeon.Trading.Tools.Infrastructure.Portfolio.Contracts;

/// <summary>
/// Typed request for the BlackLittermanTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record BlackLittermanRequest
{
    public List<string> Symbols { get; init; } = [];
    public Dictionary<string, List<double>> ReturnsData { get; init; } = new();
    public Dictionary<string, decimal> MarketCaps { get; init; } = new();
    public List<BlackLittermanTool.InvestorView>? InvestorViews { get; init; }
    public double RiskAversion { get; init; } = 2.5;
    public double Tau { get; init; } = 0.05;
    public double RiskFreeRate { get; init; } = 0.02;
}

/// <summary>
/// Typed response from the BlackLittermanTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record BlackLittermanResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Method { get; init; }
    public required string Description { get; init; }
    public required List<string> Symbols { get; init; }
    public required Dictionary<string, decimal> OptimalWeights { get; init; }
    public required decimal ExpectedReturn { get; init; }
    public required decimal ExpectedVolatility { get; init; }
    public required decimal SharpeRatio { get; init; }
    public required Dictionary<string, decimal> ImpliedReturns { get; init; }
    public required Dictionary<string, decimal> PosteriorReturns { get; init; }
    public required Dictionary<string, decimal> ReturnAdjustments { get; init; }
    public List<Dictionary<string, object>>? InvestorViews { get; init; }
}
