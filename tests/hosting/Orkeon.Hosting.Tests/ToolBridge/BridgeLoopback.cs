using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>Both sides of the tool bridge in one process: a server on a free loopback port, a caller's link to it.</summary>
internal static class BridgeLoopback
{
    /// <summary>The token both sides read.</summary>
    public const string Token = "test-bridge-token";

    /// <summary>Reads <see cref="Token"/> under the default variable, nothing else.</summary>
    public static string? Environment(string name) => name == ToolBridgeProtocol.DefaultTokenEnvVar ? Token : null;

    /// <summary>Starts a server over <paramref name="tools"/>, serving <paramref name="allow"/>.</summary>
    public static async Task<(ToolBridgeServer Server, Uri Address)> StartServerAsync(
        IEnumerable<IBaseTool> tools, string[] allow, FakeAuditLogger? audit = null, long? maxRequestBytes = null, int? port = null,
        BridgeFileSystem? files = null, TimeProvider? time = null, Action<ToolBridgeServerOptions>? configure = null,
        Microsoft.Extensions.Logging.ILogger? logger = null, Func<string, string?>? environment = null)
    {
        var registry = new ToolRegistry(tools);
        ToolBridgeServer Create(int p)
        {
            var options = new ToolBridgeServerOptions { Prefix = LoopbackPorts.BaseAddress(p).ToString() };
            foreach (var name in allow)
                options.Allow.Add(name);
            if (maxRequestBytes is { } max)
                options.MaxRequestBytes = max;
            configure?.Invoke(options);
            return new ToolBridgeServer(registry, options, environment ?? Environment, audit, logger ?? NullLogger.Instance, files?.Scope, files?.Boot, time);
        }

        var ct = TestContext.Current.CancellationToken;
        var (server, chosen) = port is { } fixedPort
            ? await LoopbackPorts.StartAsync(Create, (s, c) => s.StartAsync(c), () => fixedPort, ct)
            : await LoopbackPorts.StartAsync(Create, (s, c) => s.StartAsync(c), ct);
        return (server, LoopbackPorts.BaseAddress(chosen));
    }

    /// <summary>
    /// A raw HTTP client of the bridge at <paramref name="address"/>, under <paramref name="bearer"/>: it opens a session
    /// at its first POST that names none, and names it on that POST and each later one.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The client owns its handler and disposes it.")]
    public static HttpClient Http(Uri address, string? bearer = Token)
    {
        var client = new HttpClient(new SessionHandler(), disposeHandler: true) { BaseAddress = new Uri(address, ToolBridgeProtocol.BasePath) };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    /// <summary>A raw HTTP client of the bridge at <paramref name="address"/> that names no session of its own.</summary>
    public static HttpClient Plain(Uri address, string? bearer = Token)
    {
        var client = new HttpClient { BaseAddress = new Uri(address, ToolBridgeProtocol.BasePath) };
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    /// <summary>Opens a session on the bridge through <paramref name="client"/>; null when the bridge refused it.</summary>
    public static async Task<string?> OpenSessionAsync(HttpClient client)
    {
        using var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(ToolBridgeProtocol.SessionEndpoint, body, TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var opened = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<BridgeSessionResponse>(
            response.Content, ToolBridgeProtocol.JsonOptions, TestContext.Current.CancellationToken);
        return opened?.SessionId;
    }

    /// <summary>Posts <paramref name="json"/> to <paramref name="endpoint"/> under the session <paramref name="session"/>.</summary>
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string endpoint, string? session, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        if (session is not null)
            request.Headers.Add(ToolBridgeProtocol.SessionHeader, session);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "A delegating handler disposes its inner handler.")]
    private sealed class SessionHandler() : DelegatingHandler(new HttpClientHandler())
    {
        private readonly SemaphoreSlim _opening = new(1, 1);
        private string? _session;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && !request.Headers.Contains(ToolBridgeProtocol.SessionHeader)
                && !request.RequestUri!.AbsolutePath.EndsWith("/" + ToolBridgeProtocol.SessionEndpoint, StringComparison.Ordinal))
            {
                await _opening.WaitAsync(cancellationToken);
                try
                {
                    _session ??= await OpenAsync(request, cancellationToken);
                }
                finally
                {
                    _opening.Release();
                }

                if (_session is not null)
                    request.Headers.Add(ToolBridgeProtocol.SessionHeader, _session);
            }

            return await base.SendAsync(request, cancellationToken);
        }

        private async Task<string?> OpenAsync(HttpRequestMessage like, CancellationToken cancellationToken)
        {
            using var open = new HttpRequestMessage(HttpMethod.Post, new Uri(like.RequestUri!, ToolBridgeProtocol.SessionEndpoint))
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            open.Headers.Authorization = like.Headers.Authorization;
            using var response = await base.SendAsync(open, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            var opened = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<BridgeSessionResponse>(
                response.Content, ToolBridgeProtocol.JsonOptions, cancellationToken);
            return opened?.SessionId;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _opening.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>The caller's options for the bridge at <paramref name="address"/>.</summary>
    public static ToolBridgeClientOptions ClientOptions(Uri address, params string[] allow)
    {
        var options = new ToolBridgeClientOptions { Address = address.ToString() };
        foreach (var name in allow)
            options.Allow.Add(name);
        return options;
    }

    /// <summary>Connects a link of <paramref name="options"/> over <paramref name="registry"/>.</summary>
    public static async Task<ToolBridgeLink> ConnectAsync(
        ToolRegistry registry, ToolBridgeClientOptions options, string? token = Token, IFileSystemService? fileSystem = null, TimeProvider? time = null,
        Microsoft.Extensions.Logging.ILogger? operatorLogger = null)
    {
        var link = new ToolBridgeLink(
            options, fileSystem ?? new FakeFileSystemService(), name => name == ToolBridgeProtocol.DefaultTokenEnvVar ? token : null, time);
        await link.ConnectAsync(registry, null, null, NullLogger.Instance, operatorLogger ?? NullLogger.Instance, TestContext.Current.CancellationToken);
        return link;
    }
}

/// <summary>
/// The file system of the bridge's side in a test: a real <see cref="FileSystemService"/> over no mount of its own,
/// reading the ambient scope a bridged call enters over its scratch.
/// </summary>
internal sealed class BridgeFileSystem : IDisposable
{
    /// <summary>The ambient scope the service reads.</summary>
    public AsyncLocalFileSystemScope Scope { get; } = new();

    /// <summary>Builds the file system over <paramref name="boot"/>, the bridge's own mounts (none by default).</summary>
    public BridgeFileSystem(params FileSystemMount[] boot) => Boot = new FileSystemRegistry([.. boot]);

    /// <summary>The bridge's own mounts.</summary>
    public FileSystemRegistry Boot { get; }

    /// <summary>The service the bridge's tools write and read through.</summary>
    public FileSystemService Service => _service ??= new FileSystemService(
        Boot, new StubPathValidator().AllowAll(), NullLogger<FileSystemService>.Instance, Scope);

    private FileSystemService? _service;

    /// <inheritdoc />
    public void Dispose() => Boot.Dispose();
}
