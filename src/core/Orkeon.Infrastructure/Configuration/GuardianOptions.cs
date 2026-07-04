using Orkeon.Application.Services.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the guardian system.
/// Bind from appsettings.json "Orkeon:Guardian" section.
/// </summary>
public class GuardianOptions
{
    /// <summary>Gets or sets a value indicating whether the guardian system is enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets the default guardian policy.</summary>
    public GuardianPolicy DefaultPolicy { get; set; } = new();
    /// <summary>Gets or sets a value indicating whether auditing is enabled.</summary>
    public bool AuditEnabled { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether policy violations are logged.</summary>
    public bool LogViolations { get; set; } = true;
}
