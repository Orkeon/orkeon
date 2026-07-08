using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Helpers;
using Orkeon.Tools.Abstractions.Security;
using Orkeon.Tools.Web.Constants.Scrape;

namespace Orkeon.Tools.Web;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Strongly-typed request for <see cref="WebScrapeTool"/>.</summary>
public record WebScrapeRequest
{
    /// <summary>Gets the URL of the web page to scrape.</summary>
    [FieldSchema(Description = "The URL of the web page to scrape", Example = "https://example.com/article", IsRequired = true)]
    public Uri? Url { get; init; }

    /// <summary>Gets the optional CSS/XPath selector to filter content.</summary>
    [FieldSchema(Description = "Optional CSS/XPath selector to filter content", Example = "div.main-content")]
    public string? Selector { get; init; }

    /// <summary>
    /// When true, chunks the page, embeds each chunk and stores it in the RAG cache;
    /// returns a short summary instead of the full content. Query the chunks later
    /// with <c>cache_search</c>. Saves significant context tokens for large pages.
    /// </summary>
    [FieldSchema(Description = "When true, chunk + embed + store in the RAG cache and return a short summary instead of full content. Query chunks back via cache_search.", Example = false, IsRequired = false)]
    public bool Cached { get; init; }

    /// <summary>Gets the maximum chunk size in characters when caching.</summary>
    [FieldSchema(Description = "Maximum chunk size in characters when cached=true (default 1500)", Example = 1500, IsRequired = false)]
    public int ChunkSize { get; init; } = 1500;
}

/// <summary>Strongly-typed response for <see cref="WebScrapeTool"/>.</summary>
public record WebScrapeResponse
{
    /// <summary>Gets the extracted text content from the web page (empty when cached=true).</summary>
    [ReturnSchema(Description = "Full extracted text — only filled when cached=false. When cached=true, use cache_search to retrieve relevant chunks.", Example = "Welcome to Example.com…")]
    public string Content { get; init; } = "";

    /// <summary>Gets the page title from the <c>&lt;title&gt;</c> tag.</summary>
    [ReturnSchema(Description = "Page title from the <title> tag", Example = "Example Domain - Home")]
    public string Title { get; init; } = "";

    /// <summary>Gets the URL that was scraped.</summary>
    [ReturnSchema(Description = "The URL that was scraped", Example = "https://example.com/article")]
    public Uri? Url { get; init; }

    /// <summary>Gets the length of the extracted content in characters.</summary>
    [ReturnSchema(Description = "Length of the extracted content in characters", Example = 4523)]
    public int ContentLength { get; init; }

    /// <summary>Gets a value indicating whether the CSS/XPath selector matched any elements.</summary>
    [ReturnSchema(Description = "Whether the CSS/XPath selector matched any elements", Example = true)]
    public bool SelectorMatched { get; init; }

    /// <summary>Indicates whether the page was stored in the RAG cache.</summary>
    [ReturnSchema(Description = "True when cached=true and chunks were embedded + stored", Example = false)]
    public bool Cached { get; init; }

    /// <summary>When cached, number of chunks stored.</summary>
    [ReturnSchema(Description = "Number of chunks stored in the cache (0 when cached=false)", Example = 8)]
    public int TotalChunks { get; init; }

    /// <summary>When cached, short preview (first ~500 chars) of the content.</summary>
    [ReturnSchema(Description = "First ~500 chars of the content when cached=true (empty otherwise)", Example = "Welcome to Example Domain. This page demonstrates…")]
    public string Summary { get; init; } = "";

    /// <summary>When cached, the stable key prefix used to store chunks (web:&lt;url-hash&gt;).</summary>
    [ReturnSchema(Description = "Stable key prefix for the cached chunks (web:<url-hash>) — pass to cache_search via url_filter to scope a query", Example = "web:6f1b2a")]
    public string KeyPrefix { get; init; } = "";
}

// ── Tool ───────────────────────────────────────────────────────────────

/// <summary>
/// Scrapes a web page, returning either the full extracted text (cached=false,
/// default) or a short summary plus a populated RAG cache (cached=true) so the
/// agent can later query chunks via <c>cache_search</c> instead of pinning
/// large pages in its conversation context.
/// </summary>
[ToolContract("web_scrape",
    Name = "web_scrape",
    Description = "Scrape a web page. cached=false returns full text; cached=true chunks+embeds+stores the page in the RAG cache and returns only a short summary (use cache_search to retrieve chunks).",
    Category = "Web Operations")]
public partial class WebScrapeTool : HttpToolBase<WebScrapeRequest, WebScrapeResponse>
{
    /// <summary>Source label used to tag cached items so cache_search can filter on it.</summary>
    public const string CacheSourceTag = "web_scrape";

    private const int SummaryLength = 500;
    private const int MaxChunksPerPage = 80;
    private const string CacheKeyNamespace = "web";

    private readonly IEmbeddingService? _embeddingService;
    private readonly IMemoryProvider? _memoryProvider;

    /// <summary>Initializes a new instance of <see cref="WebScrapeTool"/>.</summary>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <remarks>
    /// Without an <see cref="IUrlValidator"/>, outbound requests are still guarded by the
    /// fail-closed default SSRF policy in <c>HttpToolBase.ValidateUrlAsync</c>.
    /// </remarks>
    public WebScrapeTool(HttpClient? httpClient = null, ILogger<WebScrapeTool>? logger = null)
        : base(httpClient, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WebScrapeTool"/> with SSRF protection.
    /// </summary>
    /// <param name="urlValidator">URL validator for SSRF protection (applied before every outbound fetch).</param>
    /// <param name="headerSanitizer">Header sanitizer to prevent header injection.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public WebScrapeTool(
        IUrlValidator urlValidator,
        HttpHeaderSanitizer headerSanitizer,
        HttpClient? httpClient = null,
        ILogger<WebScrapeTool>? logger = null)
        : base(urlValidator, headerSanitizer, httpClient, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WebScrapeTool"/> with RAG-cache support.
    /// When both services are provided, requests with <c>cached=true</c> chunk + embed +
    /// store the page; otherwise the cached flag silently degrades to full-content mode.
    /// </summary>
    public WebScrapeTool(
        IEmbeddingService embeddingService,
        IMemoryProvider memoryProvider,
        HttpClient? httpClient = null,
        ILogger<WebScrapeTool>? logger = null)
        : base(httpClient, logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WebScrapeTool"/> with both RAG-cache support
    /// and SSRF protection.
    /// </summary>
    public WebScrapeTool(
        IEmbeddingService embeddingService,
        IMemoryProvider memoryProvider,
        IUrlValidator urlValidator,
        HttpHeaderSanitizer headerSanitizer,
        HttpClient? httpClient = null,
        ILogger<WebScrapeTool>? logger = null)
        : base(urlValidator, headerSanitizer, httpClient, logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(WebScrapeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Url is null)
            return "URL cannot be empty";

        var uri = request.Url;
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Invalid URL. Must be an absolute HTTP or HTTPS URL.";

        if (request.Cached && (request.ChunkSize < 200 || request.ChunkSize > 8000))
            return "ChunkSize must be between 200 and 8000";

        return null;
    }

    /// <inheritdoc />
    protected override Task<WebScrapeResponse> ExecuteTypedAsync(
        WebScrapeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Url);
        return ExecuteTypedCoreAsync();

        async Task<WebScrapeResponse> ExecuteTypedCoreAsync()
        {
            // Dedup short-circuit: when caching is enabled and the URL was already
            // chunked + stored on a previous call, skip the HTTP fetch entirely and
            // reconstruct the response from the cache. Saves an HTTP round-trip and
            // keeps the agent context stable across redundant scrape calls.
            var dedupHit = await TryDedupShortCircuitAsync(request, cancellationToken).ConfigureAwait(false);
            if (dedupHit is not null)
                return dedupHit;

            // SSRF protection: validate the requested URL (or its resolved IP) before any
            // outbound fetch. Uses the injected IUrlValidator when available, otherwise the
            // fail-closed default guard in HttpToolBase.
            var urlValidation = await ValidateUrlAsync(request.Url, cancellationToken).ConfigureAwait(false);
            if (!urlValidation.IsAllowed)
                throw new InvalidOperationException($"URL blocked: {urlValidation.DenialReason}");

            var uri = urlValidation.ValidatedUri!;

            // Bound every outbound HTTP call with an explicit per-request timeout
            // so a slow or hanging remote server cannot freeze the calling agent
            // indefinitely (defense-in-depth on top of any HttpClient-level timeout).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(HttpDefaults.ScrapeTimeoutSeconds));

            // Wikipedia pages block HTML scraping; use the REST API instead.
            if (TryGetWikipediaApiUrl(uri, out var apiUrl, out var articleTitle))
            {
                var wiki = await FetchViaWikipediaApiAsync(apiUrl, articleTitle, request.Url, cts.Token).ConfigureAwait(false);
                return await MaybeCacheAsync(wiki, request, cancellationToken).ConfigureAwait(false);
            }

            var html = await FetchHtmlAsync(uri, request.Url, cts.Token).ConfigureAwait(false);
            var fetched = BuildResponseFromHtml(html, request.Selector, request.Url);
            return await MaybeCacheAsync(fetched, request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// When caching is enabled and both RAG services are wired, probes the cache for a
    /// previous scrape of this URL. Returns the reconstructed response on a hit, or null
    /// (no dedup possible / cache miss) so the caller performs a normal fetch.
    /// </summary>
    private async Task<WebScrapeResponse?> TryDedupShortCircuitAsync(WebScrapeRequest request, CancellationToken cancellationToken)
    {
        if (request.Cached && _embeddingService is not null && _memoryProvider is not null)
            return await TryDedupAsync(request.Url!, cancellationToken).ConfigureAwait(false);

        return null;
    }

    /// <summary>
    /// Fetches raw HTML, re-throwing any <see cref="HttpRequestException"/> with the URL in
    /// the message so the agent's circuit breaker can distinguish between different URLs that
    /// all return the same HTTP status. Without this, three distinct 404s look identical to
    /// the breaker and trip it after one round of guessing.
    /// </summary>
    private async Task<string> FetchHtmlAsync(Uri uri, Uri originalUrl, CancellationToken ct)
    {
        try
        {
            return await _httpClient.GetStringAsync(uri, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode is { } sc ? $"HTTP {(int)sc}" : "HTTP error";
            throw new HttpRequestException(
                $"web_scrape failed for {originalUrl}: {status} — {ex.Message}",
                ex,
                ex.StatusCode);
        }
    }

    /// <summary>
    /// Parses the fetched HTML into a <see cref="WebScrapeResponse"/>, honoring the optional
    /// CSS/XPath selector (selector match) or falling back to full-page text extraction.
    /// </summary>
    private WebScrapeResponse BuildResponseFromHtml(string html, string? selector, Uri url)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(selector))
            return ExtractWithSelector(doc, selector, title, url);

        var text = ExtractFullPageText(doc);
        LogScrapedUrl(url, text.Length);
        return new WebScrapeResponse
        {
            Content = text,
            Title = title,
            Url = url,
            ContentLength = text.Length,
            SelectorMatched = false
        };
    }

    /// <summary>
    /// When <paramref name="request"/>.Cached is true and the embedding/memory services are
    /// wired, chunks the fetched content, embeds each chunk and stores it in the cache; the
    /// returned response carries only a short summary. Otherwise returns <paramref name="fetched"/>
    /// unchanged.
    /// </summary>
    private async Task<WebScrapeResponse> MaybeCacheAsync(
        WebScrapeResponse fetched, WebScrapeRequest request, CancellationToken cancellationToken)
    {
        if (!request.Cached || _embeddingService is null || _memoryProvider is null)
            return fetched with { Cached = false };

        if (string.IsNullOrWhiteSpace(fetched.Content))
            return fetched with { Cached = true, Summary = "(empty page)" };

        var url = fetched.Url!;
        var keyPrefix = $"{CacheKeyNamespace}:{ShortHash(url)}";

        var chunks = TextChunker.ChunkContent(fetched.Content, request.ChunkSize);
        if (chunks.Count > MaxChunksPerPage)
            chunks = chunks.Take(MaxChunksPerPage).ToList();

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (chunkText, _) = chunks[i];
            var embedding = await _embeddingService.GetEmbeddingAsync(chunkText, cancellationToken).ConfigureAwait(false);

            var customProps = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["url"] = url.AbsoluteUri,
                ["title"] = fetched.Title,
                ["chunk_index"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["total_chunks"] = chunks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["scraped_at"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            };

            var item = MemoryItem.Create(
                content: chunkText,
                embedding: embedding,
                source: url.AbsoluteUri,
                tags: [CacheSourceTag],
                customProperties: customProps);

            var key = $"{keyPrefix}:{i:D4}";
            await _memoryProvider.StoreAsync(key, item, cancellationToken).ConfigureAwait(false);
        }

        LogCachedUrl(url, fetched.ContentLength, chunks.Count);

        var summary = fetched.Content.Length > SummaryLength
            ? fetched.Content[..SummaryLength] + "…"
            : fetched.Content;

        return new WebScrapeResponse
        {
            // Drop full content — agent must use cache_search to retrieve chunks.
            Content = "",
            Title = fetched.Title,
            Url = fetched.Url,
            ContentLength = fetched.ContentLength,
            SelectorMatched = fetched.SelectorMatched,
            Cached = true,
            TotalChunks = chunks.Count,
            Summary = summary,
            KeyPrefix = keyPrefix
        };
    }

    /// <summary>
    /// Probes the cache for chunk #0000 of this URL. Returns a fully-formed response
    /// when found (skipping the HTTP fetch); returns null on cache miss.
    /// </summary>
    private async Task<WebScrapeResponse?> TryDedupAsync(Uri url, CancellationToken ct)
    {
        var keyPrefix = $"{CacheKeyNamespace}:{ShortHash(url)}";
        var firstKey = $"{keyPrefix}:0000";

        var first = await _memoryProvider!.GetAsync(firstKey, ct).ConfigureAwait(false);
        if (first is null)
            return null;

        var props = first.Metadata.CustomProperties;
        var title = props is not null && props.TryGetValue("title", out var t) ? t : "";
        var total = 0;
        if (props is not null && props.TryGetValue("total_chunks", out var n))
        {
            int.TryParse(n, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out total);
        }

        var summary = first.Content.Length > SummaryLength
            ? first.Content[..SummaryLength] + "…"
            : first.Content;

        LogDedupHit(url, total);

        return new WebScrapeResponse
        {
            Content = "",
            Title = title,
            Url = url,
            ContentLength = first.Content.Length,
            SelectorMatched = false,
            Cached = true,
            TotalChunks = total,
            Summary = summary,
            KeyPrefix = keyPrefix
        };
    }

    private static string ShortHash(Uri url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri));
#pragma warning disable CA1308 // lowercase hex is the produced cache-key form expected by the rest of the system, not a comparison normalization
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
#pragma warning restore CA1308
    }

    // ── Wikipedia REST API helpers ───────────────────────────────────────

    [GeneratedRegex(WebScrapeDefaults.WikipediaPathRegex, RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WikiPathRegex();

    /// <summary>
    /// Detects Wikipedia article URLs and returns the corresponding REST API endpoint.
    /// Matches any language subdomain (fr, en, de, …).
    /// </summary>
    private static bool TryGetWikipediaApiUrl(Uri uri, out string apiUrl, out string articleTitle)
    {
        apiUrl = "";
        articleTitle = "";

        if (!uri.Host.EndsWith(WebScrapeDefaults.WikipediaDomain, StringComparison.OrdinalIgnoreCase))
            return false;

        var match = WikiPathRegex().Match(uri.AbsolutePath);
        if (!match.Success)
            return false;

        articleTitle = Uri.UnescapeDataString(match.Groups[1].Value);
        // REST API v1: returns structured JSON with extract (plain text summary).
        apiUrl = $"https://{uri.Host}{WebScrapeDefaults.WikipediaApiEndpoint}{match.Groups[1].Value}";
        return true;
    }

    /// <summary>
    /// Fetches article content via the Wikipedia REST API (no anti-bot blocking).
    /// </summary>
    private async Task<WebScrapeResponse> FetchViaWikipediaApiAsync(
        string apiUrl, string articleTitle, Uri originalUrl, CancellationToken ct)
    {
        var json = await _httpClient.GetStringAsync(new Uri(apiUrl), ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? articleTitle : articleTitle;
        var extract = root.TryGetProperty("extract", out var e) ? e.GetString() ?? "" : "";

        LogScrapedUrl(originalUrl, extract.Length);

        return new WebScrapeResponse
        {
            Content = extract,
            Title = title,
            Url = originalUrl,
            ContentLength = extract.Length,
            SelectorMatched = false
        };
    }

    private static WebScrapeResponse ExtractWithSelector(HtmlDocument doc, string selector, string title, Uri url)
    {
        var nodes = doc.DocumentNode.SelectNodes(selector);

        if (nodes == null || nodes.Count == 0)
        {
            return new WebScrapeResponse
            {
                Content = "",
                Title = title,
                Url = url,
                ContentLength = 0,
                SelectorMatched = false
            };
        }

        var text = string.Join("\n", nodes.Select(n => HtmlEntity.DeEntitize(n.InnerText.Trim())));

        return new WebScrapeResponse
        {
            Content = text,
            Title = title,
            Url = url,
            ContentLength = text.Length,
            SelectorMatched = true
        };
    }

    private static string ExtractFullPageText(HtmlDocument doc)
    {
        foreach (var script in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
        {
            script.Remove();
        }

        var text = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? doc.DocumentNode.InnerText);
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully scraped URL: {Url} ({Length} chars)")]
    private partial void LogScrapedUrl(Uri url, int length);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached URL: {Url} ({Length} chars, {Chunks} chunks)")]
    private partial void LogCachedUrl(Uri url, int length, int chunks);

    [LoggerMessage(Level = LogLevel.Information, Message = "web_scrape dedup hit: {Url} ({Chunks} chunks already cached — skipped fetch)")]
    private partial void LogDedupHit(Uri url, int chunks);
}
