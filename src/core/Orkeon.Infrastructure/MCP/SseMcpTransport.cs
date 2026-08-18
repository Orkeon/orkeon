using Microsoft.Extensions.Logging;
using System.Text.Json;
using Orkeon.Infrastructure.Constants.Llm;
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
public class SseMcpTransport : IMcpTransport
{
    private readonly Uri _url;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private volatile bool _isConnected;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <summary>Initializes a new instance of <see cref="SseMcpTransport"/>.</summary>
    /// <param name="url">The HTTP endpoint URL for the MCP server.</param>
    /// <param name="httpClient">Optional HTTP client to use.</param>
    /// <param name="logger">Optional logger.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the self-created HttpClient is transferred to the _httpClient field (_ownsHttpClient=true) and disposed in DisposeAsync; the HttpClient in turn owns and disposes the SocketsHttpHandler.")]
    public SseMcpTransport(Uri url, HttpClient? httpClient = null, ILogger<SseMcpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url;

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

            var httpResponse = await _httpClient.SendAsync(message, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var responseJson = UnwrapSsePayload(httpResponse.Content.Headers.ContentType?.MediaType, responseBody);
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
    /// `data:` payload (the final JSON-RPC message of the request's stream);
    /// plain JSON bodies pass through untouched.
    /// </summary>
    private static string UnwrapSsePayload(string? mediaType, string body)
    {
        if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            return body;

        string? lastData = null;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("data:", StringComparison.Ordinal))
                lastData = line["data:".Length..].TrimStart();
        }

        return lastData ?? body;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _isConnected = false;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
