using Microsoft.Extensions.Hosting;
using Orkeon.Hosting;
using Orkeon.Hosting.ToolBridge;

namespace Orkeon.Host;

/// <summary>
/// Bridges the tools <c>Orkeon:Tools:Bridge</c> selects when the host starts (#445), right after the MCP servers
/// connect: the hosted services start in order, so no message loads a crew before its bridged tools are in place. A
/// bridge that refuses the start is a refused configuration: the host exits with
/// <see cref="HostConfigurationException.ExitCode"/> and the reason.
/// </summary>
internal sealed class ToolBridgeConnectionService : IHostedService
{
    private readonly IServiceProvider _services;

    /// <summary>Builds the service over the host's container.</summary>
    public ToolBridgeConnectionService(IServiceProvider services)
        => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ToolBridgeStartup.ConnectAsync(_services, cancellationToken).ConfigureAwait(false);
        }
        catch (RunnerSettingsException ex)
        {
            throw new HostConfigurationException(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
