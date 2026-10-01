using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Memory.Sqlite;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// The host's connection settings of every memory provider, as <see cref="MemoryProviderFactory"/>
/// consumes them: one section per provider (GAP-08). In a container they are the bound options of
/// <c>Orkeon:Redis</c>, <c>Orkeon:Sqlite</c>, <c>Orkeon:ChromaDb</c>, <c>Orkeon:Pinecone</c> and
/// <c>Orkeon:LanceDb</c>; a provider whose section is absent runs on its defaults.
/// </summary>
public sealed class MemoryProviderSettings
{
    /// <summary>Gets the Redis connection (<c>Orkeon:Redis</c>).</summary>
    public RedisMemoryOptions Redis { get; init; } = new();

    /// <summary>Gets the SQLite database (<c>Orkeon:Sqlite</c>); its <c>Data Source</c> is a virtual path on a writable mount.</summary>
    public SqliteMemoryOptions Sqlite { get; init; } = new();

    /// <summary>Gets the ChromaDB server (<c>Orkeon:ChromaDb</c>).</summary>
    public ChromaDbOptions ChromaDb { get; init; } = new();

    /// <summary>Gets the Pinecone index (<c>Orkeon:Pinecone</c>).</summary>
    public PineconeOptions Pinecone { get; init; } = new();

    /// <summary>Gets the remote LanceDB server (<c>Orkeon:LanceDb</c>).</summary>
    public LanceDbOptions LanceDb { get; init; } = new();
}
