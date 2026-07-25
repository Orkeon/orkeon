namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Document-store options of the RAG subsystem, bound from the <c>Orkeon:Rag</c>
/// configuration section (RAG-03/C2, plan §6.2). Selects which memory provider backs the
/// default <see cref="Stores.MemoryProviderDocumentStore"/>.
/// </summary>
/// <remarks>
/// When <see cref="Provider"/> is unset, the store uses the ambient
/// <see cref="Orkeon.Domain.Memory.IMemoryProvider"/> of the host container (historical
/// behaviour). When set, the provider is created through the existing
/// <see cref="Orkeon.Application.Interfaces.Ports.IMemoryProviderFactory"/>; an unknown
/// alias fails loudly at resolution with the list of supported aliases — never a silent
/// fallback.
/// </remarks>
public sealed class RagStoreOptions
{
    /// <summary>
    /// Gets or sets the memory-provider type backing the RAG document store, by factory
    /// alias: <c>inmemory</c>, <c>in-memory</c>, <c>redis</c>, <c>sqlite</c>,
    /// <c>chromadb</c>, <c>chroma</c>, <c>pinecone</c>, <c>lancedb</c>, <c>lance</c>.
    /// <see langword="null"/> or empty selects the ambient container provider.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the provider connection string (e.g. the ChromaDB base URL, the
    /// LanceDB Cloud endpoint, or the SQLite connection string). Passed verbatim to the
    /// factory; ignored when <see cref="Provider"/> is unset.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets the provider-specific options passed to the factory (e.g. <c>ApiKey</c>,
    /// <c>IndexName</c>, <c>TableName</c>, <c>Database</c>). Ignored when
    /// <see cref="Provider"/> is unset.
    /// </summary>
    public Dictionary<string, string> ProviderOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
}
