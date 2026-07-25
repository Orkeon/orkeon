namespace Orkeon.Rag.Factories;

/// <summary>
/// Base class for name-based RAG component factories, following the
/// <c>MemoryProviderFactory</c> pattern: names and aliases are matched
/// case-insensitively after trimming, and an unknown name always fails loudly
/// with the list of known names — never a silent fallback.
/// </summary>
/// <remarks>
/// Concrete component implementations land in later batches (RAG-02 C3+);
/// until then, components are registered as delegates at composition time
/// via <see cref="Register"/>. Registration is not thread-safe: register during
/// composition, resolve afterwards.
/// </remarks>
/// <typeparam name="TComponent">The component contract produced by this factory.</typeparam>
public abstract class NamedRagComponentFactory<TComponent>
    where TComponent : class
{
    // Keys are normalized (trimmed, upper-invariant); values keep the display casing.
    private readonly Dictionary<string, Func<TComponent>> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _displayNames = new(StringComparer.Ordinal);

    /// <summary>Human-readable component kind used in error messages (e.g. "chunking strategy").</summary>
    protected abstract string ComponentKind { get; }

    /// <summary>All registered names and aliases, in their registered casing.</summary>
    public IReadOnlyCollection<string> KnownNames => _displayNames.Values;

    /// <summary>
    /// Registers a component under <paramref name="name"/> and optional
    /// <paramref name="aliases"/>. Names are matched case-insensitively after
    /// trimming. Duplicate registrations fail loudly.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A name or alias is empty, or is already registered.
    /// </exception>
    public void Register(string name, Func<TComponent> factory, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(aliases);

        RegisterKey(name, factory, nameof(name));
        foreach (var alias in aliases)
        {
            RegisterKey(alias, factory, nameof(aliases));
        }
    }

    /// <summary>
    /// Resolves and instantiates the component registered under
    /// <paramref name="name"/> (or one of its aliases).
    /// </summary>
    /// <exception cref="RagComponentNotFoundException">
    /// <paramref name="name"/> matches no registered name or alias. The exception
    /// message lists every known name — resolution never fails silently.
    /// </exception>
    public TComponent Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!TryCreate(name, out var component))
        {
            throw new RagComponentNotFoundException(ComponentKind, name, [.. KnownNames]);
        }

        return component;
    }

    /// <summary>
    /// Attempts to resolve <paramref name="name"/>; returns <c>false</c> without
    /// throwing when unknown. Callers that swallow a <c>false</c> result MUST
    /// surface it (log/error) — silent fallbacks are forbidden by design.
    /// </summary>
    public bool TryCreate(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TComponent? component)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_factories.TryGetValue(Normalize(name), out var factory))
        {
            component = factory();
            return true;
        }

        component = null;
        return false;
    }

    /// <summary>Whether <paramref name="name"/> matches a registered name or alias.</summary>
    public bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _factories.ContainsKey(Normalize(name));
    }

    private void RegisterKey(string key, Func<TComponent> factory, string paramName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                $"A {ComponentKind} name or alias must be a non-empty string.", paramName);
        }

        var normalized = Normalize(key);
        if (!_factories.TryAdd(normalized, factory))
        {
            throw new ArgumentException(
                $"The {ComponentKind} name or alias '{key.Trim()}' is already registered " +
                $"(names are matched case-insensitively).", paramName);
        }

        _displayNames[normalized] = key.Trim();
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
