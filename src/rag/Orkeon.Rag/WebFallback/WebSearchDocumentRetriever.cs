using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.WebFallback;

/// <summary>
/// Secure web search fallback for the corrective RAG graph (RAG-06/C1).
/// Queries a SearxNG-compatible JSON search endpoint, downloads each result page
/// through the existing <see cref="WebPageLoader"/>, and gates every document
/// through <see cref="PromptInjectionDocumentValidator"/>:
/// <see cref="PromptInjectionVerdict.Rejected"/> documents never leave this class
/// (traced in logs with their reasons); <see cref="PromptInjectionVerdict.Suspicious"/>
/// documents are flagged in metadata or discarded per
/// <see cref="WebSearchRetrieverOptions.SuspiciousAction"/> — never silently cleaned.
/// Strict opt-in: disabled by default; enabled without an endpoint is loudly logged
/// and returns nothing. Network/timeout failures degrade to an empty list (warning),
/// caller cancellation always propagates.
/// </summary>
public sealed partial class WebSearchDocumentRetriever
{
    /// <summary>Named <see cref="HttpClient"/> used for the search API call.</summary>
    public const string SearchClientName = "orkeon-rag-webfallback-search";

    /// <summary>Named <see cref="HttpClient"/> used to download result pages.</summary>
    public const string PageClientName = "orkeon-rag-webfallback-pages";

    /// <summary>Metadata key carrying the injection verdict on returned documents.</summary>
    public const string VerdictMetadataKey = "injection_verdict";

    /// <summary>Metadata key carrying the injection reasons on flagged documents.</summary>
    public const string ReasonsMetadataKey = "injection_reasons";

    /// <summary>Metadata key carrying the injection risk score on flagged documents.</summary>
    public const string RiskScoreMetadataKey = "injection_risk_score";

    /// <summary>Metadata key marking documents fetched by the web fallback.</summary>
    public const string OriginMetadataKey = "web_fallback";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebSearchRetrieverOptions _options;
    private readonly PromptInjectionDocumentValidator _validator;
    private readonly ILogger<WebSearchDocumentRetriever> _logger;

    /// <summary>Initializes a new instance of <see cref="WebSearchDocumentRetriever"/>.</summary>
    public WebSearchDocumentRetriever(
        IHttpClientFactory httpClientFactory,
        IOptions<WebSearchRetrieverOptions> options,
        PromptInjectionDocumentValidator validator,
        ILogger<WebSearchDocumentRetriever>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _validator = validator;
        _logger = logger ?? NullLogger<WebSearchDocumentRetriever>.Instance;
    }

    /// <summary>
    /// Searches the web and returns the validated documents.
    /// <paramref name="maxResults"/> is capped by <see cref="WebSearchRetrieverOptions.MaxResults"/>;
    /// a non-positive value falls back to the configured maximum. Fewer documents than
    /// requested may be returned when pages fail to download or are rejected/discarded
    /// by the injection validator.
    /// </summary>
    public async Task<IReadOnlyList<RagDocument>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (!_options.Enabled)
        {
            LogDisabled();
            return [];
        }

        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            LogNoEndpoint();
            return [];
        }

        var effectiveMax = maxResults <= 0
            ? _options.MaxResults
            : Math.Min(maxResults, _options.MaxResults);
        if (effectiveMax <= 0)
        {
            return [];
        }

        var urls = await ExecuteSearchAsync(query, effectiveMax, cancellationToken).ConfigureAwait(false);
        if (urls.Count == 0)
        {
            return [];
        }

        return await DownloadAndValidateAsync(urls, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ExecuteSearchAsync(
        string query, int effectiveMax, CancellationToken cancellationToken)
    {
        var separator = _options.Endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var requestUri = $"{_options.Endpoint}{separator}q={Uri.EscapeDataString(query)}&format=json";

        var client = _httpClientFactory.CreateClient(SearchClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyApiKey(request);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        try
        {
            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogSearchHttpError((int)response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            return ParseResultUrls(json, effectiveMax);
        }
        catch (HttpRequestException exception)
        {
            LogSearchFailed(exception.Message);
            return [];
        }
        catch (JsonException exception)
        {
            LogSearchUnparseable(exception.Message);
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogSearchTimedOut(_options.Timeout);
            return [];
        }
    }

    private List<string> ParseResultUrls(string json, int effectiveMax)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            LogSearchUnparseable("missing 'results' array");
            return [];
        }

        var urls = new List<string>(effectiveMax);
        foreach (var result in results.EnumerateArray())
        {
            if (urls.Count >= effectiveMax)
            {
                break;
            }

            if (result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("url", out var urlProperty)
                && urlProperty.ValueKind == JsonValueKind.String)
            {
                var url = urlProperty.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    urls.Add(url);
                }
            }
        }

        return urls;
    }

    private async Task<IReadOnlyList<RagDocument>> DownloadAndValidateAsync(
        IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        var loader = new WebPageLoader(_httpClientFactory.CreateClient(PageClientName));
        var documents = new List<RagDocument>(urls.Count);

        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var document = await DownloadPageAsync(loader, url, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                continue;
            }

            var analysis = _validator.Validate(document);

            if (analysis.Verdict == PromptInjectionVerdict.Rejected)
            {
                LogDocumentRejected(url, analysis.RiskScore, string.Join("; ", analysis.Reasons));
                continue;
            }

            if (analysis.Verdict == PromptInjectionVerdict.Suspicious
                && _options.SuspiciousAction == SuspiciousContentAction.Discard)
            {
                LogDocumentDiscarded(url, analysis.RiskScore, string.Join("; ", analysis.Reasons));
                continue;
            }

            documents.Add(Annotate(document, analysis));
        }

        return documents;
    }

    private async Task<RagDocument?> DownloadPageAsync(
        WebPageLoader loader, string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        try
        {
            // Only the first page is retained. An explicit enumerator states that
            // intent directly — an `await foreach` that returns on its first item
            // reads as a loop but never iterates twice (S1751).
            var enumerator = loader
                .LoadAsync(
                    new SourceDescriptor { Location = url, Kind = WebPageLoader.UrlKind },
                    timeoutCts.Token)
                .GetAsyncEnumerator(timeoutCts.Token);
            await using var __enumerator = enumerator.ConfigureAwait(false);

            return await enumerator.MoveNextAsync().ConfigureAwait(false)
                ? enumerator.Current
                : null;
        }
        catch (HttpRequestException exception)
        {
            LogPageDownloadFailed(url, exception.Message);
            return null;
        }
        catch (ArgumentException exception)
        {
            LogPageDownloadFailed(url, exception.Message);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogPageTimedOut(url, _options.Timeout);
            return null;
        }
    }

    private static RagDocument Annotate(RagDocument document, PromptInjectionAnalysis analysis)
    {
        var metadata = document.Metadata
            .SetItem(OriginMetadataKey, "true")
            .SetItem(VerdictMetadataKey, VerdictLabel(analysis.Verdict));

        if (analysis.Verdict == PromptInjectionVerdict.Suspicious)
        {
            metadata = metadata
                .SetItem(ReasonsMetadataKey, string.Join("; ", analysis.Reasons))
                .SetItem(RiskScoreMetadataKey, analysis.RiskScore.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        return document with { Metadata = metadata };
    }

    private static string VerdictLabel(PromptInjectionVerdict verdict) => verdict switch
    {
        PromptInjectionVerdict.Suspicious => "suspicious",
        PromptInjectionVerdict.Rejected => "rejected",
        _ => "clean",
    };

    private void ApplyApiKey(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKeyEnvVar))
        {
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
        else
        {
            LogApiKeyMissing(_options.ApiKeyEnvVar);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "RAG web fallback is disabled (Orkeon:Rag:WebFallback:Enabled=false) — returning no documents")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback is enabled but no search endpoint is configured (Orkeon:Rag:WebFallback:Endpoint) — treating as disabled")]
    private partial void LogNoEndpoint();

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback search returned HTTP {StatusCode} — returning no documents")]
    private partial void LogSearchHttpError(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback search failed: {Error} — returning no documents")]
    private partial void LogSearchFailed(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback search response could not be parsed: {Error} — returning no documents")]
    private partial void LogSearchUnparseable(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback search timed out after {Timeout} — returning no documents")]
    private partial void LogSearchTimedOut(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback page download failed for {Url}: {Error} — skipping result")]
    private partial void LogPageDownloadFailed(string url, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback page download timed out for {Url} after {Timeout} — skipping result")]
    private partial void LogPageTimedOut(string url, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback REJECTED document from {Url} (risk {RiskScore}): {Reasons}")]
    private partial void LogDocumentRejected(string url, double riskScore, string reasons);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback discarded suspicious document from {Url} per policy (risk {RiskScore}): {Reasons}")]
    private partial void LogDocumentDiscarded(string url, double riskScore, string reasons);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAG web fallback API key environment variable {EnvVar} is set in options but empty in the environment — sending unauthenticated request")]
    private partial void LogApiKeyMissing(string envVar);
}
