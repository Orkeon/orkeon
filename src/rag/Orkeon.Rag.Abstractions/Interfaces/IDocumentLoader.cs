using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Loads <see cref="RagDocument"/>s from a <see cref="SourceDescriptor"/>
/// (file, directory, URL, inline text…).
/// </summary>
public interface IDocumentLoader
{
    /// <summary>Whether this loader can handle <paramref name="source"/>.</summary>
    bool CanLoad(SourceDescriptor source);

    /// <summary>Streams the documents contained in <paramref name="source"/>.</summary>
    IAsyncEnumerable<RagDocument> LoadAsync(
        SourceDescriptor source,
        CancellationToken cancellationToken = default);
}
