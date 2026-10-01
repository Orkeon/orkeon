using Orkeon.Domain.Memory;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.TestDoubles;

public class TestMemoryProviderFactory : IMemoryProviderFactory
{
    private readonly Dictionary<string, IMemoryProvider> _providers = [];
    private IMemoryProvider? _defaultProvider;

    public void RegisterProvider(string type, IMemoryProvider provider)
    {
        _providers[type] = provider;
    }

    public void SetDefaultProvider(IMemoryProvider provider)
    {
        _defaultProvider = provider;
    }

    public IReadOnlyList<string> SupportedTypes => _providers.Keys.ToList();

    public IMemoryProvider GetProvider(string providerType)
    {
        if (_providers.TryGetValue(providerType, out var provider))
        {
            return provider;
        }

        if (_defaultProvider != null)
        {
            return _defaultProvider;
        }

        throw new NotSupportedException($"Provider type '{providerType}' is not supported");
    }
}
