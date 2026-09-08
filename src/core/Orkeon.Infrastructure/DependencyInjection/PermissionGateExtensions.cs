using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// DI extension for the per-tool-call permission gate. The gate
/// is a config opt-in: without <c>Orkeon:Security:PermissionGate:Enabled = true</c> nothing
/// is registered and <c>ctx.llm.act</c> keeps its ungated behaviour — enabling it globally
/// would deny every tool call of scripts that never pass a <c>permissionMode</c> (the
/// <c>default</c> mode asks, and headless asks degrade to denies).
/// </summary>
public static class PermissionGateExtensions
{
    /// <summary>
    /// Registers <see cref="ModePermissionGate"/> as the <see cref="IPermissionGate"/> when
    /// the <c>Orkeon:Security:PermissionGate</c> section enables it. <c>Interactive</c>
    /// (default <c>false</c>) declares whether an approval channel exists; the interactive
    /// Ask flow itself is a v2 follow-up, so hosts should leave it <c>false</c> for now.
    /// Idempotent (<c>TryAddSingleton</c> — a host-supplied gate wins).
    /// </summary>
    public static IServiceCollection AddOrkeonPermissionGate(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Orkeon:Security:PermissionGate");
        if (!section.GetValue("Enabled", false))
            return services;

        var interactive = section.GetValue("Interactive", false);
        services.TryAddSingleton<IPermissionGate>(_ => new ModePermissionGate(interactive));
        return services;
    }
}
