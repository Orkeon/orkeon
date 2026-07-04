using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Dlp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering DLP services.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-003): DLP is NOT registered by
/// <c>AddOrkeonInfrastructure()</c>. Hosts that enforce data loss prevention call
/// <c>AddOrkeonDlp()</c> explicitly. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public static class DlpExtensions
{
    /// <summary>
    /// Opt-in: adds DLP (Data Loss Prevention) services including PII detection,
    /// 5-channel interceptors, and configurable per-channel policies.
    /// Options bind from the "Orkeon:Dlp" configuration section.
    /// </summary>
    /// <remarks>
    /// Self-contained: the policy provider needs options only and the interceptors
    /// depend on the <see cref="IPiiDetector"/> registered here.
    /// </remarks>
    public static IServiceCollection AddOrkeonDlp(this IServiceCollection services)
    {
        services.AddOptions<DlpOptions>()
            .BindConfiguration("Orkeon:Dlp");

        services.TryAddSingleton<IPiiDetector, PiiDetector>();
        services.TryAddSingleton<IDlpPolicyProvider, DlpPolicyProvider>();

        // TryAddEnumerable so calling AddOrkeonDlp() twice does not duplicate interceptors.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDlpInterceptor, ToolOutputDlpInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDlpInterceptor, DelegationDlpInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDlpInterceptor, LogDlpInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDlpInterceptor, MemoryDlpInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDlpInterceptor, ExternalOutputDlpInterceptor>());

        return services;
    }
}
