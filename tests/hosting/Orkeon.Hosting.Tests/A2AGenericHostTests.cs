using System.Net;
using Microsoft.Extensions.Hosting;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-10 acceptance: a generic host that calls <c>AddOrkeonA2A(o =&gt; o.EnableServer = true)</c>
/// serves the agent card once started — no start-up code resolves <c>IA2AServer</c> — and stops
/// serving it when the host stops.
/// </summary>
public sealed class A2AGenericHostTests
{
    [Fact]
    public async Task GenericHost_WithA2AServerEnabled_ServesTheAgentCard_UntilItStops()
    {
        var ct = TestContext.Current.CancellationToken;
        var (host, port) = await LoopbackPorts.StartAsync(
            HostServingA2AOn, static (built, token) => built.StartAsync(token), ct);
        using var _ = host;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var cardUrl = $"{LoopbackPorts.Host}:{port}/.well-known/agent.json";

        try
        {
            using var card = await http.GetAsync(cardUrl, ct);
            Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        // Assumes nobody takes the port in the instant after the stop: a residue GAP-41 accepts.
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(cardUrl, ct));
    }

    private static IHost HostServingA2AOn(int port)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddOrkeonA2A(o =>
        {
            o.EnableServer = true;
            o.Host = LoopbackPorts.Host;
            o.Port = port;
        });
        return builder.Build();
    }
}
