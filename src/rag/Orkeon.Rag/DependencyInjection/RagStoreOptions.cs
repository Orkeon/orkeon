namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Document-store options of the RAG subsystem, bound from the <c>Orkeon:Rag</c>
/// configuration section (RAG-03/C2, plan §6.2). Selects which memory provider backs the
/// default <see cref="Stores.MemoryProviderDocumentStore"/>.
/// </summary>
/// <remarks>
/// When <see cref="Provider"/> is unset, the store uses the ambient
/// <see cref="Orkeon.Domain.Memory.IMemoryProvider"/> of the host container. When set, the
/// store uses that type's shared provider from the
/// <see cref="Orkeon.Application.Interfaces.Ports.IMemoryProviderFactory"/>, whose connection
/// comes from the provider's own host section (<c>Orkeon:Redis</c>, <c>Orkeon:Sqlite</c>,
/// <c>Orkeon:ChromaDb</c>, <c>Orkeon:Pinecone</c>, <c>Orkeon:LanceDb</c> — GAP-08). An unknown
/// type fails loudly at resolution with the list of supported types — never a silent fallback.
/// </remarks>
public sealed class RagStoreOptions
{
    /// <summary>
    /// Gets or sets the memory-provider type backing the RAG document store: <c>inmemory</c>,
    /// <c>in-memory</c>, <c>redis</c>, <c>sqlite</c>, <c>chromadb</c>, <c>chroma</c>,
    /// <c>pinecone</c>, <c>lancedb</c>, <c>lance</c>. <see langword="null"/> or empty selects
    /// the ambient container provider.
    /// </summary>
    public string? Provider { get; set; }
}
