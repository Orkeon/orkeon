using Microsoft.Extensions.Configuration;

namespace Orkeon.Application.Memory;

/// <summary>
/// The memory provider types a host knows, with their aliases, and what each needs from its own host
/// section (GAP-08, GAP-40). One list for every place a type is named — <c>Memory:Provider</c>,
/// <c>Orkeon:Rag:Provider</c>, a crew's <c>memoryProvider:</c> — and for the factory that builds them:
/// a name outside it is refused, never served by the volatile in-memory provider.
/// </summary>
public static class MemoryProviderTypes
{
    /// <summary>The in-memory provider: volatile, the default when no type is named.</summary>
    public const string InMemory = "inmemory";

    /// <summary>Redis, connected from <c>Orkeon:Redis</c>.</summary>
    public const string Redis = "redis";

    /// <summary>SQLite, connected from <c>Orkeon:Sqlite</c>.</summary>
    public const string Sqlite = "sqlite";

    /// <summary>ChromaDB, connected from <c>Orkeon:ChromaDb</c>.</summary>
    public const string ChromaDb = "chromadb";

    /// <summary>Pinecone, connected from <c>Orkeon:Pinecone</c>.</summary>
    public const string Pinecone = "pinecone";

    /// <summary>LanceDB Cloud/Enterprise, connected from <c>Orkeon:LanceDb</c>.</summary>
    public const string LanceDb = "lancedb";

    private const string LanceDbEndpointKey = "Orkeon:LanceDb:Endpoint";
    private const string PineconeSection = "Orkeon:Pinecone";

    /// <summary>Every name a type may be given, aliases included, in the order the documentation lists them.</summary>
    public static IReadOnlyList<string> Supported { get; } =
        [InMemory, "in-memory", Redis, Sqlite, ChromaDb, "chroma", Pinecone, LanceDb, "lance"];

    /// <summary>
    /// The canonical type <paramref name="type"/> names — trimmed, case-insensitive, an alias resolved —
    /// <see cref="InMemory"/> for a blank one, null for a name no provider answers to.
    /// </summary>
    /// <param name="type">A type as written, or null.</param>
    public static string? Canonical(string? type) => (type ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "" or "INMEMORY" or "IN-MEMORY" => InMemory,
        "REDIS" => Redis,
        "SQLITE" => Sqlite,
        "CHROMADB" or "CHROMA" => ChromaDb,
        "PINECONE" => Pinecone,
        "LANCEDB" or "LANCE" => LanceDb,
        _ => null,
    };

    /// <summary>What a run says of a type no provider answers to: the key that names it, and the known types.</summary>
    /// <param name="key">Where the type is written: <c>Memory:Provider</c>, a crew's <c>memoryProvider:</c>, …</param>
    /// <param name="type">The type as written.</param>
    public static string UnknownMessage(string key, string type) =>
        $"{key} is '{type}', which is not a memory provider: write one of {string.Join(", ", Supported)}.";

    /// <summary>
    /// Why <paramref name="type"/>, named by <paramref name="key"/>, cannot be served on
    /// <paramref name="configuration"/>, or null when it can: a name no provider answers to; LanceDB
    /// without its endpoint; Pinecone without the key and index name it looks its host up with, or the
    /// host itself. Reads the provider's section only: nothing is built or connected.
    /// </summary>
    /// <param name="key">Where the type is written, for the message.</param>
    /// <param name="type">The type as written; blank is the in-memory provider.</param>
    /// <param name="configuration">The host configuration, holding the providers' sections.</param>
    public static string? Problem(string key, string? type, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        switch (Canonical(type))
        {
            case null:
                return UnknownMessage(key, type!.Trim());
            case LanceDb when string.IsNullOrWhiteSpace(configuration[LanceDbEndpointKey]):
                return $"{key} is '{type!.Trim()}', which needs {LanceDbEndpointKey}, the LanceDB Cloud/Enterprise REST " +
                       "endpoint (with its ApiKey): set it, or name another provider.";
            case Pinecone when !HasPineconeIndex(configuration):
                return $"{key} is '{type!.Trim()}', which needs {PineconeSection}:ApiKey and {PineconeSection}:IndexName " +
                       $"to look up the index host, or {PineconeSection}:Host: set them, or name another provider.";
            default:
                return null;
        }
    }

    /// <summary>The Pinecone index can be reached: its host is set, or its key and its name (which has a default) are.</summary>
    private static bool HasPineconeIndex(IConfiguration configuration)
    {
        var section = configuration.GetSection(PineconeSection);
        if (!string.IsNullOrWhiteSpace(section["Host"]))
            return true;

        var indexName = section.GetSection("IndexName");
        var namesIndex = indexName.Value is null || !string.IsNullOrWhiteSpace(indexName.Value);
        return namesIndex && !string.IsNullOrWhiteSpace(section["ApiKey"]);
    }
}
