using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Security;
using Orkeon.Tools.Web.Constants.Http;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web;

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>
/// Strongly-typed request for HttpApiTool.
/// </summary>
public sealed class HttpApiRequest
{
    /// <summary>Gets or sets the URL to call.</summary>
    [JsonPropertyName("url")]
    [FieldSchema(Description = "The URL to call", Example = "https://api.example.com/users", IsRequired = true)]
    public Uri? Url { get; set; }

    /// <summary>Gets or sets the HTTP method (default: GET).</summary>
    [JsonPropertyName("method")]
    [FieldSchema(Description = "HTTP method (default: GET)", IsRequired = false, Example = "GET", Default = "GET", Enum = new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" })]
    public string Method { get; set; } = HttpApiDefaults.DefaultMethod;

    /// <summary>Gets or sets optional HTTP headers as key-value pairs.</summary>
    [JsonPropertyName("headers")]
    [FieldSchema(Description = "Optional HTTP headers as key-value pairs", IsRequired = false)]
    public object? Headers { get; set; }

    /// <summary>Gets or sets the optional request body (for POST, PUT, PATCH).</summary>
    [JsonPropertyName("body")]
    [FieldSchema(Description = "Optional request body (for POST, PUT, PATCH)", IsRequired = false, Example = "{\"name\": \"Alice\"}")]
    public string? Body { get; set; }

    /// <summary>Gets or sets the Content-Type header for the request body (default: application/json).</summary>
    [JsonPropertyName("content_type")]
    [FieldSchema(Description = "Content-Type header for request body (default: application/json)", IsRequired = false, Example = "application/json", Default = "application/json")]
    public string ContentType { get; set; } = "application/json";

    /// <summary>Initializes a new instance of <see cref="HttpApiRequest"/>.</summary>
    public HttpApiRequest() { }
}

/// <summary>
/// Strongly-typed response for HttpApiTool.
/// </summary>
public sealed class HttpApiResponse
{
    /// <summary>Gets or sets the HTTP status code (e.g., 200, 404, 500).</summary>
    [JsonPropertyName("status_code")]
    [ReturnSchema(Description = "HTTP status code (e.g., 200, 404, 500)", Example = 200)]
    public int StatusCode { get; set; }

    /// <summary>Gets or sets the HTTP status reason phrase (e.g., OK, Not Found).</summary>
    [JsonPropertyName("status_description")]
    [ReturnSchema(Description = "HTTP status reason phrase (e.g., OK, Not Found)", Example = "OK")]
    public string StatusDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets the response body content as a string.</summary>
    [JsonPropertyName("body")]
    [ReturnSchema(Description = "Response body content as string", Example = "{\"id\": 1, \"name\": \"Alice\"}")]
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets or sets the response headers as key-value pairs.</summary>
    [JsonPropertyName("headers")]
    [ReturnSchema(Description = "Response headers as key-value pairs")]
    public Dictionary<string, string> Headers { get; init; } = [];

    /// <summary>Gets or sets a value indicating whether the response has a 2xx status code.</summary>
    [JsonPropertyName("is_success")]
    [ReturnSchema(Description = "Whether the response has a 2xx status code", Example = true)]
    public bool IsSuccess { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for making HTTP API calls (GET, POST, PUT, DELETE).
/// Returns response body, status code, and headers.
/// </summary>
public partial class HttpApiTool : HttpToolBase<HttpApiRequest, HttpApiResponse>
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"
    };

    /// <inheritdoc />
    public override string Name => "http_api";

    /// <inheritdoc />
    public override string Description => "Make HTTP API calls with support for GET, POST, PUT, DELETE, PATCH methods. Returns response body, status code, and headers.";

    /// <summary>Initializes a new instance of <see cref="HttpApiTool"/> with SSRF protection.</summary>
    /// <param name="urlValidator">URL validator for SSRF protection.</param>
    /// <param name="headerSanitizer">Header sanitizer to prevent header injection.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public HttpApiTool(IUrlValidator urlValidator, HttpHeaderSanitizer headerSanitizer,
        HttpClient? httpClient = null, ILogger<HttpApiTool>? logger = null)
        : base(urlValidator, headerSanitizer, httpClient, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="HttpApiTool"/> with IHttpClientFactory and SSRF protection.</summary>
    /// <param name="httpClientFactory">The HTTP client factory for proper socket management.</param>
    /// <param name="urlValidator">URL validator for SSRF protection.</param>
    /// <param name="headerSanitizer">Header sanitizer to prevent header injection.</param>
    /// <param name="logger">Optional logger instance.</param>
    public HttpApiTool(IHttpClientFactory httpClientFactory, IUrlValidator urlValidator, HttpHeaderSanitizer headerSanitizer,
        ILogger<HttpApiTool>? logger = null)
        : base(httpClientFactory, urlValidator, headerSanitizer, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="HttpApiTool"/> without SSRF protection (legacy).</summary>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public HttpApiTool(HttpClient? httpClient = null, ILogger<HttpApiTool>? logger = null)
        : base(httpClient, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(HttpApiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Url is null)
            return "URL parameter is required";

        var method = request.Method?.ToUpperInvariant() ?? HttpApiDefaults.DefaultMethod;
        if (!AllowedMethods.Contains(method))
            return $"Unsupported HTTP method: {method}. Allowed: {string.Join(", ", AllowedMethods)}";

        return null;
    }

    /// <inheritdoc />
    protected override Task<HttpApiResponse> ExecuteTypedAsync(
        HttpApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Url);
        return ExecuteTypedCoreAsync();

        async Task<HttpApiResponse> ExecuteTypedCoreAsync()
        {
        // SSRF protection: validate URL via inherited ValidateUrlAsync
        var urlValidation = await ValidateUrlAsync(request.Url, cancellationToken).ConfigureAwait(false);
        if (!urlValidation.IsAllowed)
            throw new InvalidOperationException($"URL blocked: {urlValidation.DenialReason}");

        var uri = urlValidation.ValidatedUri!;
        var method = request.Method?.ToUpperInvariant() ?? HttpApiDefaults.DefaultMethod;

        using var httpRequest = new HttpRequestMessage(new HttpMethod(method), uri);

        // Add custom headers
        if (request.Headers != null)
        {
            var headers = ParseHeaders(request.Headers);
            foreach (var (key, value) in headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }
        }

        // Add body for methods that support it
        if (request.Body != null)
        {
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var responseHeaders = new Dictionary<string, string>();
        foreach (var header in response.Headers)
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }
        foreach (var header in response.Content.Headers)
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }

        LogHttpRequestCompleted(method, request.Url, (int)response.StatusCode);

        return new HttpApiResponse
        {
            StatusCode = (int)response.StatusCode,
            StatusDescription = response.ReasonPhrase ?? "",
            Body = responseBody,
            Headers = responseHeaders,
            IsSuccess = response.IsSuccessStatusCode
        };
        }
    }

    private static Dictionary<string, string> ParseHeaders(object headersObj)
    {
        var headers = new Dictionary<string, string>();

        if (headersObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in jsonElement.EnumerateObject())
            {
                headers[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        else if (headersObj is IDictionary<string, object> dict)
        {
            foreach (var (key, value) in dict)
            {
                headers[key] = value?.ToString() ?? "";
            }
        }
        else if (headersObj is string headerStr && !string.IsNullOrWhiteSpace(headerStr))
        {
            try
            {
                using var doc = JsonDocument.Parse(headerStr);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    headers[prop.Name] = prop.Value.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // Ignore unparseable header strings
            }
        }

        return headers;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP {Method} {Url} -> {StatusCode}")]
    private partial void LogHttpRequestCompleted(string method, Uri url, int statusCode);
}
