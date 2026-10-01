using System.Net;
using Microsoft.Extensions.Hosting;
using Orkeon.Infrastructure.AgentCommunication;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-10 acceptance: a generic host that calls <c>AddOrkeonA2A(o =&gt; o.EnableServer = true)</c>
/// serves the agent card once started — no start-up code resolves <c>IA2AServer</c> — and stops
/// serving it when the host stops.
/// </summary>
public sealed class A2AGenericHostTests
{
    private static int GetFreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task GenericHost_WithA2AServerEnabled_ServesTheAgentCard_UntilItStops()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = GetFreePort();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddOrkeonA2A(o =>
        {
            o.EnableServer = true;
            o.Port = port;
        });

        using var host = builder.Build();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var cardUrl = $"http://localhost:{port}/.well-known/agent.json";

        await host.StartAsync(ct);
        try
        {
            using var card = await http.GetAsync(cardUrl, ct);
            Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(cardUrl, ct));
    }
}
