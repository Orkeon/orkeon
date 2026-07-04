using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Security;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Strongly-typed request for <see cref="ScrapeElementTool"/>.</summary>
public record ScrapeElementRequest
{
    /// <summary>Gets the URL of the web page to scrape.</summary>
    [FieldSchema(Description = "The URL of the web page to scrape", Example = "https://example.com/products", IsRequired = true)]
    public Uri? Url { get; init; }

    /// <summary>Gets the CSS selector used to find elements.</summary>
    [FieldSchema(Description = "CSS selector to find elements", Example = "div.product > h2")]
    public string CssSelector { get; init; } = "";

    /// <summary>Gets a value indicating whether to include HTML attributes for each element.</summary>
    [FieldSchema(Description = "Include HTML attributes for each element", Example = false, IsRequired = false, Default = false)]
    public bool ExtractAttributes { get; init; }

    /// <summary>Gets a value indicating whether to include raw HTML for each element.</summary>
    [FieldSchema(Description = "Include raw inner HTML for each element", Example = false, IsRequired = false, Default = false)]
    public bool IncludeHtml { get; init; }

    /// <summary>Gets the maximum number of elements to return.</summary>
    [FieldSchema(Description = "Maximum number of elements to return", Example = 50, IsRequired = false, Default = 50)]
    public int MaxElements { get; init; } = 50;
}

/// <summary>A single scraped HTML element.</summary>
public record ScrapedElement
{
    /// <summary>Gets the inner text of the element, trimmed.</summary>
    public string Text { get; init; } = "";

    /// <summary>Gets the raw inner HTML (only populated when IncludeHtml is true).</summary>
    public string? Html { get; init; }

    /// <summary>Gets the HTML attributes (only populated when ExtractAttributes is true).</summary>
    public Dictionary<string, string>? Attributes { get; init; }

    /// <summary>Gets the zero-based position of this element in the result set.</summary>
    public int Index { get; init; }
}

/// <summary>Strongly-typed response for <see cref="ScrapeElementTool"/>.</summary>
public record ScrapeElementResponse
{
    /// <summary>Gets the list of extracted elements.</summary>
    [ReturnSchema(Description = "List of extracted elements matching the CSS selector")]
    public IReadOnlyList<ScrapedElement> Elements { get; init; } = [];

    /// <summary>Gets the total number of elements found (before MaxElements limit).</summary>
    [ReturnSchema(Description = "Total number of elements found before limiting", Example = 120)]
    public int ElementCount { get; init; }

    /// <summary>Gets the number of elements actually returned (after MaxElements limit).</summary>
    [ReturnSchema(Description = "Number of elements returned after MaxElements limit", Example = 50)]
    public int ReturnedCount { get; init; }

    /// <summary>Gets the URL that was scraped.</summary>
    [ReturnSchema(Description = "The URL that was scraped", Example = "https://example.com/products")]
    public Uri? Url { get; init; }

    /// <summary>Gets the CSS selector that was used.</summary>
    [ReturnSchema(Description = "The CSS selector that was used", Example = "div.product > h2")]
    public string CssSelector { get; init; } = "";
}

/// <summary>
/// Tool for extracting targeted elements from web pages via CSS selectors.
/// Uses AngleSharp (MIT) for standards-compliant HTML parsing and native CSS selector support.
/// </summary>
[ToolContract("scrape_element",
    Name = "scrape_element",
    Description = "Extract targeted elements from a web page using CSS selectors. Returns text, optional HTML, and attributes for matched elements.",
    Category = "Web Operations")]
public partial class ScrapeElementTool : HttpToolBase<ScrapeElementRequest, ScrapeElementResponse>
{
    /// <summary>Initializes a new instance of <see cref="ScrapeElementTool"/>.</summary>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <remarks>
    /// Without an <see cref="IUrlValidator"/>, the outbound request is still guarded by the
    /// fail-closed default SSRF policy in <c>HttpToolBase.ValidateUrlAsync</c>.
    /// </remarks>
    public ScrapeElementTool(HttpClient? httpClient = null, ILogger<ScrapeElementTool>? logger = null)
        : base(httpClient, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="ScrapeElementTool"/> with SSRF protection.</summary>
    /// <param name="urlValidator">URL validator for SSRF protection (applied before the outbound fetch).</param>
    /// <param name="headerSanitizer">Header sanitizer to prevent header injection.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> to use for HTTP requests.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ScrapeElementTool(
        IUrlValidator urlValidator,
        HttpHeaderSanitizer headerSanitizer,
        HttpClient? httpClient = null,
        ILogger<ScrapeElementTool>? logger = null)
        : base(urlValidator, headerSanitizer, httpClient, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ScrapeElementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Url is null)
            return "URL cannot be empty";

        var uri = request.Url;
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Invalid URL. Must be an absolute HTTP or HTTPS URL.";

        if (string.IsNullOrWhiteSpace(request.CssSelector))
            return "CSS selector cannot be empty";

        if (request.MaxElements <= 0 || request.MaxElements > 500)
            return "MaxElements must be between 1 and 500";

        return null;
    }

    /// <inheritdoc />
    protected override Task<ScrapeElementResponse> ExecuteTypedAsync(
        ScrapeElementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Url);

        return ExecuteTypedCoreAsync();

        async Task<ScrapeElementResponse> ExecuteTypedCoreAsync()
        {
            // SSRF protection: validate the requested URL (or its resolved IP) before fetching.
            var urlValidation = await ValidateUrlAsync(request.Url, cancellationToken).ConfigureAwait(false);
            if (!urlValidation.IsAllowed)
                throw new InvalidOperationException($"URL blocked: {urlValidation.DenialReason}");

            var uri = urlValidation.ValidatedUri!;
            var html = await _httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);

            var parser = new HtmlParser();
            using var document = await parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);

            // AngleSharp's native QuerySelectorAll returns matches in document order,
            // same contract as the previous Fizzler-based implementation.
            var matchedNodes = document.QuerySelectorAll(request.CssSelector).ToList();
            var totalCount = matchedNodes.Count;

            var limitedNodes = matchedNodes.Take(request.MaxElements).ToList();

            var elements = new List<ScrapedElement>(limitedNodes.Count);
            for (var i = 0; i < limitedNodes.Count; i++)
            {
                var node = limitedNodes[i];
                elements.Add(BuildElement(node, i, request.ExtractAttributes, request.IncludeHtml));
            }

            LogScrapedElements(request.Url, request.CssSelector, totalCount, elements.Count);

            return new ScrapeElementResponse
            {
                Elements = elements,
                ElementCount = totalCount,
                ReturnedCount = elements.Count,
                Url = request.Url,
                CssSelector = request.CssSelector
            };
        }
    }

    private static ScrapedElement BuildElement(IElement node, int index, bool extractAttributes, bool includeHtml)
    {
        // Clone the node so we can remove script/style without mutating the original document
        var cleanNode = (IElement)node.Clone();
        RemoveScriptAndStyleTags(cleanNode);

        // TextContent is already entity-decoded (DOM spec), matching the previous
        // HtmlEntity.DeEntitize(InnerText) behavior.
        var text = cleanNode.TextContent.Trim();

        string? rawHtml = null;
        if (includeHtml)
        {
            rawHtml = cleanNode.InnerHtml;
        }

        Dictionary<string, string>? attributes = null;
        if (extractAttributes && node.Attributes.Length > 0)
        {
            attributes = new Dictionary<string, string>(node.Attributes.Length);
            foreach (var attr in node.Attributes)
            {
                attributes[attr.Name] = attr.Value;
            }
        }

        return new ScrapedElement
        {
            Text = text,
            Html = rawHtml,
            Attributes = attributes,
            Index = index
        };
    }

    private static void RemoveScriptAndStyleTags(IElement node)
    {
        // Descendant-only scope, same as the previous XPath ".//script|.//style".
        foreach (var child in node.QuerySelectorAll("script, style").ToList())
        {
            child.Remove();
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scraped {Url} with selector '{CssSelector}': {TotalCount} found, {ReturnedCount} returned")]
    private partial void LogScrapedElements(Uri url, string cssSelector, int totalCount, int returnedCount);
}
