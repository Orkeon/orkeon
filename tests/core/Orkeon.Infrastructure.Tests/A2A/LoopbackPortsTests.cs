using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Tests.Shared.Network;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// GAP-41 — the start every test server goes through. A port is asked of the system, given back,
/// then bound by the server: on a machine running several test passes at once, another process
/// can take it in between, and the server failed on « Address already in use ». The start now
/// tries again on another port; a refusal that is no conflict still leaves at once.
/// </summary>
public sealed class LoopbackPortsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_port_taken_before_the_server_binds_it_is_tried_again_on_another_and_the_card_is_served()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(),
            "Under Windows, HTTP.sys binds the listener: a socket this process holds is not the conflict it meets.");

        // Another program takes the first port the probe proposes before the server binds it.
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        var takenPort = ((IPEndPoint)taken.LocalEndpoint).Port;
        var proposed = new List<int>();
        int Probe()
        {
            var port = proposed.Count == 0 ? takenPort : LoopbackPorts.Probe();
            proposed.Add(port);
            return port;
        }

        var (server, port) = await LoopbackPorts.StartAsync(
            p => new A2AServer(A2ALoopback.Options(p), new StubA2ATaskRouter()),
            static (s, ct) => s.StartAsync(ct),
            Probe,
            Ct);
        try
        {
            Assert.Equal(2, proposed.Count);
            Assert.Equal(proposed[1], port);
            Assert.NotEqual(takenPort, port);

            using var http = new HttpClient { Timeout = Polling.DefaultTimeout };
            using var card = JsonDocument.Parse(await http.GetStringAsync(
                new Uri(LoopbackPorts.BaseAddress(port), ".well-known/agent.json"), Ct));
            Assert.Equal("Orkeon", card.RootElement.GetProperty("name").GetString());
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_refusal_that_is_no_conflict_leaves_at_the_first_attempt()
    {
        // A declared scheme without its validator: the configuration is refused, on any port.
        var proposed = 0;
        int Probe()
        {
            proposed++;
            return LoopbackPorts.Probe();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoopbackPorts.StartAsync(
            port => new A2AServer(
                A2ALoopback.Options(port), new StubA2ATaskRouter(),
                security: new A2ASecurityOptions { AllowedAuthSchemes = { "Bearer" } }),
            static (server, ct) => server.StartAsync(ct),
            Probe,
            Ct));

        Assert.Contains("Bearer", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, proposed);
    }

    [Fact]
    public async Task Ten_conflicts_in_a_row_let_the_last_one_go_and_every_server_built_is_disposed()
    {
        var built = new List<DisposableServer>();
        var conflicts = new List<HttpListenerException>();

        var error = await Assert.ThrowsAsync<HttpListenerException>(() => LoopbackPorts.StartAsync(
            _ =>
            {
                var server = new DisposableServer();
                built.Add(server);
                return server;
            },
            (_, _) =>
            {
                var conflict = new HttpListenerException(98, $"Address already in use ({conflicts.Count + 1})");
                conflicts.Add(conflict);
                return Task.FromException(conflict);
            },
            Ct));

        Assert.Equal(LoopbackPorts.Attempts, conflicts.Count);
        Assert.Same(conflicts[^1], error);
        Assert.Equal(LoopbackPorts.Attempts, built.Count);
        Assert.All(built, server => Assert.True(server.Disposed));
    }

    [Theory]
    [InlineData(nameof(HttpListenerException))]
    [InlineData(nameof(SocketException))]
    public async Task A_conflict_inside_the_exception_a_host_throws_is_still_a_conflict(string systemRefusal)
    {
        // orkeon-host refuses a listener the system refused with an exception of its own, which
        // keeps the system's as its inner one.
        Exception refusal = systemRefusal == nameof(SocketException)
            ? new SocketException((int)SocketError.AddressAlreadyInUse)
            : new HttpListenerException(98, "Address already in use");
        var attempts = 0;

        var (server, _) = await LoopbackPorts.StartAsync(
            _ => new DisposableServer(),
            (_, _) => ++attempts == 1
                ? Task.FromException(new InvalidOperationException("The system refused the listener.", refusal))
                : Task.CompletedTask,
            Ct);

        Assert.Equal(2, attempts);
        Assert.False(server.Disposed);
        await server.DisposeAsync();
    }

    /// <summary>A server whose start the test decides, telling whether it was disposed.</summary>
    private sealed class DisposableServer : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
