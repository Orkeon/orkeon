using System.Net;
using System.Text;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// <see cref="WebPageLoader"/> on the new contract — port of the legacy
/// <c>Orkeon.Infrastructure.Tests</c> WebPageLoader suite (RAG-02/C5): HTTP fetch,
/// HTML text/title extraction, provenance metadata, URL gating, and HTTP errors.
/// </summary>
public sealed class WebPageLoaderTests : IDisposable
{
    private readonly StubHttpHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly WebPageLoader _loader;

    public WebPageLoaderTests()
    {
        _httpClient = new HttpClient(_handler);
        // The loader fails closed without an IUrlValidator; these tests exercise the
        // fetch/parse behavior, so they wire the permissive double.
        _loader = new WebPageLoader(_httpClient, new StubUrlValidator());
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }

    private static SourceDescriptor Url(string location) => new() { Location = location };

    private async Task<RagDocument> LoadSingleAsync(string location)
    {
        var documents = new List<RagDocument>();
        await foreach (var doc in _loader.LoadAsync(Url(location), TestContext.Current.CancellationToken))
            documents.Add(doc);
        return Assert.Single(documents);
    }

    [Fact]
    public async Task LoadAsync_FetchesAndParsesContent()
    {
        _handler.Respond(HttpStatusCode.OK,
            "<html><head><title>Test</title></head><body><h1>Hello Web</h1><p>Content here.</p></body></html>");

        var result = await LoadSingleAsync("https://example.com/page");

        Assert.Contains("Hello Web", result.Content, StringComparison.Ordinal);
        Assert.Contains("Content here.", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>", result.Content, StringComparison.Ordinal);
        Assert.Equal("https://example.com/page", result.SourceId);
        Assert.Equal("https://example.com/page", result.Location);
    }

    [Fact]
    public async Task LoadAsync_ExtractsTitle_AndProvenanceMetadata()
    {
        _handler.Respond(HttpStatusCode.OK,
            "<html><head><title>Page Title</title></head><body><p>Text</p></body></html>");

        var result = await LoadSingleAsync("https://example.com/");

        Assert.Equal("Page Title", result.Title);
        Assert.Equal("Page Title", result.Metadata["title"]);
        Assert.Equal("https://example.com/", result.Metadata["url"]);
        Assert.Equal("200", result.Metadata["status_code"]);
        Assert.True(result.Metadata.ContainsKey("fetched_at"));
        Assert.True(result.Metadata.ContainsKey("content_type"));
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com/page", true)]
    [InlineData("/tmp/file.txt", false)]
    [InlineData("C:\\file.txt", false)]
    [InlineData("", false)]
    public void CanLoad_AcceptsOnlyHttpUrls(string location, bool expected)
    {
        Assert.Equal(expected, _loader.CanLoad(new SourceDescriptor { Location = location }));
    }

    [Fact]
    public void CanLoad_HonorsTheKindHint()
    {
        Assert.True(_loader.CanLoad(new SourceDescriptor { Location = "https://example.com", Kind = "url" }));
        Assert.True(_loader.CanLoad(new SourceDescriptor { Location = "https://example.com", Kind = "web" }));
        Assert.False(_loader.CanLoad(new SourceDescriptor { Location = "https://example.com", Kind = "file" }));
    }

    [Fact]
    public async Task LoadAsync_Throws_WhenLocationIsNotAnHttpUrl()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _loader.LoadAsync(Url("/tmp/file.txt"), TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task LoadAsync_Throws_OnHttpError()
    {
        _handler.Respond(HttpStatusCode.NotFound, "Not Found");

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in _loader.LoadAsync(Url("https://example.com/missing"), TestContext.Current.CancellationToken))
            {
            }
        });
    }

    /// <summary>Hand-written double: serves one configurable HTML response.</summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _content = string.Empty;

        public void Respond(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_content, Encoding.UTF8, "text/html"),
            });
    }
}
