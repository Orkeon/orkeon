using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryProviderFactory"/>: the provider registered for a type, else the
/// default one; records the types it was asked for.
/// </summary>
public sealed class StubMemoryProviderFactory(IMemoryProvider defaultProvider, IReadOnlyDictionary<string, IMemoryProvider>? byType = null)
    : IMemoryProviderFactory
{
    public List<string> RequestedTypes { get; } = [];

    public IReadOnlyList<string> SupportedTypes => [.. byType?.Keys ?? [], "inmemory"];

    public IMemoryProvider GetProvider(string providerType)
    {
        RequestedTypes.Add(providerType);
        return byType is not null && byType.TryGetValue(providerType, out var provider) ? provider : defaultProvider;
    }
}
