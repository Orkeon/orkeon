namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the ConcentrationRiskTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record ConcentrationRiskRequest
{
    public List<Dictionary<string, object>> Positions { get; init; } = [];
}

/// <summary>
/// Typed response from the ConcentrationRiskTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record ConcentrationRiskResponse
{
    public required DateTime Timestamp { get; init; }
    public required int TotalPositions { get; init; }
    public required decimal HerfindahlIndex { get; init; }
    public required decimal EffectivePositions { get; init; }
    public required decimal Top5ConcentrationPct { get; init; }
    public required decimal MaxSinglePositionPct { get; init; }
    public required string ConcentrationRating { get; init; }
    public required decimal DiversificationScore { get; init; }
}
