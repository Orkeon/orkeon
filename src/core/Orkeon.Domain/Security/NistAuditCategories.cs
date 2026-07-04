namespace Orkeon.Domain.Security;

/// <summary>
/// NIST SP 800-53 audit event categories for AI systems.
/// Maps Orkeon audit categories to NIST control families.
/// </summary>
public static class NistAuditCategories
{
    /// <summary>
    /// Access Control (AC) -- NIST SP 800-53 control family for managing system access permissions.
    /// </summary>
    public const string AccessControl = "AC";

    /// <summary>
    /// Audit and Accountability (AU) -- NIST SP 800-53 control family for audit trail generation and review.
    /// </summary>
    public const string AuditAccountability = "AU";

    /// <summary>
    /// Configuration Management (CM) -- NIST SP 800-53 control family for baseline configuration and change control.
    /// </summary>
    public const string ConfigurationManagement = "CM";

    /// <summary>
    /// Incident Response (IR) -- NIST SP 800-53 control family for detecting and responding to security incidents.
    /// </summary>
    public const string IncidentResponse = "IR";

    /// <summary>
    /// Risk Assessment (RA) -- NIST SP 800-53 control family for identifying and evaluating risks.
    /// </summary>
    public const string RiskAssessment = "RA";

    /// <summary>
    /// System and Information Integrity (SI) -- NIST SP 800-53 control family for ensuring system and data integrity.
    /// </summary>
    public const string SystemIntegrity = "SI";

    /// <summary>
    /// System and Communications Protection (SC) -- NIST SP 800-53 control family for protecting communications and data in transit.
    /// </summary>
    public const string CommunicationsProtection = "SC";

    /// <summary>
    /// Program Management (PM) -- NIST SP 800-53 control family for AI-specific program oversight and governance.
    /// </summary>
    public const string ProgramManagement = "PM";

    private static readonly string[] s_llmCallCategories = [SystemIntegrity, AuditAccountability, RiskAssessment];
    private static readonly string[] s_toolExecutionCategories = [SystemIntegrity, AccessControl, AuditAccountability];
    private static readonly string[] s_fileAccessCategories = [AccessControl, AuditAccountability];
    private static readonly string[] s_httpRequestCategories = [CommunicationsProtection, SystemIntegrity];
    private static readonly string[] s_securityEventCategories = [IncidentResponse, AuditAccountability, RiskAssessment];
    private static readonly string[] s_crewLifecycleCategories = [AuditAccountability, ProgramManagement];
    private static readonly string[] s_agentDecisionCategories = [RiskAssessment, AuditAccountability, ProgramManagement];
    private static readonly string[] s_memoryOperationCategories = [AccessControl, SystemIntegrity];
    private static readonly string[] s_configChangeCategories = [ConfigurationManagement, AuditAccountability];
    private static readonly string[] s_defaultCategories = [AuditAccountability];

    /// <summary>
    /// Maps an AuditCategory to its primary NIST control family.
    /// </summary>
    public static string GetNistFamily(AuditCategory category) => category switch
    {
        AuditCategory.LlmCall => SystemIntegrity,
        AuditCategory.ToolExecution => SystemIntegrity,
        AuditCategory.FileAccess => AccessControl,
        AuditCategory.HttpRequest => CommunicationsProtection,
        AuditCategory.SecurityEvent => IncidentResponse,
        AuditCategory.CrewLifecycle => AuditAccountability,
        AuditCategory.AgentDecision => RiskAssessment,
        AuditCategory.MemoryOperation => AccessControl,
        AuditCategory.ConfigChange => ConfigurationManagement,
        _ => AuditAccountability
    };

    /// <summary>
    /// Gets all NIST families relevant to an AuditCategory.
    /// </summary>
    public static IReadOnlyList<string> GetAllNistFamilies(AuditCategory category) => category switch
    {
        AuditCategory.LlmCall => s_llmCallCategories,
        AuditCategory.ToolExecution => s_toolExecutionCategories,
        AuditCategory.FileAccess => s_fileAccessCategories,
        AuditCategory.HttpRequest => s_httpRequestCategories,
        AuditCategory.SecurityEvent => s_securityEventCategories,
        AuditCategory.CrewLifecycle => s_crewLifecycleCategories,
        AuditCategory.AgentDecision => s_agentDecisionCategories,
        AuditCategory.MemoryOperation => s_memoryOperationCategories,
        AuditCategory.ConfigChange => s_configChangeCategories,
        _ => s_defaultCategories
    };
}
