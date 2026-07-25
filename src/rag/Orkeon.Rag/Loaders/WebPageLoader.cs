using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Loaders;

/// <summary>
/// Loads web pages via HTTP and extracts clean text content. Port of the
/// legacy Infrastructure <c>WebPageLoader</c> onto the
/// <c>Orkeon.Rag.Abstractions</c> contract (RAG-02/C5).
/// </summary>
public sealed class WebPageLoader : IDocumentLoader
{
    /// <summary><see cref="SourceDescriptor.Kind"/> hints accepted by this loader.</summary>
    public const string UrlKind = "url";

    /// <summary>Legacy alias of <see cref="UrlKind"/> (the old loader's SupportedType).</summary>
    public const string WebKind = "web";

    private readonly HttpClient _httpClient;

    /// <summary>Initializes a new instance of <see cref="WebPageLoader"/>.</summary>
    /// <param name="httpClient">The HTTP client used to fetch pages.</param>
    public WebPageLoader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public bool CanLoad(SourceDescriptor source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Location))
            return false;

        // Honor the loader hint: only claim url/web-kind (or unhinted) sources.
        if (source.Kind is not null
            && !string.Equals(source.Kind, UrlKind, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(source.Kind, WebKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsHttpUrl(source.Location);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagDocument> LoadAsync(
        SourceDescriptor source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Location);

        if (!IsHttpUrl(source.Location))
            throw new ArgumentException($"Source is not a valid HTTP/HTTPS URL: {source.Location}", nameof(source));

        yield return await LoadPageAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RagDocument> LoadPageAsync(SourceDescriptor source, CancellationToken cancellationToken)
    {
        var requestUri = new Uri(source.Location, UriKind.Absolute);
        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var htmlContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var text = HtmlDocumentLoader.ExtractText(htmlContent);
        var title = HtmlDocumentLoader.ExtractTitle(htmlContent);

        var metadata = ImmutableDictionary.CreateBuilder<string, string>();
        metadata["url"] = source.Location;
        metadata["status_code"] = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        metadata["content_type"] = response.Content.Headers.ContentType?.MediaType ?? "unknown";
        metadata["fetched_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(title))
            metadata["title"] = title;

        var sourceId = string.IsNullOrWhiteSpace(source.SourceId) ? source.Location : source.SourceId;
        return new RagDocument
        {
            Id = sourceId,
            SourceId = sourceId,
            Content = text,
            Title = string.IsNullOrEmpty(title) ? null : title,
            Location = source.Location,
            Metadata = metadata.ToImmutable(),
        };
    }

    private static bool IsHttpUrl(string location) =>
        location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
