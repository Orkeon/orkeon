namespace Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;

/// <summary>
/// Typed request for the POVExecutionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record POVExecutionRequest
{
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Quantity { get; init; }
    public double TargetPov { get; init; }
    public double? MaxPov { get; init; }
    public double? MinPov { get; init; }
    public int MaxDurationMinutes { get; init; } = 240;
}

/// <summary>
/// Typed response from the POVExecutionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record POVExecutionResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Algorithm { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required decimal TargetParticipationRate { get; init; }
    public required decimal MinParticipationRate { get; init; }
    public required decimal MaxParticipationRate { get; init; }
    public required List<Dictionary<string, object>> MonitoringIntervals { get; init; }
    public required int ExpectedCompletionMinutes { get; init; }
    public required List<string> AdaptiveFeatures { get; init; }
    public required Dictionary<string, string> ExecutionStrategy { get; init; }
}
