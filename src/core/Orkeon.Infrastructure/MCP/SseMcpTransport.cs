using Microsoft.Extensions.Logging;
using System.Text.Json;
using Orkeon.Infrastructure.Constants.Llm;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// MCP transport that communicates with a server over HTTP,
/// posting JSON-RPC requests and receiving JSON responses.
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
            using var content = new StringContent(json, System.Text.Encoding.UTF8, HttpDefaults.JsonContentType);

            var httpResponse = await _httpClient.PostAsync(_url, content, ct).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var responseJson = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
