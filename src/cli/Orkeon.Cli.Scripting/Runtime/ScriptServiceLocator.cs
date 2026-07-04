namespace Orkeon.Cli.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: exposed to JS as ctx.services
/// <summary>
/// Whitelisted service locator exposed to scripts as <c>ctx.services</c>. Resolves
/// services by their logical name (e.g. <c>"fs"</c>) — the host configures which CLR
/// services are reachable via <see cref="ScriptServiceWhitelist"/>.
/// </summary>
/// <remarks>
/// <para>
/// Spec §8.3: the only access path scripts have to host services. Never expose the raw
/// <see cref="IServiceProvider"/> — a script must not be able to reach <c>IHostLifetime</c>
/// and kill the process other than via <c>ctx.exit()</c>.
/// </para>
/// </remarks>
public sealed class ScriptServiceLocator
{
    private static readonly ScriptServiceLocator _empty = new(
        Array.Empty<KeyValuePair<string, Func<IServiceProvider, object?>>>(),
        EmptyServiceProvider.Instance,
        System.Collections.Immutable.ImmutableHashSet<string>.Empty);

    private readonly IServiceProvider _services;
    private readonly System.Collections.Immutable.ImmutableDictionary<string, Func<IServiceProvider, object?>> _entries;
    private readonly System.Collections.Immutable.ImmutableHashSet<string> _optional;

    /// <summary>Shared empty locator — used in unit-test contexts where no host services exist.</summary>
    public static ScriptServiceLocator Empty => _empty;

    internal ScriptServiceLocator(Built whitelist, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentNullException.ThrowIfNull(services);
        _entries = whitelist.Entries;
        _optional = whitelist.Optional;
        _services = services;
    }

    private ScriptServiceLocator(
        IEnumerable<KeyValuePair<string, Func<IServiceProvider, object?>>> entries,
        IServiceProvider services,
        System.Collections.Immutable.ImmutableHashSet<string> optional)
    {
        _entries = entries.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        _services = services;
        _optional = optional;
    }

    /// <summary>True when the whitelist contains <paramref name="name"/> and (for optional entries) the resolver returns non-null.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure resolving an optional service is treated as 'not available' (returns false) rather than propagating.")]
    public bool has(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!_entries.TryGetValue(name, out var resolver)) return false;
        if (_optional.Contains(name))
        {
            try { return resolver(_services) is not null; }
            catch { return false; }
        }
        return true;
    }

    /// <summary>Resolve a service by name. Throws when not whitelisted, or when the optional resolver returned null.</summary>
    public object get(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("ctx.services.get(name): name is required.");
        if (!_entries.TryGetValue(name, out var resolver))
            throw new InvalidOperationException(
                $"ctx.services.get('{name}') is not whitelisted. Available: {string.Join(", ", _entries.Keys)}.");
        var instance = resolver(_services);
        if (instance is null)
            throw new InvalidOperationException(
                $"ctx.services.get('{name}') resolved to null — the optional service is not registered in this host.");
        return instance;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
#pragma warning restore IDE1006

internal static class ScriptServiceLocatorExtensions
{
    public static System.Collections.Immutable.ImmutableDictionary<TK, TV> ToImmutableDictionary<TK, TV>(
        this IEnumerable<KeyValuePair<TK, TV>> source, IEqualityComparer<TK> comparer) where TK : notnull
        => System.Collections.Immutable.ImmutableDictionary.CreateRange(comparer, source);
}
