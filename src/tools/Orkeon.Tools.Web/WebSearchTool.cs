using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web;

/// <summary>
/// Tool for performing web searches using the Tavily Search API.
/// Returns relevant search results with titles, URLs, content snippets, and relevance scores.
/// </summary>
[ToolContract("web_search",
    Name = "web_search",
    Description = "Search the web using the Tavily Search API. Returns relevant results with titles, URLs, content snippets, and scores.",
    Category = "Web Operations")]
public partial class WebSearchTool : HttpToolBase<WebSearchRequest, WebSearchResponse>
{
    private const string TavilyApiEndpoint = "https://api.tavily.com/search";

    /// <summary>
    /// The secret name used to retrieve the Tavily API key from the secret provider.
    /// Maps to environment variable ORKEON_TAVILY_API_KEY when using EnvironmentSecretProvider.
    /// </summary>
    public const string TavilyApiKeySecretName = "TAVILY_API_KEY";

    private readonly ISecretProvider _secretProvider;

    /// <summary>Initializes a new instance of <see cref="WebSearchTool"/>.</summary>
    /// <param name="secretProvider">The secret provider used to retrieve the Tavily API key at execution time.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public WebSearchTool(ISecretProvider secretProvider, HttpClient? httpClient = null, ILogger<WebSearchTool>? logger = null)
        : base(httpClient, logger)
    {
        _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
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
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Request content ownership is transferred to HttpClient.PostAsync.")]
    protected override Task<WebSearchResponse> ExecuteTypedAsync(
        WebSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<WebSearchResponse> ExecuteTypedCoreAsync()
        {
            // Retrieve the API key at execution time only — never stored as a field
            using var secret = await _secretProvider.GetSecretAsync(TavilyApiKeySecretName, cancellationToken).ConfigureAwait(false);
            var apiKey = secret.Value;

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Tavily API key is empty. Configure the secret '" + TavilyApiKeySecretName + "'.");

            var requestBody = new Dictionary<string, object>
            {
                ["api_key"] = apiKey,
                ["query"] = request.Query,
                ["max_results"] = request.MaxResults,
                ["search_depth"] = request.SearchDepth
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(new Uri(TavilyApiEndpoint), jsonContent, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;
                throw new HttpRequestException(
                    $"Tavily API request failed with status {statusCode}: {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var tavilyResponse = JsonSerializer.Deserialize<TavilyApiResponse>(responseJson);

            var results = tavilyResponse?.Results?.Select(r => new WebSearchResult
            {
                Title = r.Title ?? "",
                Url = Uri.TryCreate(r.Url, UriKind.Absolute, out var u) ? u : null,
                Content = r.Content ?? "",
                Score = r.Score
            }).ToList() ?? [];

            LogSearchCompleted(request.Query, results.Count);

            return new WebSearchResponse
            {
                Results = results,
                ResultCount = results.Count,
                Query = request.Query
            };
        }
    }

    // ── Tavily API response model ────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Tavily API response.")]
    private sealed class TavilyApiResponse
    {
        [JsonPropertyName("results")]
        public List<TavilyResult>? Results { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the Tavily API response.")]
    private sealed class TavilyResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Web search completed for query: \"{Query}\" ({ResultCount} results)")]
    private partial void LogSearchCompleted(string query, int resultCount);
}
