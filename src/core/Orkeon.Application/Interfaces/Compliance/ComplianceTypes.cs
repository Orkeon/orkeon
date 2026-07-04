namespace Orkeon.Application.Interfaces.Compliance;

/// <summary>
/// Implementation status of a NIST control.
/// </summary>
public enum ControlStatus
{
    /// <summary>The control is fully implemented.</summary>
    Implemented,
    /// <summary>The control is partially implemented.</summary>
    PartiallyImplemented,
    /// <summary>The control is not implemented.</summary>
    NotImplemented,
    /// <summary>The control is not applicable.</summary>
    NotApplicable
}

/// <summary>
/// Data classification levels for AI-processed information.
/// </summary>
public enum DataClassification
{
    /// <summary>Publicly available information.</summary>
    Public,
    /// <summary>Internal use only.</summary>
    Internal,
    /// <summary>Confidential information.</summary>
    Confidential,
    /// <summary>Restricted access information.</summary>
    Restricted
}

/// <summary>
/// Represents a single NIST SP 800-53 security control.
/// </summary>
public sealed record NistControl(
    string ControlId,
    string Family,
    string Title,
    string Description,
    ControlStatus Status = ControlStatus.NotImplemented);

/// <summary>
/// Compliance score summary across all NIST control families.
/// </summary>
public sealed record ComplianceScore(
    double OverallScore,
    int TotalControls,
    int ImplementedControls,
    int PartiallyImplementedControls,
    int NotImplementedControls,
    int NotApplicableControls,
    IReadOnlyDictionary<string, double> ScoresByFamily);

/// <summary>
/// A complete NIST compliance report with findings and recommendations.
/// </summary>
public sealed record NistComplianceReport(
    string ReportId,
    DateTime GeneratedAt,
    ComplianceScore Score,
    IReadOnlyList<NistControl> Controls,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Recommendations);
