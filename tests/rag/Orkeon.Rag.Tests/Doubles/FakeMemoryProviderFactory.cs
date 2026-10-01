using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Tests.Stores.Doubles;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-rolled <see cref="IMemoryProviderFactory"/> spy: records the types it is asked for and
/// returns a fixed <see cref="FakeMemoryProvider"/> (one shared instance, like the real factory),
/// so DI tests can prove the RAG document store resolves its provider through the factory.
/// </summary>
public sealed class FakeMemoryProviderFactory : IMemoryProviderFactory
{
    /// <summary>The provider returned by every call.</summary>
    public FakeMemoryProvider Shared { get; } = new();

    /// <summary>Every type passed to <see cref="GetProvider"/>, in order.</summary>
    public List<string> RequestedTypes { get; } = [];

    /// <summary>The aliases of the real factory.</summary>
    public IReadOnlyList<string> SupportedTypes { get; } =
        ["inmemory", "in-memory", "redis", "sqlite", "chromadb", "chroma", "pinecone", "lancedb", "lance"];

    public IMemoryProvider GetProvider(string providerType)
    {
        RequestedTypes.Add(providerType);
        return Shared;
    }
}
