using Microsoft.Extensions.Logging;
using System.Text.Json;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Infrastructure.MCP.Auth;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// MCP transport that communicates with a server over HTTP, posting one
/// JSON-RPC message per request (the Streamable HTTP shape, JSON-response
/// mode). Modern requests carry the required MCP headers, derived from the
/// message itself; an SSE-framed response body is unwrapped to its final
/// JSON-RPC message. Server-initiated streams (`subscriptions/listen`) are
/// not consumed.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class SseMcpTransport : IMcpTransport
{
    private readonly Uri _url;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IMcpRequestAuthenticator? _authenticator;
    private volatile bool _isConnected;
    private readonly ILogger _logger;
    private readonly System.Threading.Channels.Channel<JsonRpcNotification> _notifications;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <summary>
    /// The notifications the server sent ahead of the final message of an SSE-framed answer. The
    /// transport opens no stream of its own, so a server that only notifies on one is not heard.
    /// </summary>
    public System.Threading.Channels.ChannelReader<JsonRpcNotification> Notifications => _notifications.Reader;

    /// <summary>Initializes a new instance of <see cref="SseMcpTransport"/>.</summary>
    /// <param name="url">The HTTP endpoint URL for the MCP server.</param>
    /// <param name="httpClient">Optional HTTP client to use.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="authenticator">Optional authenticator applied to every request.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the self-created HttpClient is transferred to the _httpClient field (_ownsHttpClient=true) and disposed in DisposeAsync; the HttpClient in turn owns and disposes the SocketsHttpHandler.")]
    public SseMcpTransport(Uri url, HttpClient? httpClient = null, ILogger<SseMcpTransport>? logger = null,
        IMcpRequestAuthenticator? authenticator = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url;
        _authenticator = authenticator;
        _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _notifications = System.Threading.Channels.Channel.CreateBounded<JsonRpcNotification>(
            new System.Threading.Channels.BoundedChannelOptions(StdioMcpTransport.NotificationCapacity)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
            },
            dropped => LogNotificationDropped(dropped.Method, url));

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            // PooledConnectionLifetime recycles pooled connections so this self-owned
            // fallback client picks up DNS changes on long-lived agent processes (ANT-013).
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2)
            });
            _ownsHttpClient = true;
        }
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken ct = default)
    {
        _isConnected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendRequestCoreAsync();

        async Task<JsonRpcResponse> SendRequestCoreAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Transport is not connected.");

            var json = JsonSerializer.Serialize(request);
            using var message = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, HttpDefaults.JsonContentType)
            };
            AddMcpHeaders(message, request);
            if (_authenticator is not null)
                await _authenticator.ApplyAsync(message, ct).ConfigureAwait(false);

            var httpResponse = await _httpClient.SendAsync(message, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseJson = UnwrapSsePayload(httpResponse.Content.Headers.ContentType?.MediaType, responseBody, request.Method);
            var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson);

            if (response == null)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError(JsonRpcErrorCodes.InternalError, "Empty response from server")
                };
            }

            return response;
        }
    }

    /// <inheritdoc />
    public Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return SendNotificationCoreAsync();

        async Task SendNotificationCoreAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Transport is not connected.");

            var json = JsonSerializer.Serialize(notification);
            using var message = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, HttpDefaults.JsonContentType)
            };
            message.Headers.TryAddWithoutValidation(McpProtocol.MethodHeader, notification.Method);
            if (_authenticator is not null)
                await _authenticator.ApplyAsync(message, ct).ConfigureAwait(false);

            var httpResponse = await _httpClient.SendAsync(message, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Adds the Streamable HTTP request headers, derived from the JSON-RPC
    /// message itself: MCP-Protocol-Version (from `params._meta`), Mcp-Method,
    /// and Mcp-Name (from `params.name`, when present). Legacy requests carry
    /// no modern `_meta` and therefore no protocol-version header.
    /// </summary>
    private static void AddMcpHeaders(HttpRequestMessage message, JsonRpcRequest request)
    {
        message.Headers.TryAddWithoutValidation(McpProtocol.MethodHeader, request.Method);

        var version = McpProtocol.TryReadRequestedVersion(request.Params);
        if (version != null)
            message.Headers.TryAddWithoutValidation(McpProtocol.ProtocolVersionHeader, version);

        if (request.Params is { ValueKind: JsonValueKind.Object } p &&
            p.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String)
        {
            message.Headers.TryAddWithoutValidation(McpProtocol.NameHeader, name.GetString());
        }
    }

    /// <summary>
    /// When the server framed its answer as an SSE stream, extracts the last
    /// `data:` payload (the final JSON-RPC message of the request's stream) and hands the frames with
    /// a method and no id to <see cref="Notifications"/>; plain JSON bodies pass through untouched.
    /// </summary>
    private string UnwrapSsePayload(string? mediaType, string body, string requestMethod)
    {
        if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            return body;

        string? lastData = null;
        var sawNotification = false;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].TrimStart();
            if (TryReadNotification(data) is { } notification)
            {
                _notifications.Writer.TryWrite(notification);
                sawNotification = true;
            }
            else
            {
                lastData = data;
            }
        }

        if (lastData is null && sawNotification)
        {
            throw new InvalidOperationException(
                $"The server answered {requestMethod} with notifications only: its stream ended without the response to the request.");
        }

        return lastData ?? body;
    }

    private static JsonRpcNotification? TryReadNotification(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String &&
                !root.TryGetProperty("id", out _))
            {
                return JsonSerializer.Deserialize<JsonRpcNotification>(data);
            }
        }
        catch (JsonException)
        {
            // Not a JSON-RPC frame: left to the response deserializer, which names the failure.
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP server '{Url}' sent more notifications than the client reads: the oldest ('{Method}') was dropped")]
    private partial void LogNotificationDropped(string method, Uri url);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _isConnected = false;
        _notifications.Writer.TryComplete();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
