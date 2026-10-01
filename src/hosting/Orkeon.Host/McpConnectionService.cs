using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Hosting;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Host;

/// <summary>
/// Connects the MCP servers the settings declare (<c>MCP:Servers</c>) when the service starts,
/// and disconnects them when it stops (GAP-11).
/// <para>
/// The runners connect them in their kickoff (<see cref="McpStartup"/>); the daemon never runs
/// a kickoff, so until this service it registered the MCP provider and connected nothing — the
/// same settings gave <c>orkeon run</c> the servers' tools and the host none. The connection is
/// awaited in <see cref="StartAsync"/>, and the service is registered first: the hosted
/// services start in order, so no chat message can load a crew — and resolve its tools under
/// <c>StrictTools</c> — before the servers' tools are in the registry. A server that cannot be
/// connected costs one error line and the host starts without it, exactly as a run does.
/// </para>
/// </summary>
internal sealed class McpConnectionService : IHostedService
{
    private readonly IServiceProvider _services;

    /// <summary>Builds the service over the host's container.</summary>
    public McpConnectionService(IServiceProvider services)
        => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => McpStartup.ConnectConfiguredServersAsync(_services, cancellationToken);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Registered first, so stopped last (LIFO): the drain has finished before the servers'
        // tools leave the registry and their processes are stopped. Disposal is idempotent —
        // the container's own disposal at exit finds nothing left to do.
        if (_services.GetService<McpToolProvider>() is { } provider)
            await provider.DisposeAsync().ConfigureAwait(false);
    }
}
