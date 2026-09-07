using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// SSRF regression tests for <see cref="WebPageLoader"/> (D9-09). Ingestion sources
/// come straight from an agent (<c>rag_ingest</c> forwards its <c>sources</c>
/// verbatim), so the loader is the choke point where a URL must be validated —
/// and it must refuse to fetch at all when no validator is available, rather than
/// fetch unchecked.
/// </summary>
public sealed class WebPageLoaderSsrfTests : IDisposable
{
    private const string MetadataUrl = "http://169.254.169.254/latest/meta-data/";

    private readonly RecordingHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;

    public WebPageLoaderSsrfTests() => _httpClient = new HttpClient(_handler);

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task LoadAsync_ShouldRefuseAndNeverFetch_WhenTheValidatorDeniesTheUrl()
    {
        var validator = new StubUrlValidator(
            url => !url.Host.StartsWith("169.254", StringComparison.Ordinal),
            "private/reserved IP");
        var loader = new WebPageLoader(_httpClient, validator);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(loader, MetadataUrl));

        Assert.Contains("SSRF", error.Message, StringComparison.Ordinal);
        Assert.Contains("private/reserved IP", error.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
        Assert.Equal([new Uri(MetadataUrl)], validator.Validated);
    }

    [Fact]
    public async Task LoadAsync_ShouldRefuseAndNeverFetch_WhenNoUrlValidatorIsAvailable()
    {
        // Fail closed: AddOrkeonRag alone registers no IUrlValidator, and fetching
        // an agent-supplied URL unchecked is exactly the defect being fixed.
        var loader = new WebPageLoader(_httpClient);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(loader, "https://example.com/page"));

        Assert.Contains("IUrlValidator", error.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task LoadAsync_ShouldFetch_WhenTheValidatorAllowsTheUrl()
    {
        var validator = new StubUrlValidator();
        var loader = new WebPageLoader(_httpClient, validator);

        var document = await DrainAsync(loader, "https://example.com/page");

        Assert.Contains("Allowed", document.Content, StringComparison.Ordinal);
        Assert.Single(_handler.Requests);
    }

    private static async Task<RagDocument> DrainAsync(WebPageLoader loader, string location)
    {
        var documents = new List<RagDocument>();
        await foreach (var document in loader.LoadAsync(
            new SourceDescriptor { Location = location }, TestContext.Current.CancellationToken))
        {
            documents.Add(document);
        }

        return Assert.Single(documents);
    }
}
