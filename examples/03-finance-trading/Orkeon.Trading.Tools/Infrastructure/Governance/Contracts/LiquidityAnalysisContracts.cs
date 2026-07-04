namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the LiquidityAnalysisTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record LiquidityAnalysisRequest
{
    public List<Dictionary<string, object>> Positions { get; init; } = [];
}

/// <summary>
/// Typed response from the LiquidityAnalysisTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record LiquidityAnalysisResponse
{
    public required DateTime Timestamp { get; init; }
    public required decimal PortfolioLiquidityScore { get; init; }
    public required decimal MaxDaysToLiquidate { get; init; }
    public required List<Dictionary<string, object>> PositionLiquidity { get; init; }
    public required string LiquidityRating { get; init; }
    public required int IlliquidPositions { get; init; }
}
