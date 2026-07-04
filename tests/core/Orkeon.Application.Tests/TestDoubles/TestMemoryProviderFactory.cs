using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using MemoryProviderConfigDto = Orkeon.Application.Memory.MemoryProviderConfigDto;

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

    public IMemoryProvider Create(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory = null)
    {
        if (_providers.TryGetValue(config.Type, out var provider))
        {
            return provider;
        }

        if (_defaultProvider != null)
        {
            return _defaultProvider;
        }

        throw new NotSupportedException($"Provider type '{config.Type}' is not supported");
    }

    public System.Threading.Tasks.Task<IMemoryProvider> CreateAndInitializeAsync(
        MemoryProviderConfigDto config,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var provider = Create(config, loggerFactory);
        return System.Threading.Tasks.Task.FromResult(provider);
    }

    public IEnumerable<string> GetAvailableProviders()
    {
        return _providers.Keys;
    }
}
