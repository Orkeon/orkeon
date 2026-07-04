namespace Orkeon.Domain.Security;

/// <summary>
/// Information about an LLM call for audit logging purposes.
/// Groups all parameters for <see cref="AuditEventBuilders.LlmCall(LlmCallAuditInfo)"/>.
/// </summary>
public sealed class LlmCallAuditInfo
{
    /// <summary>
    /// The correlation ID for tracing.
    /// </summary>
    public string CorrelationId { get; init; } = null!;

    /// <summary>
    /// The LLM provider name.
    /// </summary>
    public string Provider { get; init; } = null!;

    /// <summary>
    /// The LLM model name.
    /// </summary>
    public string Model { get; init; } = null!;

    /// <summary>
    /// Number of prompt tokens used.
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Number of completion tokens used.
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Estimated cost of the LLM call.
    /// </summary>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Duration of the call in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// The outcome of the LLM call.
    /// </summary>
    public AuditOutcome Outcome { get; init; }

    /// <summary>
    /// Optional agent role associated with the call.
    /// </summary>
    public string? AgentRole { get; init; }

    /// <summary>
    /// Optional crew ID associated with the call.
    /// </summary>
    public string? CrewId { get; init; }
}
