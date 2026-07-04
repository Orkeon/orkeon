using System.Collections.Immutable;

namespace Orkeon.Cli.Scripting.Runtime;

/// <summary>
/// Fluent builder for the set of services scripted commands are allowed to resolve via
/// <c>ctx.services.get(name)</c>. Built once at host startup and frozen.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of entries:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Add"/> — required: the resolver must return a non-null instance or <c>get()</c> throws.</description></item>
///   <item><description><see cref="AddOptional"/> — optional: when the resolver returns null, <c>ctx.services.has(name)</c> reports <c>false</c> and <c>get()</c> throws a "not registered" message.</description></item>
/// </list>
/// <para>
/// The host injects custom services by calling <see cref="Add"/> with a closure over its
/// own <see cref="IServiceProvider"/>. Spec §8.3 — the whitelist is extensible by the
/// application, never by the scripts.
/// </para>
/// </remarks>
public sealed class ScriptServiceWhitelist
{
    private readonly Dictionary<string, Func<IServiceProvider, object?>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _optional = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Add a required service binding. <paramref name="resolver"/> must return non-null at runtime.</summary>
    public ScriptServiceWhitelist Add(string name, Func<IServiceProvider, object> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolver);
        _entries[name] = resolver;
        _optional.Remove(name);
        return this;
    }

    /// <summary>Add an optional service binding. Resolver may return <see langword="null"/>.</summary>
    public ScriptServiceWhitelist AddOptional(string name, Func<IServiceProvider, object?> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resolver);
        _entries[name] = resolver;
        _optional.Add(name);
        return this;
    }

    /// <summary>Snapshot the whitelist into the immutable form consumed by <see cref="ScriptServiceLocator"/>.</summary>
    internal Built Build() => new(
        _entries.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
        _optional.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Frozen whitelist consumed by <see cref="ScriptServiceLocator"/>.</summary>
internal sealed record Built(
    ImmutableDictionary<string, Func<IServiceProvider, object?>> Entries,
    ImmutableHashSet<string> Optional);
