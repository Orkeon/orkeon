namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Decision for data validation indicating whether content should be allowed, quarantined, or rejected.
/// </summary>
public enum DataValidationDecision
{
    /// <summary>Content passes validation and is allowed into the pipeline.</summary>
    Allow,
    /// <summary>Content is suspicious and should be held for manual review.</summary>
    Quarantine,
    /// <summary>Content is rejected and must not enter the pipeline.</summary>
    Reject
}

/// <summary>
/// Result of a data validation check including decision, risk score, and findings.
/// </summary>
public record DataValidationResult(
    DataValidationDecision Decision,
    string? Reason = null,
    double RiskScore = 0.0,
    IReadOnlyList<string> Findings = null!)
{
    /// <summary>
    /// Gets the list of specific findings discovered during validation.
    /// </summary>
    public IReadOnlyList<string> Findings { get; init; } = Findings ?? Array.Empty<string>();

    /// <summary>
    /// Creates a result indicating the content is allowed.
    /// </summary>
    public static DataValidationResult Allowed() => new(DataValidationDecision.Allow);

    /// <summary>
    /// Creates a result indicating the content should be quarantined for review.
    /// </summary>
    /// <param name="reason">The reason for quarantine.</param>
    /// <param name="riskScore">The calculated risk score between 0.0 and 1.0.</param>
    /// <param name="findings">Optional specific findings from the validation.</param>
    public static DataValidationResult Quarantined(string reason, double riskScore, IReadOnlyList<string>? findings = null)
        => new(DataValidationDecision.Quarantine, reason, riskScore, findings ?? Array.Empty<string>());

    /// <summary>
    /// Creates a result indicating the content is rejected.
    /// </summary>
    /// <param name="reason">The reason for rejection.</param>
    /// <param name="riskScore">The calculated risk score between 0.0 and 1.0.</param>
    /// <param name="findings">Optional specific findings from the validation.</param>
    public static DataValidationResult Rejected(string reason, double riskScore, IReadOnlyList<string>? findings = null)
        => new(DataValidationDecision.Reject, reason, riskScore, findings ?? Array.Empty<string>());
}

/// <summary>
/// Context for data validation.
/// </summary>
public record DataValidationContext(
    string DocumentId,
    string? Source = null,
    string? ContentHash = null,
    IDictionary<string, string>? Metadata = null);

/// <summary>
/// Validates data entering the RAG pipeline for quality and security issues.
/// </summary>
public interface IDataValidator
{
    /// <summary>
    /// Gets the unique name identifying this validator in the pipeline.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Validates the specified content against security and quality rules.
    /// </summary>
    /// <param name="content">The document content to validate.</param>
    /// <param name="context">The validation context with document metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A validation result indicating allow, quarantine, or reject.</returns>
    System.Threading.Tasks.Task<DataValidationResult> ValidateAsync(string content, DataValidationContext context, CancellationToken ct = default);
}
