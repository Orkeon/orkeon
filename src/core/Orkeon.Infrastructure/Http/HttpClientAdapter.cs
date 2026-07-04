using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.Infrastructure;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.Http;

/// <summary>
/// Adapter that implements the domain IHttpClient interface using the actual HttpClient.
/// Includes resilience patterns like retry and circuit breaker.
/// </summary>
public partial class HttpClientAdapter : IHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpClientAdapter> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _resiliencePolicy;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Initializes a new instance of <see cref="HttpClientAdapter"/>.</summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public HttpClientAdapter(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpClientAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient("Orkeon");
        _logger = logger;

        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = SerializationDefaults.JsonMaxDepth
        };

        // Configure resilience policy with retry and circuit breaker
        _resiliencePolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    LogHttpRequestFailedRetryAfter(retryCount, timespan.TotalSeconds, outcome.Result?.StatusCode.ToString() ?? "unknown");
                });
    }

    /// <inheritdoc />
    public Task<T> GetAsync<T>(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return GetCoreAsync();

        async Task<T> GetCoreAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogGetRequestTo(url);

            var response = await _resiliencePolicy.ExecuteAsync(async (ct) =>
                await _httpClient.GetAsync(url, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(content, _jsonOptions)!;
        }
    }

    /// <inheritdoc />
    public Task<TResponse> PostAsync<TRequest, TResponse>(
        Uri url,
        TRequest data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return PostCoreAsync();

        async Task<TResponse> PostCoreAsync()
        {
            LogPostRequestTo(url);

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, HttpDefaults.JsonContentType);

            var response = await _resiliencePolicy.ExecuteAsync(async (ct) =>
                await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions)!;
        }
    }

    /// <inheritdoc />
    public Task<TResponse> PutAsync<TRequest, TResponse>(
        Uri url,
        TRequest data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return PutCoreAsync();

        async Task<TResponse> PutCoreAsync()
        {
            LogPutRequestTo(url);

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, HttpDefaults.JsonContentType);

            var response = await _resiliencePolicy.ExecuteAsync(async (ct) =>
                await _httpClient.PutAsync(url, content, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions)!;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return DeleteCoreAsync();

        async Task DeleteCoreAsync()
        {
            LogDeleteRequestTo(url);

            var response = await _resiliencePolicy.ExecuteAsync(async (ct) =>
                await _httpClient.DeleteAsync(url, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }
    }

    /// <inheritdoc />
    public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return GetStringCoreAsync();

        async Task<string> GetStringCoreAsync()
        {
            LogGetStringRequestTo(url);

            var response = await _resiliencePolicy.ExecuteAsync(async (ct) =>
                await _httpClient.GetAsync(url, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "HTTP request failed. Retry {RetryCount} after {Timespan}s. Status: {StatusCode}")]
    private partial void LogHttpRequestFailedRetryAfter(int retryCount, double timespan, string statusCode);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "GET request to {Url}")]
    private partial void LogGetRequestTo(Uri url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "POST request to {Url}")]
    private partial void LogPostRequestTo(Uri url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "PUT request to {Url}")]
    private partial void LogPutRequestTo(Uri url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "DELETE request to {Url}")]
    private partial void LogDeleteRequestTo(Uri url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "GET string request to {Url}")]
    private partial void LogGetStringRequestTo(Uri url);

}
