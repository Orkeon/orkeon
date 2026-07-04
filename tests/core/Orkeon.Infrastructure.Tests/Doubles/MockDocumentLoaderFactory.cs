using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IDocumentLoaderFactory for testing.
/// </summary>
public sealed class MockDocumentLoaderFactory : IDocumentLoaderFactory
{
    private readonly Dictionary<string, IDocumentLoader> _loaders = new(StringComparer.OrdinalIgnoreCase);
    private IDocumentLoader? _defaultLoader;

    // --- Tracking ---
    public int GetLoaderCallCount { get; private set; }
    public int GetLoaderForSourceCallCount { get; private set; }
    public string? LastGetLoaderSourceType { get; private set; }
    public string? LastGetLoaderForSourceSource { get; private set; }

    /// <summary>
    /// Gets all supported source types from registered loaders.
    /// </summary>
    public IReadOnlyList<string> SupportedTypes => _loaders.Keys.ToList().AsReadOnly();

    // --- Configuration ---
    /// <summary>
    /// Registers a loader for a specific source type.
    /// </summary>
    public void RegisterLoader(string sourceType, IDocumentLoader loader) =>
        _loaders[sourceType] = loader;

    /// <summary>
    /// Sets a default loader to return when no specific loader matches.
    /// </summary>
    public void SetDefaultLoader(IDocumentLoader loader) => _defaultLoader = loader;

    public IDocumentLoader GetLoader(string sourceType)
    {
        GetLoaderCallCount++;
        LastGetLoaderSourceType = sourceType;

        if (_loaders.TryGetValue(sourceType, out var loader))
            return loader;

        if (_defaultLoader != null)
            return _defaultLoader;

        throw new NotSupportedException($"No loader registered for source type '{sourceType}'");
    }

    public IDocumentLoader? GetLoaderForSource(string source)
    {
        GetLoaderForSourceCallCount++;
        LastGetLoaderForSourceSource = source;

        foreach (var loader in _loaders.Values)
        {
            if (loader.CanLoad(source))
                return loader;
        }

        if (_defaultLoader != null && _defaultLoader.CanLoad(source))
            return _defaultLoader;

        return null;
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        GetLoaderCallCount = 0;
        GetLoaderForSourceCallCount = 0;
        LastGetLoaderSourceType = null;
        LastGetLoaderForSourceSource = null;
    }
}
