using System.Collections.ObjectModel;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Selects the <see cref="IDocumentLoader"/> able to handle a
/// <see cref="SourceDescriptor"/> (first registered loader whose
/// <see cref="IDocumentLoader.CanLoad"/> accepts it). Port of the legacy
/// <c>Orkeon.Infrastructure.Knowledge.Loaders.DocumentLoaderFactory</c> onto the
/// new contract; resolution failures are loud, never silent.
/// </summary>
public sealed class DocumentLoaderFactory
{
    private readonly ReadOnlyCollection<IDocumentLoader> _loaders;

    /// <summary>Initializes the factory with the registered loaders.</summary>
    /// <param name="loaders">All registered document loaders, in registration order.</param>
    public DocumentLoaderFactory(IEnumerable<IDocumentLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);
        _loaders = loaders.ToList().AsReadOnly();
    }

    /// <summary>All registered loaders, in registration order.</summary>
    public IReadOnlyList<IDocumentLoader> Loaders => _loaders;

    /// <summary>
    /// Returns the first loader that can handle <paramref name="source"/>.
    /// </summary>
    /// <exception cref="RagComponentNotFoundException">
    /// No registered loader can handle the source. The message lists the registered
    /// loader types — resolution never fails silently.
    /// </exception>
    public IDocumentLoader GetLoader(SourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var loader = _loaders.FirstOrDefault(l => l.CanLoad(source));
        if (loader is null)
        {
            throw new RagComponentNotFoundException(
                "document loader",
                source.Location,
                _loaders.Select(l => l.GetType().Name).ToList());
        }

        return loader;
    }

    /// <summary>
    /// Attempts to find a loader for <paramref name="source"/>; returns
    /// <see langword="false"/> without throwing when none can handle it. Callers
    /// that swallow a <see langword="false"/> result MUST surface it (log/error) —
    /// silent fallbacks are forbidden by design.
    /// </summary>
    public bool TryGetLoader(
        SourceDescriptor source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IDocumentLoader? loader)
    {
        ArgumentNullException.ThrowIfNull(source);

        loader = _loaders.FirstOrDefault(l => l.CanLoad(source));
        return loader is not null;
    }
}
