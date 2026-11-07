using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-03: the MCP HTTP client is named once, kept out of the LLM exchange log, and follows no redirect.</summary>
public sealed class McpHttpClientExclusionTests
{
    private sealed class RecordingExchangeLogger : ILlmExchangeLogger
    {
        private readonly List<LlmExchangeRecord> _records = [];

        public int Count
        {
            get { lock (_records) { return _records.Count; } }
        }

        public Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
        {
            lock (_records)
                _records.Add(exchange);
            return Task.CompletedTask;
        }
    }

    private static ServiceCollection Services(RecordingExchangeLogger exchanges)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/logs"));
        services.AddSingleton<ILlmExchangeLogger>(exchanges);
        services.AddLlmExchangeLogging("/logs");
        services.AddOrkeonMcp(new ConfigurationBuilder().Build());
        services.AddHttpClient(McpServiceExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler());
        return services;
    }

    private static async Task SendAuthorizedRequestAsync(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(McpServiceExtensions.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.example.test/rpc")
        {
            Content = new StringContent("{}"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer s3cret");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldWriteNoExchangeLogLine_WhenAnMcpRequestCarriesAnAuthorizationHeader()
    {
        var exchanges = new RecordingExchangeLogger();

        await SendAuthorizedRequestAsync(Services(exchanges));

        Assert.Equal(0, exchanges.Count);
    }

    [Fact]
    public async Task ShouldWriteAnExchangeLogLine_WhenTheExclusionIsRemoved()
    {
        var exchanges = new RecordingExchangeLogger();
        var services = Services(exchanges);
        services.RemoveAll<LlmExchangeLogExclusion>();

        await SendAuthorizedRequestAsync(services);

        Assert.Equal(1, exchanges.Count);
    }

    [Fact]
    public async Task ShouldNotFollowARedirect_SoStaticHeadersStayOnTheConfiguredHost()
    {
        var (listener, prefix) = LoopbackPorts.StartListener("/mcp/");
        var hits = new List<string>();
        var stop = new CancellationTokenSource();
        var loop = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                lock (hits)
                    hits.Add(context.Request.Url!.AbsolutePath);
                context.Response.StatusCode = (int)HttpStatusCode.Found;
                context.Response.RedirectLocation = new Uri(prefix, "elsewhere").ToString();
                context.Response.Close();
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOrkeonMcp(new ConfigurationBuilder().Build());
            using var provider = services.BuildServiceProvider();
            using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(McpServiceExtensions.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, prefix);
            request.Headers.TryAddWithoutValidation("X-Api-Key", "k3y");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            lock (hits)
                Assert.Equal(["/mcp/"], hits);
        }
        finally
        {
            await stop.CancelAsync();
            listener.Close();
            await loop;
            stop.Dispose();
        }
    }
}
