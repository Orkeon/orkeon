using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using MemoryProviderConfigDto = Orkeon.Application.Memory.MemoryProviderConfigDto;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Factory interface for creating memory providers.
/// This allows the Infrastructure layer to provide implementations
/// while maintaining proper architectural boundaries.
/// </summary>
public interface IMemoryProviderFactory
{
    /// <summary>
    /// Creates a memory provider based on configuration.
    /// </summary>
    /// <param name="config">Memory provider configuration</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    /// <returns>A memory provider instance</returns>
    IMemoryProvider Create(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory = null);

    /// <summary>
    /// Creates and initializes a memory provider.
    /// </summary>
    /// <param name="config">Memory provider configuration</param>
    /// <param name="loggerFactory">Optional logger factory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialized memory provider instance</returns>
    System.Threading.Tasks.Task<IMemoryProvider> CreateAndInitializeAsync(
        MemoryProviderConfigDto config,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default);
}
