namespace Orkeon.Application.Interfaces.Knowledge;

/// <summary>
/// Loads documents from various sources and returns their content.
/// </summary>
public interface IDocumentLoader
{
    /// <summary>
    /// Gets the type of document this loader supports (e.g., "text", "csv", "html", "web").
    /// </summary>
    string SupportedType { get; }

    /// <summary>
    /// Loads a document from the specified source.
    /// </summary>
    /// <param name="source">The source path, URL, or identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded document content and metadata.</returns>
    System.Threading.Tasks.Task<LoadedDocument> LoadAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether this loader can handle the given source.
    /// </summary>
    /// <param name="source">The source path, URL, or identifier.</param>
    /// <returns>True if this loader can handle the source.</returns>
    bool CanLoad(string source);
}

/// <summary>
/// Represents a document loaded from a source.
/// </summary>
public record LoadedDocument(
    string Content,
    string SourceId,
    string SourceType,
    Dictionary<string, object> Metadata);

/// <summary>
/// Factory for creating document loaders based on source type.
/// </summary>
public interface IDocumentLoaderFactory
{
    /// <summary>
    /// Gets a loader for the specified source type.
    /// </summary>
    /// <param name="sourceType">The type of source (e.g., "text", "csv").</param>
    /// <returns>The appropriate document loader.</returns>
    /// <exception cref="NotSupportedException">Thrown when no loader supports the given type.</exception>
    IDocumentLoader GetLoader(string sourceType);

    /// <summary>
    /// Gets a loader that can handle the given source, or null if none can.
    /// </summary>
    /// <param name="source">The source path, URL, or identifier.</param>
    /// <returns>The appropriate document loader, or null.</returns>
    IDocumentLoader? GetLoaderForSource(string source);

    /// <summary>
    /// Gets all supported source types.
    /// </summary>
    IReadOnlyList<string> SupportedTypes { get; }
}
