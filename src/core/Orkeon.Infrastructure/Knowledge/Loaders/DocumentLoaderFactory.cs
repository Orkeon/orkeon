using System.Collections.ObjectModel;
using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Knowledge.Loaders;

/// <summary>
/// Factory that selects the appropriate document loader based on source type or source path.
/// </summary>
public sealed class DocumentLoaderFactory : IDocumentLoaderFactory
{
    private readonly Dictionary<string, IDocumentLoader> _loadersByType;
    private readonly ReadOnlyCollection<IDocumentLoader> _allLoaders;

    /// <summary>Initializes a new instance of <see cref="DocumentLoaderFactory"/>.</summary>
    /// <param name="loaders">All registered document loaders.</param>
    public DocumentLoaderFactory(IEnumerable<IDocumentLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        var loaderList = loaders.ToList();
        _allLoaders = loaderList.AsReadOnly();
        _loadersByType = new Dictionary<string, IDocumentLoader>(StringComparer.OrdinalIgnoreCase);

        foreach (var loader in loaderList)
        {
            // Last registered loader wins for a given type
            _loadersByType[loader.SupportedType] = loader;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedTypes => _loadersByType.Keys.ToList().AsReadOnly();

    /// <inheritdoc />
    public IDocumentLoader GetLoader(string sourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);

        if (_loadersByType.TryGetValue(sourceType, out var loader))
            return loader;

        throw new NotSupportedException(
            $"No document loader registered for type '{sourceType}'. Supported types: {string.Join(", ", _loadersByType.Keys)}");
    }

    /// <inheritdoc />
    public IDocumentLoader? GetLoaderForSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        return _allLoaders.FirstOrDefault(loader => loader.CanLoad(source));
    }
}
