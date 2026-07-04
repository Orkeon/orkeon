namespace Orkeon.Application.Interfaces.Compliance;

/// <summary>
/// NIST SP 800-53 control catalog relevant to AI agent systems.
/// </summary>
public static class NistControlMapping
{
    private static readonly List<NistControl> s_controls =
    [
        // Access Control (AC)
        new("AC-1", "AC", "Access Control Policy", "Develop and document access control policy"),
        new("AC-2", "AC", "Account Management", "Manage system accounts including AI agent identities"),
        new("AC-3", "AC", "Access Enforcement", "Enforce access control decisions for tool and resource usage"),
        new("AC-6", "AC", "Least Privilege", "Employ least privilege for agent permissions"),

        // Audit and Accountability (AU)
        new("AU-2", "AU", "Audit Events", "Define and audit AI decision events"),
        new("AU-3", "AU", "Content of Audit Records", "Ensure audit records contain required information"),
        new("AU-6", "AU", "Audit Review and Analysis", "Review and analyze audit records for anomalies"),
        new("AU-12", "AU", "Audit Generation", "Generate audit records for defined events"),

        // Configuration Management (CM)
        new("CM-2", "CM", "Baseline Configuration", "Maintain baseline configurations for AI models"),
        new("CM-3", "CM", "Configuration Change Control", "Track and control changes to AI configurations"),
        new("CM-6", "CM", "Configuration Settings", "Manage configuration settings for AI components"),

        // Incident Response (IR)
        new("IR-4", "IR", "Incident Handling", "Handle security incidents involving AI agents"),
        new("IR-5", "IR", "Incident Monitoring", "Monitor for AI-related security incidents"),
        new("IR-6", "IR", "Incident Reporting", "Report AI security incidents"),

        // Risk Assessment (RA)
        new("RA-3", "RA", "Risk Assessment", "Assess risks from AI agent operations"),
        new("RA-5", "RA", "Vulnerability Monitoring", "Monitor AI system vulnerabilities"),

        // System and Information Integrity (SI)
        new("SI-3", "SI", "Malicious Code Protection", "Protect against malicious inputs to AI"),
        new("SI-4", "SI", "System Monitoring", "Monitor AI system operations"),
        new("SI-5", "SI", "Security Alerts", "Generate alerts for AI security events"),
        new("SI-10", "SI", "Information Input Validation", "Validate AI inputs and prompts"),

        // System and Communications Protection (SC)
        new("SC-7", "SC", "Boundary Protection", "Monitor and control AI communications"),
        new("SC-8", "SC", "Transmission Confidentiality", "Protect confidentiality of AI data in transit"),
        new("SC-13", "SC", "Cryptographic Protection", "Employ cryptography for AI data protection"),

        // Program Management (PM)
        new("PM-9", "PM", "Risk Management Strategy", "AI risk management strategy alignment"),
        new("PM-28", "PM", "Risk Framing", "Frame AI-specific risks within organizational context"),
    ];

    /// <summary>
    /// Gets all NIST controls in the catalog.
    /// </summary>
    public static IReadOnlyList<NistControl> GetAllControls() => s_controls.AsReadOnly();

    /// <summary>
    /// Gets controls filtered by NIST family code (e.g., "AC", "AU").
    /// </summary>
    public static IReadOnlyList<NistControl> GetControlsByFamily(string family)
        => s_controls.Where(c => c.Family == family).ToList().AsReadOnly();

    /// <summary>
    /// Gets a specific control by its identifier, or null if not found.
    /// </summary>
    public static NistControl? GetControl(string controlId)
        => s_controls.FirstOrDefault(c => c.ControlId == controlId);
}
