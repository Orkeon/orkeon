using Orkeon.Application.Interfaces.Compliance;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Compliance;

/// <summary>
/// Generates NIST SP 800-53 compliance reports by analyzing audit event coverage.
/// </summary>
public sealed partial class NistComplianceReportGenerator : INistComplianceReporter
{
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<NistComplianceReportGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NistComplianceReportGenerator"/> class.
    /// </summary>
    /// <param name="auditLogger">The audit logger for querying audit events.</param>
    /// <param name="logger">The logger instance.</param>
    public NistComplianceReportGenerator(
        IAuditLogger auditLogger,
        ILogger<NistComplianceReportGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(auditLogger);
        _auditLogger = auditLogger;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NistComplianceReport> GenerateReportAsync(CancellationToken ct = default)
    {
        LogGeneratingNistSp80053();

        var coveredFamilies = await GetCoveredFamiliesAsync(ct).ConfigureAwait(false);
        var controls = EvaluateControls(coveredFamilies);
        var score = CalculateScore(controls);
        var findings = GenerateFindings(controls);
        var recommendations = GenerateRecommendations(controls, coveredFamilies);

        var report = new NistComplianceReport(
            ReportId: Guid.NewGuid().ToString("N"),
            GeneratedAt: DateTime.UtcNow,
            Score: score,
            Controls: controls,
            Findings: findings,
            Recommendations: recommendations);

        LogNistComplianceReportGeneratedControls(score.OverallScore, score.ImplementedControls, score.TotalControls);

        return report;
    }

    /// <inheritdoc />
    public async Task<ComplianceScore> GetComplianceScoreAsync(CancellationToken ct = default)
    {
        var coveredFamilies = await GetCoveredFamiliesAsync(ct).ConfigureAwait(false);
        var controls = EvaluateControls(coveredFamilies);
        return CalculateScore(controls);
    }

    /// <summary>
    /// Queries audit events for each AuditCategory and collects the NIST families
    /// that have associated audit evidence.
    /// </summary>
    private async Task<HashSet<string>> GetCoveredFamiliesAsync(CancellationToken ct)
    {
        var coveredFamilies = new HashSet<string>();

        foreach (AuditCategory category in Enum.GetValues<AuditCategory>())
        {
            var query = new AuditQuery(Category: category, Limit: 1);
            var events = await _auditLogger.QueryAsync(query, ct).ConfigureAwait(false);

            if (events.Count > 0)
            {
                var families = NistAuditCategories.GetAllNistFamilies(category);
                foreach (var family in families)
                {
                    coveredFamilies.Add(family);
                }
            }
        }

        return coveredFamilies;
    }

    /// <summary>
    /// Evaluates each NIST control based on whether its family has audit coverage.
    /// </summary>
    private static List<NistControl> EvaluateControls(HashSet<string> coveredFamilies)
    {
        var allControls = NistControlMapping.GetAllControls();
        var evaluated = new List<NistControl>(allControls.Count);

        foreach (var control in allControls)
        {
            var status = coveredFamilies.Contains(control.Family)
                ? ControlStatus.Implemented
                : ControlStatus.NotImplemented;

            evaluated.Add(control with { Status = status });
        }

        return evaluated;
    }

    /// <summary>
    /// Calculates the overall compliance score and per-family scores.
    /// </summary>
    private static ComplianceScore CalculateScore(List<NistControl> controls)
    {
        var implemented = 0;
        var partial = 0;
        var notImplemented = 0;
        var notApplicable = 0;

        var familyScores = new Dictionary<string, (int implemented, int partial, int total)>();

        foreach (var control in controls)
        {
            switch (control.Status)
            {
                case ControlStatus.Implemented:
                    implemented++;
                    break;
                case ControlStatus.PartiallyImplemented:
                    partial++;
                    break;
                case ControlStatus.NotImplemented:
                    notImplemented++;
                    break;
                case ControlStatus.NotApplicable:
                    notApplicable++;
                    break;
            }

            if (control.Status != ControlStatus.NotApplicable)
            {
                if (!familyScores.ContainsKey(control.Family))
                    familyScores[control.Family] = (0, 0, 0);

                var (fi, fp, ft) = familyScores[control.Family];
                familyScores[control.Family] = control.Status switch
                {
                    ControlStatus.Implemented => (fi + 1, fp, ft + 1),
                    ControlStatus.PartiallyImplemented => (fi, fp + 1, ft + 1),
                    _ => (fi, fp, ft + 1)
                };
            }
        }

        var total = controls.Count;
        var applicable = total - notApplicable;
        var overallScore = applicable > 0
            ? (implemented * 1.0 + partial * 0.5) / applicable
            : 0.0;

        var scoresByFamily = new Dictionary<string, double>();
        foreach (var (family, (fi, fp, ft)) in familyScores)
        {
            scoresByFamily[family] = ft > 0
                ? (fi * 1.0 + fp * 0.5) / ft
                : 0.0;
        }

        return new ComplianceScore(
            OverallScore: overallScore,
            TotalControls: total,
            ImplementedControls: implemented,
            PartiallyImplementedControls: partial,
            NotImplementedControls: notImplemented,
            NotApplicableControls: notApplicable,
            ScoresByFamily: scoresByFamily);
    }

    /// <summary>
    /// Generates findings for controls that are not fully implemented.
    /// </summary>
    private static List<string> GenerateFindings(List<NistControl> controls)
    {
        var findings = new List<string>();

        foreach (var control in controls)
        {
            if (control.Status == ControlStatus.NotImplemented)
            {
                findings.Add(
                    $"[{control.ControlId}] {control.Title}: No audit evidence found. {control.Description}.");
            }
            else if (control.Status == ControlStatus.PartiallyImplemented)
            {
                findings.Add(
                    $"[{control.ControlId}] {control.Title}: Partial implementation detected. Full coverage recommended.");
            }
        }

        return findings;
    }

    /// <summary>
    /// Generates recommendations based on missing NIST control families.
    /// </summary>
    private static List<string> GenerateRecommendations(
        List<NistControl> controls,
        HashSet<string> coveredFamilies)
    {
        var recommendations = new List<string>();

        var allFamilies = controls
            .Select(c => c.Family)
            .Distinct()
            .ToHashSet();

        var missingFamilies = allFamilies.Except(coveredFamilies).ToList();

        foreach (var family in missingFamilies)
        {
            var recommendation = family switch
            {
                "AC" => "Implement access control logging for file and memory operations to satisfy AC family controls.",
                "AU" => "Enable audit trail generation for crew lifecycle events to satisfy AU family controls.",
                "CM" => "Track configuration changes to AI models and components to satisfy CM family controls.",
                "IR" => "Implement security event monitoring and incident detection to satisfy IR family controls.",
                "RA" => "Enable risk assessment logging for agent decision events to satisfy RA family controls.",
                "SI" => "Implement input validation and system monitoring for LLM calls to satisfy SI family controls.",
                "SC" => "Enable communication protection logging for HTTP requests to satisfy SC family controls.",
                "PM" => "Establish AI program management practices and audit trails to satisfy PM family controls.",
                _ => $"Review and implement controls for the {family} family."
            };
            recommendations.Add(recommendation);
        }

        if (recommendations.Count == 0 && coveredFamilies.Count == allFamilies.Count)
        {
            recommendations.Add("All NIST control families have audit coverage. Consider periodic review to maintain compliance.");
        }

        return recommendations;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Generating NIST SP 800-53 compliance report")]
    private partial void LogGeneratingNistSp80053();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "NIST compliance report generated: {Score:P1} ({Implemented}/{Total} controls)")]
    private partial void LogNistComplianceReportGeneratedControls(double score, int implemented, int total);

}
