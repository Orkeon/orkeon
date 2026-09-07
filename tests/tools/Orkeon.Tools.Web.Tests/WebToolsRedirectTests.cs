using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Abstractions.Security;
using Orkeon.Tools.Web.DependencyInjection;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Redirect regression tests for the shipped DI wiring (D9-01). SECURITY.md
/// promises that redirects are never followed automatically: a 302 answered by a
/// validated public host would otherwise carry the request to an internal address
/// the <see cref="IUrlValidator"/> never saw. These tests exercise the composition
/// a host really builds — <c>AddHttpClient()</c> plus <c>AddOrkeonWebTools()</c> —
/// not a hand-made client, because that is where the guarantee was lost.
/// </summary>
public sealed class WebToolsRedirectTests
{
    [Fact]
    public async Task WebToolsClient_ShouldNotFollowRedirect_WhenResolvedFromTheDefaultWiring()
    {
        using var server = new RedirectingLoopbackServer();

        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonWebTools();
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(WebToolExtensions.HttpClientName);
        using var response = await client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/redirect"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.DoesNotContain("/secret", server.ServedPaths);
    }

    [Fact]
    public async Task HttpApiTool_ShouldIssueItsRequests_ThroughTheWebToolsNamedClient()
    {
        // The recorder sits on the named client only. If the tool keeps resolving the
        // ambient HttpClient registered by AddHttpClient(), nothing is recorded — and
        // that ambient client is the one whose handler follows redirects.
        var recorded = new List<Uri>();

        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddHttpClient(WebToolExtensions.HttpClientName)
            .AddHttpMessageHandler(() => new RecordingHandler(recorded));
        services.AddSingleton<IUrlValidator>(new AllowAllUrlValidator());
        services.AddSingleton(new HttpHeaderSanitizer());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonWebTools();
        using var provider = services.BuildServiceProvider();

        var tool = provider.GetServices<Orkeon.Domain.Tools.IBaseTool>().OfType<HttpApiTool>().Single();
        await tool.CallAsync(
            new ToolCallRequest(
                ToolName: "http_api",
                Parameters: new Dictionary<string, object?> { [ParamUrl] = "http://127.0.0.1:9/api" }),
            TestContext.Current.CancellationToken);

        Assert.Single(recorded);
    }

    /// <summary>
    /// Hand-written double: a loopback HTTP server answering <c>/redirect</c> with a
    /// 302 pointing at its own <c>/secret</c> path, and recording every path it was
    /// asked for. A client that follows redirects therefore leaves a trace.
    /// </summary>
    private sealed class RedirectingLoopbackServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly List<string> _servedPaths = [];
        private readonly Task _pump;

        public RedirectingLoopbackServer()
        {
            Port = ReserveLoopbackPort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _pump = PumpAsync();
        }

        /// <summary>Loopback port the server listens on.</summary>
        public int Port { get; }

        /// <summary>Snapshot of the paths served so far, oldest first.</summary>
        public IReadOnlyList<string> ServedPaths
        {
            get
            {
                lock (_servedPaths)
                {
                    return [.. _servedPaths];
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _pump.GetAwaiter().GetResult();
            _listener.Close();
        }

        /// <summary>
        /// Asks the OS for a free loopback port and releases it immediately: the
        /// listener binds it back straight away, and nothing else in this test
        /// process binds ephemeral ports.
        /// </summary>
        private static int ReserveLoopbackPort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task PumpAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                lock (_servedPaths)
                {
                    _servedPaths.Add(path);
                }

                if (path == "/redirect")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Found;
                    context.Response.Headers["Location"] = $"http://127.0.0.1:{Port}/secret";
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }

                context.Response.Close();
            }
        }
    }

    /// <summary>
    /// Hand-written double: records the outgoing URI and answers without ever
    /// reaching the network, so the assertion is about the wiring, not about a
    /// reachable server.
    /// </summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly List<Uri> _recorded;

        public RecordingHandler(List<Uri> recorded) => _recorded = recorded;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_recorded)
            {
                _recorded.Add(request.RequestUri!);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
