namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the AuditTrailTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record AuditTrailRequest
{
    public string Action { get; init; } = "";
    public string Actor { get; init; } = "";
    public Dictionary<string, object> Details { get; init; } = new();
}

/// <summary>
/// Typed response from the AuditTrailTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record AuditTrailResponse
{
    public required bool AuditEntryCreated { get; init; }
    public required string AuditId { get; init; }
    public required DateTime Timestamp { get; init; }
}
