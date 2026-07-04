namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the RegulatoryReportingTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record RegulatoryReportingRequest
{
    public string ReportType { get; init; } = "";
    public Dictionary<string, object> PortfolioData { get; init; } = new();
}

/// <summary>
/// Typed response from the RegulatoryReportingTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record RegulatoryReportingResponse
{
    public required DateTime Timestamp { get; init; }
    public required string ReportType { get; init; }
    public required bool ReportGenerated { get; init; }
    public required Dictionary<string, object> ReportData { get; init; }
    public required string FilingDeadline { get; init; }
}
