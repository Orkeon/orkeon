using Microsoft.Extensions.Hosting;
using Orkeon.Application.Interfaces.AgentCommunication;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Starts the <see cref="IA2AServer"/> with the host and stops it on shutdown (GAP-10).
/// Registered by <c>AddOrkeonA2A</c> when <see cref="A2AOptions.EnableServer"/> is set, so a
/// generic host serves <c>/.well-known/agent.json</c> without any start-up code of its own.
/// A process that never runs the host (a one-shot runner) never starts the server either.
/// A start failure — a declared auth scheme without a validator, mutual TLS without a trust
/// anchor — fails the host's start: the server never listens unprotected.
/// </summary>
internal sealed class A2AServerHostedService : IHostedService
{
    private readonly IA2AServer _server;

    public A2AServerHostedService(IA2AServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _server.StopAsync(cancellationToken);
}
