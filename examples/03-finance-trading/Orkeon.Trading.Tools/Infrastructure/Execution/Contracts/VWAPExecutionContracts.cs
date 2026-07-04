namespace Orkeon.Trading.Tools.Infrastructure.Execution.Contracts;

/// <summary>
/// Typed request for the VWAPExecutionTool.
/// Property names are auto-mapped to snake_case YAML parameter names
/// via <see cref="System.Text.Json.JsonNamingPolicy.SnakeCaseLower"/>.
/// </summary>
public record VWAPExecutionRequest
{
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Quantity { get; init; }
    public string StartTime { get; init; } = "09:30";
    public string EndTime { get; init; } = "16:00";
    public List<double>? HistoricalVolumeProfile { get; init; }
    public double ParticipationRate { get; init; } = 0.1;
}

/// <summary>
/// Typed response from the VWAPExecutionTool.
/// Property names are serialized to snake_case keys matching the YAML returns schema.
/// </summary>
public record VWAPExecutionResponse
{
    public required DateTime Timestamp { get; init; }
    public required string Algorithm { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required decimal TotalQuantity { get; init; }
    public required string StartTime { get; init; }
    public required string EndTime { get; init; }
    public required int TotalSlices { get; init; }
    public required double SliceIntervalMinutes { get; init; }
    public required List<Dictionary<string, object>> ExecutionSchedule { get; init; }
    public required string EstimatedCompletion { get; init; }
    public required double MaxParticipationRate { get; init; }
    public required Dictionary<string, string> ExecutionStrategy { get; init; }
}
