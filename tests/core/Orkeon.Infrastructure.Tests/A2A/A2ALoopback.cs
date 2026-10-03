using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// An A2A server on a free loopback port (GAP-41), started through
/// <see cref="LoopbackPorts.StartAsync{T}(Func{int, T}, Func{T, CancellationToken, Task}, CancellationToken)"/>:
/// a port another process takes between the probe and the bind costs a retry, never the test.
/// Its clients call it on <see cref="LoopbackPorts.Host"/>, the address it listens on.
/// </summary>
internal static class A2ALoopback
{
    /// <summary>The options of a server listening on <see cref="LoopbackPorts.Host"/>, port <paramref name="port"/>.</summary>
    public static A2AOptions Options(int port) => new() { Host = LoopbackPorts.Host, Port = port };

    /// <summary>Starts the server <paramref name="create"/> builds over the options it is given.</summary>
    public static Task<(A2AServer Server, int Port)> StartAsync(Func<A2AOptions, A2AServer> create, CancellationToken ct) =>
        LoopbackPorts.StartAsync(port => create(Options(port)), static (server, token) => server.StartAsync(token), ct);
}
