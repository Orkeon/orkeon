namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Channels where DLP interception can occur.
/// </summary>
public enum DlpChannel
{
    /// <summary>Tool output channel.</summary>
    ToolOutput,
    /// <summary>Delegation channel.</summary>
    Delegation,
    /// <summary>Log channel.</summary>
    Log,
    /// <summary>Memory channel.</summary>
    Memory,
    /// <summary>External output channel.</summary>
    ExternalOutput
}

/// <summary>
/// Action to take when PII is detected.
/// </summary>
public enum DlpAction
{
    /// <summary>Allow content to pass through.</summary>
    Allow,
    /// <summary>Mask detected PII.</summary>
    Mask,
    /// <summary>Block the content entirely.</summary>
    Block,
    /// <summary>Allow but audit the detection.</summary>
    Audit
}

/// <summary>
/// Types of personally identifiable information.
/// </summary>
public enum PiiType
{
    /// <summary>Email address.</summary>
    Email,
    /// <summary>Phone number.</summary>
    Phone,
    /// <summary>Social Security Number.</summary>
    Ssn,
    /// <summary>Credit card number.</summary>
    CreditCard,
    /// <summary>International Bank Account Number.</summary>
    Iban,
    /// <summary>Passport number.</summary>
    Passport,
    /// <summary>IP address.</summary>
    IpAddress,
    /// <summary>Custom PII type.</summary>
    Custom
}

/// <summary>
/// Represents a single PII match found during scanning.
/// </summary>
public record PiiMatch(PiiType Type, string MatchedText, int Position, int Length);

/// <summary>
/// Result of scanning content for PII.
/// </summary>
public record DlpScanResult(bool HasPii, IReadOnlyList<PiiMatch> Matches, string? SanitizedContent = null);

/// <summary>
/// Result of DLP interception on a channel.
/// </summary>
public record DlpInterceptionResult(
    DlpAction ActionTaken,
    string? OriginalContent,
    string? ProcessedContent,
    IReadOnlyList<PiiMatch> DetectedPii);

/// <summary>
/// Intercepts content on a specific DLP channel to detect and handle PII.
/// </summary>
public interface IDlpInterceptor
{
    /// <summary>
    /// The channel this interceptor handles.
    /// </summary>
    DlpChannel Channel { get; }

    /// <summary>
    /// Intercepts and processes content according to the given policy.
    /// </summary>
    System.Threading.Tasks.Task<DlpInterceptionResult> InterceptAsync(string content, DlpPolicy policy, CancellationToken ct = default);
}
