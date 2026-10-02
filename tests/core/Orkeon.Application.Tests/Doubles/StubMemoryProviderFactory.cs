using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryProviderFactory"/>: hands out one given provider for every type,
/// and records the types it was asked for.
/// </summary>
internal sealed class StubMemoryProviderFactory(IMemoryProvider provider) : IMemoryProviderFactory
{
    public List<string> RequestedTypes { get; } = [];

    public IReadOnlyList<string> SupportedTypes { get; } = ["inmemory"];

    public IMemoryProvider GetProvider(string providerType)
    {
        RequestedTypes.Add(providerType);
        return provider;
    }
}
