using System.Text.Json;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Tools.Web;

/// <summary>
/// Web search tool using the Brave Search API.
/// Reuses the shared <see cref="WebSearchRequest"/> and <see cref="WebSearchResponse"/> contracts
/// so agents can switch between search providers without changing their code.
/// </summary>
/// <remarks>
/// <para><b>API Endpoint:</b> <c>GET https://api.search.brave.com/res/v1/web/search</c></para>
/// <para><b>Authentication:</b> <c>X-Subscription-Token</c> header with API key.</para>
/// <para><b>Free tier:</b> 2000 requests/month (no credit card required).</para>
/// <para><b>Differences from Tavily:</b> Brave does not provide a relevance score;
/// <see cref="WebSearchResult.Score"/> is always 0.0.</para>
/// </remarks>
[ToolContract("brave_search",
    Name = "brave_search",
    Description = "Search the web using Brave Search API. Returns titles, URLs, and content snippets.",
    Category = "Web Operations")]
public partial class BraveSearchTool : HttpToolBase<WebSearchRequest, WebSearchResponse>
{
    private const string BraveSearchEndpoint = "https://api.search.brave.com/res/v1/web/search";

    private readonly string _apiKey;

    /// <summary>
    /// Initializes a new instance of <see cref="BraveSearchTool"/>.
    /// </summary>
    /// <param name="apiKey">Brave Search API key (X-Subscription-Token).</param>
    /// <param name="httpClient">Optional pre-configured <see cref="HttpClient"/>.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="apiKey"/> is null or empty.</exception>
    public BraveSearchTool(string apiKey, HttpClient? httpClient = null, ILogger<BraveSearchTool>? logger = null)
        : base(httpClient, logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Brave Search API key cannot be null or empty.", nameof(apiKey));

        _apiKey = apiKey;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(WebSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        return null;
    }

    /// <inheritdoc />
    protected override Task<WebSearchResponse> ExecuteTypedAsync(
        WebSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<WebSearchResponse> ExecuteTypedCoreAsync()
        {
            var maxResults = request.MaxResults > 0 ? request.MaxResults : 5;

            var url = $"{BraveSearchEndpoint}?q={Uri.EscapeDataString(request.Query)}&count={maxResults}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Add("X-Subscription-Token", _apiKey);
            httpRequest.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Brave Search API returned {(int)response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var braveResponse = JsonSerializer.Deserialize<BraveApiResponse>(responseBody, JsonOptions);

            var results = new List<WebSearchResult>();

            if (braveResponse?.Web?.Results is { Count: > 0 })
            {
                foreach (var item in braveResponse.Web.Results)
                {
                    results.Add(new WebSearchResult
                    {
                        Title = item.Title ?? "",
                        Url = Uri.TryCreate(item.Url, UriKind.Absolute, out var u) ? u : null,
                        Content = item.Description ?? "",
                        Score = 0.0 // Brave does not provide relevance scores
                    });
                }
            }

            LogSearchCompleted(request.Query, results.Count);

            return new WebSearchResponse
            {
                Query = request.Query,
                Results = results,
                ResultCount = results.Count
            };
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Brave search for '{Query}' returned {Count} results")]
    private partial void LogSearchCompleted(string query, int count);

    // ── Brave API response models (internal) ──────────────────────────────
    //
    // System.Text.Json populates these by reflection, so the analyzer sees no assignment
    // and reports the properties as unassigned (S3459) and their accessors as unused
    // (S1144). Both are false positives — note they are already `init`, which does not
    // silence either rule (Sonar treats `init` as an unused private setter).
#pragma warning disable S3459, S1144 // Populated by System.Text.Json reflection, not by code

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Brave API response.")]
    private sealed class BraveApiResponse
    {
        public BraveWebResults? Web { get; init; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Brave API response.")]
    private sealed class BraveWebResults
    {
        public List<BraveWebResult>? Results { get; init; } = [];
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Brave API response.")]
    private sealed class BraveWebResult
    {
        public string? Title { get; init; }
        public string? Url { get; init; }
        public string? Description { get; init; }
    }

#pragma warning restore S3459, S1144
}
