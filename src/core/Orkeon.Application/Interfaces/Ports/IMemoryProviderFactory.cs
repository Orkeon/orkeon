using Orkeon.Domain.Memory;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Hands out the memory provider of a given type. The type is all a caller chooses
/// (<c>Memory:Provider</c>, a crew's <c>memoryProvider:</c>, <c>Orkeon:Rag:Provider</c>): the
/// connection of each provider comes from the host's configuration section for that provider
/// (<c>Orkeon:Redis</c>, <c>Orkeon:Sqlite</c>, <c>Orkeon:ChromaDb</c>, <c>Orkeon:Pinecone</c>,
/// <c>Orkeon:LanceDb</c>), never from the caller.
/// </summary>
/// <remarks>
/// One instance per type: every caller asking for the same type receives the same provider (one
/// Redis connection, one HTTP client, one SQLite connection), and the factory owns those
/// instances — callers never dispose them. An unrecognized type resolves to the in-memory
/// provider with an explicit warning, never in silence.
/// </remarks>
public interface IMemoryProviderFactory
{
    /// <summary>
    /// Gets every type name the factory recognizes, aliases included (case-insensitive).
    /// </summary>
    IReadOnlyList<string> SupportedTypes { get; }

    /// <summary>
    /// Gets the shared provider of <paramref name="providerType"/>, creating it on first use.
    /// </summary>
    /// <param name="providerType">The provider type, e.g. <c>redis</c> or <c>sqlite</c>; empty selects the in-memory provider.</param>
    /// <returns>The provider instance shared by every caller asking for that type.</returns>
    IMemoryProvider GetProvider(string providerType);
}
