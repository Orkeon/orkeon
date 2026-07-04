namespace Orkeon.Trading.Tools.Infrastructure.Data.Contracts;

/// <summary>
/// Typed request for the FundamentalDataTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record FundamentalDataRequest
{
    public string Symbol { get; init; } = "";
    public List<string>? Metrics { get; init; }
    public string Period { get; init; } = "ttm";
}

/// <summary>
/// Typed response from the FundamentalDataTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record FundamentalDataResponse
{
    public required string Symbol { get; init; }
    public required DateTime ReportDate { get; init; }
    public required Dictionary<string, object> Valuation { get; init; }
    public required Dictionary<string, object> Profitability { get; init; }
    public required Dictionary<string, object> FinancialHealth { get; init; }
    public required Dictionary<string, object> Growth { get; init; }
    public required decimal OverallScore { get; init; }
    public required Dictionary<string, object> ScoreBreakdown { get; init; }
}
