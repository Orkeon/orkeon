using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// The host's named LLM profiles (GAP-17): one provider per profile, configured by the host
/// under <c>Llm:Profiles:&lt;name&gt;</c> with the same shape as the <c>Llm</c> section, which
/// stays the default profile. A crew references a profile by name — <c>llm: { profile: … }</c>
/// in YAML, <c>llm.profile(…)</c> in <c>.ork.ts</c> — never by key or endpoint.
/// </summary>
/// <remarks>
/// Each profile's provider is built once, on first use, metered like the default one, and
/// owned by the registry: a caller never disposes what <see cref="Resolve"/> returns (the rule
/// <see cref="IMemoryProviderFactory.GetProvider"/> follows too).
/// </remarks>
public interface ILlmProfileRegistry
{
    /// <summary>
    /// The profiles a crew may name besides <see cref="LlmProfiles.Default"/>, in the order the
    /// host declared them — those a host allow-list refuses are left out.
    /// </summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Whether a crew may name <paramref name="name"/>: <see cref="LlmProfiles.Default"/>, or
    /// one of <see cref="Names"/> (compared case-insensitively).
    /// </summary>
    /// <param name="name">The profile name a crew wrote.</param>
    /// <returns>True when the profile resolves.</returns>
    bool IsKnown(string name);

    /// <summary>
    /// The profile <paramref name="name"/> names; null, empty or <see cref="LlmProfiles.Default"/>
    /// is the host's default profile.
    /// </summary>
    /// <param name="name">The profile name, or null for the default.</param>
    /// <returns>The profile, its provider built on first use.</returns>
    /// <exception cref="InvalidOperationException">
    /// The host defines no such profile, or refuses it; the message lists the known ones.
    /// </exception>
    LlmProfile Resolve(string? name);
}

/// <summary>
/// One host LLM profile: its provider, on the three surfaces the runtime consumes, all over one
/// metered instance.
/// </summary>
public sealed class LlmProfile
{
    /// <summary>The profile's name; <see cref="LlmProfiles.Default"/> for the host's default.</summary>
    public required string Name { get; init; }

    /// <summary>The profile's provider, metered for the host's usage sink.</summary>
    public required ILlmProvider Provider { get; init; }

    /// <summary>The same provider as an <see cref="IBasicLlmProvider"/>.</summary>
    public required IBasicLlmProvider BasicProvider { get; init; }

    /// <summary>The same provider as a chat client, on the profile's own configuration.</summary>
    public required IChatClient ChatClient { get; init; }
}

/// <summary>
/// Names and checks shared by everything that resolves a profile name: the crew loaders, the
/// agent loop and the scripting DSL.
/// </summary>
public static class LlmProfiles
{
    /// <summary>
    /// The name of the host's default profile — the <c>Llm</c> section. Reserved: a host
    /// cannot define <c>Llm:Profiles:default</c>, and a crew may always name it, to bring an
    /// agent or a task back to the default under a crew-level profile.
    /// </summary>
    public const string Default = "default";

    /// <summary>Whether <paramref name="name"/> designates the default profile (null, blank or <see cref="Default"/>).</summary>
    /// <param name="name">A profile name, or null.</param>
    /// <returns>True for the default profile.</returns>
    public static bool IsDefault(string? name) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), Default, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The message a load fails with when a crew names a profile the host does not offer.
    /// </summary>
    /// <param name="name">The name the crew wrote.</param>
    /// <param name="where">Who wrote it — <c>agent 'Writer'</c>, <c>task 'plan'</c>.</param>
    /// <param name="known">The profiles the host offers besides the default.</param>
    /// <returns>The operator-facing message, listing the known profiles.</returns>
    public static string UnknownMessage(string name, string where, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(known);
        return $"{where} names the LLM profile '{name}', which this host does not offer. " +
            $"Known profiles: {string.Join(", ", new[] { Default }.Concat(known))}. " +
            "A profile is defined by the host under Llm:Profiles:<name>.";
    }

    /// <summary>
    /// Throws when <paramref name="name"/> is set and is neither the default nor a profile of
    /// <paramref name="registry"/>. A host without a registry offers the default alone.
    /// </summary>
    /// <param name="registry">The host's profiles, or null.</param>
    /// <param name="name">The profile a crew named, or null.</param>
    /// <param name="where">Who named it, for the message.</param>
    /// <exception cref="InvalidOperationException">The profile is unknown.</exception>
    public static void EnsureKnown(ILlmProfileRegistry? registry, string? name, string where)
    {
        if (IsDefault(name))
            return;

        if (registry is not null && registry.IsKnown(name!))
            return;

        throw new InvalidOperationException(UnknownMessage(name!, where, registry?.Names ?? []));
    }
}
