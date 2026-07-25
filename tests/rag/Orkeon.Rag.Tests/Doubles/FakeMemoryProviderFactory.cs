using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Memory;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Tests.Stores.Doubles;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-rolled <see cref="IMemoryProviderFactory"/> spy: records the configuration it
/// receives and returns a fixed <see cref="FakeMemoryProvider"/>, so DI tests can prove
/// the RAG document store resolves its provider through the factory.
/// </summary>
public sealed class FakeMemoryProviderFactory : IMemoryProviderFactory
{
    /// <summary>The provider returned by every call.</summary>
    public FakeMemoryProvider Provider { get; } = new();

    /// <summary>Configuration received by the last <see cref="Create"/> call.</summary>
    public MemoryProviderConfigDto? LastConfig { get; private set; }

    /// <summary>Number of <see cref="Create"/> calls.</summary>
    public int CreateCalls { get; private set; }

    public IMemoryProvider Create(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory = null)
    {
        CreateCalls++;
        LastConfig = config;
        return Provider;
    }

    public System.Threading.Tasks.Task<IMemoryProvider> CreateAndInitializeAsync(
        MemoryProviderConfigDto config,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(Create(config, loggerFactory));
}
