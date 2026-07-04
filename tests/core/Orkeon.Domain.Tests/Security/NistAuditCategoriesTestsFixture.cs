using Orkeon.Domain.Security;

namespace Orkeon.Domain.Tests.Security;

public class NistAuditCategoriesTestsFixture
{
    /// <summary>
    /// Returns all defined AuditCategory enum values.
    /// </summary>
    public static IReadOnlyList<AuditCategory> AllCategories { get; } =
        Enum.GetValues<AuditCategory>().ToList().AsReadOnly();

    /// <summary>
    /// Returns all NIST family constants defined in NistAuditCategories.
    /// </summary>
    public static IReadOnlyList<string> AllFamilyConstants { get; } =
    [
        NistAuditCategories.AccessControl,
        NistAuditCategories.AuditAccountability,
        NistAuditCategories.ConfigurationManagement,
        NistAuditCategories.IncidentResponse,
        NistAuditCategories.RiskAssessment,
        NistAuditCategories.SystemIntegrity,
        NistAuditCategories.CommunicationsProtection,
        NistAuditCategories.ProgramManagement,
    ];

    /// <summary>
    /// Returns expected primary family for each AuditCategory.
    /// </summary>
    public static IReadOnlyDictionary<AuditCategory, string> ExpectedPrimaryFamilies { get; } =
        new Dictionary<AuditCategory, string>
        {
            [AuditCategory.LlmCall] = NistAuditCategories.SystemIntegrity,
            [AuditCategory.ToolExecution] = NistAuditCategories.SystemIntegrity,
            [AuditCategory.FileAccess] = NistAuditCategories.AccessControl,
            [AuditCategory.HttpRequest] = NistAuditCategories.CommunicationsProtection,
            [AuditCategory.SecurityEvent] = NistAuditCategories.IncidentResponse,
            [AuditCategory.CrewLifecycle] = NistAuditCategories.AuditAccountability,
            [AuditCategory.AgentDecision] = NistAuditCategories.RiskAssessment,
            [AuditCategory.MemoryOperation] = NistAuditCategories.AccessControl,
            [AuditCategory.ConfigChange] = NistAuditCategories.ConfigurationManagement,
        };
}
