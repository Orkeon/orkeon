using Orkeon.Application.Interfaces.Compliance;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Compliance;
using Orkeon.Infrastructure.Security;
using Orkeon.Infrastructure.Security.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering NIST SP 800-53 compliance services.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-003): NIST compliance reporting is NOT registered by
/// <c>AddOrkeonInfrastructure()</c>. Hosts that produce compliance reports call
/// <c>AddOrkeonNistCompliance()</c> explicitly. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public static class NistComplianceExtensions
{
    /// <summary>
    /// Adds NIST compliance reporting services including the report generator.
    /// </summary>
    /// <remarks>
    /// The report generator reads the audit trail through <see cref="IAuditLogger"/>.
    /// To keep the opt-in graph resolvable on its own, this method TryAdds a fallback
    /// audit chain (structured-log sink + audit logger). When the host already called
    /// <c>AddOrkeonInfrastructure()</c>, the audit services registered there win.
    /// </remarks>
    public static IServiceCollection AddOrkeonNistCompliance(this IServiceCollection services)
    {
        // Fallback audit chain — no-ops when AddOrkeonInfrastructure() already registered it.
        services.AddOptions();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuditSink, StructuredLogAuditSink>());
        services.TryAddSingleton<IAuditLogger, AuditLogger>();

        services.TryAddSingleton<INistComplianceReporter, NistComplianceReportGenerator>();
        return services;
    }
}
