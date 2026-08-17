using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Constants.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Constants.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// HTTP-based client for sending tasks to remote A2A agents.
/// When mTLS is configured the security handler (and its imported client certificate)
/// is built once and cached for the client's lifetime — dispose the client to release
/// them. Certificate rotation requires a new instance: the options snapshot is taken
/// at construction.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class A2AClient : IA2AClient, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly A2AOptions _options;
    private readonly A2ASecurityOptions? _security;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger _logger;

    // ANT-018: the mTLS handler is built once (lazy, thread-safe) and shared across
    // calls — one PFX read/import, pooled connections — instead of per call.
    private readonly SemaphoreSlim _secureHandlerLock = new(1, 1);
    private SocketsHttpHandler? _secureHandler;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>Initializes a new instance of <see cref="A2AClient"/>.</summary>
    public A2AClient(
        IHttpClientFactory httpClientFactory,
        IOptions<A2AOptions> options,
        IFileSystemService fileSystem,
        IOptions<A2ASecurityOptions>? security = null,
        ILogger<A2AClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _httpClientFactory = httpClientFactory;
        _options = options?.Value ?? new A2AOptions();
        _security = security?.Value;
        _fileSystem = fileSystem;
        _logger = logger ?? NullLogger<A2AClient>.Instance;
    }

    /// <summary>Initializes a new instance of <see cref="A2AClient"/> with direct options (useful for testing).</summary>
    public A2AClient(
        IHttpClientFactory httpClientFactory,
        A2AOptions options,
        IFileSystemService fileSystem,
        A2ASecurityOptions? security = null,
        ILogger<A2AClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _httpClientFactory = httpClientFactory;
        _options = options ?? new A2AOptions();
        _security = security;
        _fileSystem = fileSystem;
        _logger = logger ?? NullLogger<A2AClient>.Instance;
    }

    /// <summary>
    /// Whether a dedicated mTLS-configured handler is required (client certificate set).
    /// When <c>true</c> the client builds its own <see cref="HttpClient"/> from a
    /// security-aware handler instead of the named factory client.
    /// </summary>
    private bool RequiresSecureHandler =>
        _security != null
        && !string.IsNullOrWhiteSpace(_security.ClientCertificatePath);

    /// <inheritdoc />
    public Task<A2ATaskResponse> SendTaskAsync(
        Uri agentUrl, A2ATaskRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentUrl);
        ArgumentNullException.ThrowIfNull(request);

        return SendTaskAsyncCore(agentUrl, request, ct);
    }

    private async Task<A2ATaskResponse> SendTaskAsyncCore(
        Uri agentUrl, A2ATaskRequest request, CancellationToken ct)
    {
        var url = agentUrl.ToString().TrimEnd('/') + "/a2a/tasks/send";
        var requestUri = new Uri(url, UriKind.Absolute);

        LogSendingA2ATask(request.Id, url);

        using var client = await CreateClientAsync(ct).ConfigureAwait(false);
        using var content = JsonContent.Create(request, options: JsonOptions);
        using var response = await client.PostAsync(requestUri, content, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<A2ATaskResponse>(JsonOptions, ct).ConfigureAwait(false);
        return result ?? new A2ATaskResponse
        {
            TaskId = request.Id,
            Status = A2ATaskStatus.Failed,
            Error = "Empty response from remote agent"
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<A2ATaskUpdate> SendTaskStreamingAsync(
        Uri agentUrl, A2ATaskRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentUrl);
        ArgumentNullException.ThrowIfNull(request);

        return SendTaskStreamingAsyncCore(agentUrl, request, ct);
    }

    private async IAsyncEnumerable<A2ATaskUpdate> SendTaskStreamingAsyncCore(
        Uri agentUrl, A2ATaskRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = agentUrl.ToString().TrimEnd('/') + "/a2a/tasks/sendSubscribe";

        LogSendingStreamingA2ATask(request.Id, url);

        using var client = await CreateClientAsync(ct).ConfigureAwait(false);
        using var httpContent = JsonContent.Create(request, options: JsonOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = httpContent };
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(HttpDefaults.SseContentType));

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) break;

            if (!line.StartsWith(HttpDefaults.SseDataPrefix, StringComparison.Ordinal)) continue;

            var json = line[HttpDefaults.SseDataPrefix.Length..];
            if (string.IsNullOrWhiteSpace(json) || json == HttpDefaults.SseDoneMarker) continue;

            var update = TryDeserializeSseEvent(json);

            if (update != null)
            {
                yield return update;

                if (update.Status is A2ATaskStatus.Completed
                    or A2ATaskStatus.Failed
                    or A2ATaskStatus.Cancelled)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Attempts to deserialize an SSE event JSON string into an A2ATaskUpdate.
    /// Returns null and logs a warning on failure.
    /// </summary>
    private A2ATaskUpdate? TryDeserializeSseEvent(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<A2ATaskUpdate>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogFailedToParseSSEEvent(ex, json);
            return null;
        }
    }

    /// <inheritdoc />
    public Task CancelTaskAsync(Uri agentUrl, string taskId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        return CancelTaskAsyncCore();

        async Task CancelTaskAsyncCore()
        {
            var url = agentUrl.ToString().TrimEnd('/') + $"/a2a/tasks/{Uri.EscapeDataString(taskId)}";
            var requestUri = new Uri(url, UriKind.Absolute);

            LogCancellingA2ATask(taskId, url);

            using var client = await CreateClientAsync(ct).ConfigureAwait(false);
            using var response = await client.DeleteAsync(requestUri, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public Task<A2ATaskResponse> GetTaskStatusAsync(
        Uri agentUrl, string taskId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        return GetTaskStatusAsyncCore();

        async Task<A2ATaskResponse> GetTaskStatusAsyncCore()
        {
            var url = agentUrl.ToString().TrimEnd('/') + $"/a2a/tasks/{Uri.EscapeDataString(taskId)}";
            var requestUri = new Uri(url, UriKind.Absolute);

            LogGettingA2ATaskStatus(taskId, url);

            using var client = await CreateClientAsync(ct).ConfigureAwait(false);
            using var response = await client.GetAsync(requestUri, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<A2ATaskResponse>(JsonOptions, ct).ConfigureAwait(false);
            return result ?? new A2ATaskResponse
            {
                TaskId = taskId,
                Status = A2ATaskStatus.Failed,
                Error = "Empty response from remote agent"
            };
        }
    }

    /// <summary>
    /// Creates the <see cref="HttpClient"/> used for an A2A call. When a client certificate
    /// is configured (<see cref="A2ASecurityOptions.ClientCertificatePath"/>), the returned
    /// client wraps the single cached mTLS handler (built on first use: the certificate is
    /// read and imported once, connections are pooled across calls) and never owns it
    /// (<c>disposeHandler: false</c> — the per-call client stays cheap to dispose);
    /// otherwise the shared named factory client is returned.
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(CancellationToken ct)
    {
        HttpClient client;
        if (RequiresSecureHandler)
        {
            var handler = await GetOrCreateSecureHandlerAsync(ct).ConfigureAwait(false);
            client = new HttpClient(handler, disposeHandler: false);
        }
        else
        {
            client = _httpClientFactory.CreateClient("A2A");
        }

        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }

    private async Task<SocketsHttpHandler> GetOrCreateSecureHandlerAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_secureHandler != null)
            return _secureHandler;

        await _secureHandlerLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _secureHandler ??= await A2ASecurityHandlerFactory
                .CreateAsync(_security, _fileSystem, _logger, ct)
                .ConfigureAwait(false);
            return _secureHandler;
        }
        finally
        {
            _secureHandlerLock.Release();
        }
    }

    /// <summary>Disposes the cached mTLS handler and its imported client certificate.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Core disposal. Disposing the handler does not dispose the certificates it holds,
    /// so the imported client certificate is disposed explicitly (exactly once — under
    /// Windows a PFX import can materialize ephemeral key files on disk).
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;

        if (disposing)
        {
            var handler = _secureHandler;
            _secureHandler = null;
            if (handler != null)
            {
                var certificates = handler.SslOptions.ClientCertificates;
                handler.Dispose();
                if (certificates != null)
                {
                    foreach (X509Certificate certificate in certificates)
                        certificate.Dispose();
                }
            }

            _secureHandlerLock.Dispose();
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Sending A2A task {TaskId} to {Url}")]
    private partial void LogSendingA2ATask(string taskId, string url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Sending streaming A2A task {TaskId} to {Url}")]
    private partial void LogSendingStreamingA2ATask(string taskId, string url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse SSE event: {Json}")]
    private partial void LogFailedToParseSSEEvent(Exception ex, string json);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Cancelling A2A task {TaskId} at {Url}")]
    private partial void LogCancellingA2ATask(string taskId, string url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Getting A2A task status {TaskId} from {Url}")]
    private partial void LogGettingA2ATaskStatus(string taskId, string url);
}
