using Orkeon.Trading.Tools.Domain.Models;

namespace Orkeon.Trading.Tools.Infrastructure.Risk.Contracts;

/// <summary>
/// Typed request for the StressTestingTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record StressTestingRequest
{
    public List<PortfolioPosition> PortfolioPositions { get; init; } = [];
    public decimal PortfolioValue { get; init; }
    public List<string> Scenarios { get; init; } = ["all"];
    public Dictionary<string, decimal>? CustomShocks { get; init; }
}

/// <summary>
/// Typed response from the StressTestingTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record StressTestingResponse
{
    public required decimal PortfolioValue { get; init; }
    public required DateTime Timestamp { get; init; }
    public required int ScenariosTested { get; init; }
    public required List<Dictionary<string, object>> ScenarioResults { get; init; }
    public required Dictionary<string, object> WorstCaseScenario { get; init; }
    public required Dictionary<string, object> Summary { get; init; }
    public required List<string> Recommendations { get; init; }
}
