using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the audit trail system.
/// </summary>
public class AuditOptions
{
    /// <summary>
    /// Minimum severity level for events to be logged.
    /// </summary>
    public AuditSeverity MinSeverity { get; set; } = AuditSeverity.Info;

    /// <summary>
    /// Directory where audit log files are stored.
    /// </summary>
    public string AuditDirectory { get; set; } = "./audit-logs";

    /// <summary>
    /// Number of days to retain audit logs.
    /// </summary>
    public int RetentionDays { get; set; } = SecurityDefaults.AuditRetentionDays;

    /// <summary>
    /// Whether audit logging is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Categories to log. Empty means all categories are enabled.
    /// </summary>
    public HashSet<AuditCategory> EnabledCategories { get; } = [];
}
