namespace Orkeon.Trading.Tools.Infrastructure.Governance.Contracts;

/// <summary>
/// Typed request for the AlertManagementTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record AlertManagementRequest
{
    public string AlertType { get; init; } = "";
    public string Severity { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>
/// Typed response from the AlertManagementTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record AlertManagementResponse
{
    public required bool AlertCreated { get; init; }
    public required string AlertId { get; init; }
    public required bool NotificationSent { get; init; }
    public required List<string> Channels { get; init; }
}
