using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Knowledge.Loaders;

/// <summary>
/// Loads web pages via HTTP and extracts text content.
/// </summary>
public class WebPageLoader : IDocumentLoader
{
    private readonly HttpClient _httpClient;

    /// <summary>Initializes a new instance of <see cref="WebPageLoader"/>.</summary>
    /// <param name="httpClient">The HTTP client for fetching web pages.</param>
    public WebPageLoader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public string SupportedType => "web";

    /// <inheritdoc />
    public Task<LoadedDocument> LoadAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!CanLoad(source))
            throw new ArgumentException($"Source is not a valid HTTP/HTTPS URL: {source}", nameof(source));

        return LoadCoreAsync(source, cancellationToken);

        async Task<LoadedDocument> LoadCoreAsync(string source, CancellationToken cancellationToken)
        {
            var requestUri = new Uri(source, UriKind.Absolute);
            var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var text = HtmlDocumentLoader.ExtractText(htmlContent);
            var title = HtmlDocumentLoader.ExtractTitle(htmlContent);

            var metadata = new Dictionary<string, object>
            {
                ["url"] = source,
                ["status_code"] = (int)response.StatusCode,
                ["content_type"] = response.Content.Headers.ContentType?.MediaType ?? "unknown",
                ["fetched_at"] = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(title))
                metadata["title"] = title;

            return new LoadedDocument(
                Content: text,
                SourceId: source,
                SourceType: SupportedType,
                Metadata: metadata);
        }
    }

    /// <inheritdoc />
    public bool CanLoad(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
