namespace Orkeon.Application.Interfaces.Compliance;

/// <summary>
/// Generates NIST SP 800-53 compliance reports for the AI agent system.
/// </summary>
public interface INistComplianceReporter
{
    /// <summary>
    /// Generates a full NIST compliance report including control statuses, findings, and recommendations.
    /// </summary>
    System.Threading.Tasks.Task<NistComplianceReport> GenerateReportAsync(CancellationToken ct = default);

    /// <summary>
    /// Calculates the current compliance score without generating a full report.
    /// </summary>
    System.Threading.Tasks.Task<ComplianceScore> GetComplianceScoreAsync(CancellationToken ct = default);
}
