using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
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
    /// <summary>The section the gate is configured by.</summary>
    private const string SectionName = "Orkeon:Security:PermissionGate";

    /// <summary>
    /// Registers <see cref="ModePermissionGate"/> as the <see cref="IPermissionGate"/> when
    /// the <c>Orkeon:Security:PermissionGate</c> section enables it. When the host registers an
    /// <see cref="IToolApprovalChannel"/> (the REPL does), the gate puts every <c>ask</c> to it;
    /// otherwise <c>Interactive</c> (default <c>false</c>) declares whether the caller resolves
    /// <c>ask</c> itself, and left <c>false</c> an <c>ask</c> is a refusal.
    /// Idempotent (<c>TryAddSingleton</c> — a host-supplied gate wins).
    /// </summary>
    public static IServiceCollection AddOrkeonPermissionGate(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Read here, at registration, so its values refuse the start already; its keys are judged
        // with every declared section's (GAP-40), whether the gate is on or not.
        services.DeclareSettingsShape(SectionName, typeof(PermissionGateSettingsShape));
        var section = configuration.GetSection(SectionName);
        var defaults = new PermissionGateSettingsShape();
        if (!section.GetValue("Enabled", defaults.Enabled))
            return services;

        var interactive = section.GetValue("Interactive", defaults.Interactive);
        services.TryAddSingleton<IPermissionGate>(sp => sp.GetService<IToolApprovalChannel>() is { } channel
            ? new ModePermissionGate(channel)
            : new ModePermissionGate(interactive));
        return services;
    }
}

/// <summary>
/// The permission gate of tool calls: off, every call runs. On, a tool that only reads passes and
/// the others need an approval, by permission mode.
/// </summary>
/// <remarks>Its properties are the keys, and their values on a new instance the defaults the registration applies: nothing binds it.</remarks>
internal sealed class PermissionGateSettingsShape
{
    /// <summary>Whether the gate is registered. Left <c>false</c>, no tool call is gated.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether the host resolves an approval itself, without an approval channel. A host that registers a
    /// channel (orkeon-repl does) asks the operator whatever this says. Left <c>false</c> without a channel,
    /// a call that needs an approval is refused, and the model reads the reason as the tool's result.
    /// </summary>
    public bool Interactive { get; set; }
}
