namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the ComplianceCheckTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record ComplianceCheckRequest
{
    public Dictionary<string, object> Trade { get; init; } = new();
    public List<string> Rules { get; init; } = [];
}

/// <summary>
/// Typed response from the ComplianceCheckTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record ComplianceCheckResponse
{
    public required DateTime Timestamp { get; init; }
    public required bool TradeApproved { get; init; }
    public required int RulesChecked { get; init; }
    public required List<string> Violations { get; init; }
    public required List<Dictionary<string, object>> ComplianceChecks { get; init; }
    public required string Action { get; init; }
}
