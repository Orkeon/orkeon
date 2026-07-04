namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class HtmlDocumentLoaderTests : IDisposable
{
    private readonly HtmlDocumentLoaderTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldStripTagsAndExtractText_WhenLoadingHtmlContent()
    {
        var html = "<html><head><title>Test Page</title></head><body><h1>Hello</h1><p>World</p></body></html>";
        var path = _fixture.CreateTempFile(html);

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Hello", result.Content);
        Assert.Contains("World", result.Content);
        Assert.DoesNotContain("<h1>", result.Content);
        Assert.DoesNotContain("<p>", result.Content);
    }

    [Fact]
    public async Task ShouldRemoveScriptContent_WhenLoadingHtmlWithScripts()
    {
        var html = "<html><body><p>Visible</p><script>var x = 1;</script></body></html>";
        var path = _fixture.CreateTempFile(html);

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Visible", result.Content);
        Assert.DoesNotContain("var x = 1", result.Content);
    }

    [Fact]
    public async Task ShouldRemoveStyleContent_WhenLoadingHtmlWithStyles()
    {
        var html = "<html><head><style>body { color: red; }</style></head><body><p>Visible</p></body></html>";
        var path = _fixture.CreateTempFile(html);

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Visible", result.Content);
        Assert.DoesNotContain("color: red", result.Content);
    }

    [Fact]
    public async Task ShouldExtractTitleToMetadata_WhenLoadingHtmlWithTitle()
    {
        var html = "<html><head><title>My Page Title</title></head><body><p>Content</p></body></html>";
        var path = _fixture.CreateTempFile(html);

        var result = await _fixture.LoadAsync(path);

        Assert.True(result.Metadata.ContainsKey("title"));
        Assert.Equal("My Page Title", result.Metadata["title"]);
    }

    [Fact]
    public async Task ShouldDecodeEntities_WhenLoadingHtmlWithEntities()
    {
        var html = "<html><body><p>Hello &amp; World</p></body></html>";
        var path = _fixture.CreateTempFile(html);

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Hello & World", result.Content);
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadHtmlFile()
    {
        Assert.True(_fixture.CanLoad("page.html"));
        Assert.True(_fixture.CanLoad("page.htm"));
        Assert.True(_fixture.CanLoad("PAGE.HTML"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNonHtmlFile()
    {
        Assert.False(_fixture.CanLoad("page.txt"));
        Assert.False(_fixture.CanLoad("page.pdf"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNullOrEmpty()
    {
        Assert.False(_fixture.CanLoad(null!));
        Assert.False(_fixture.CanLoad(""));
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenLoadingNonExistentFile()
    {
        var path = $"/nonexistent_{Guid.NewGuid()}.html";

        var act = () => _fixture.LoadAsync(path);

        await Assert.ThrowsAsync<FileNotFoundException>(act);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
