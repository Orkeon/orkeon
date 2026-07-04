using System.Net;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public class WebPageLoaderTests
{
    [Fact]
    public async Task ShouldFetchAndParseContent_WhenLoadingWebUrl()
    {
        using var fixture = new WebPageLoaderTestsFixture();
        var html = "<html><head><title>Test</title></head><body><h1>Hello Web</h1><p>Content here.</p></body></html>";

        var result = await fixture
            .WithResponse(html)
            .LoadAsync("https://example.com/page");

        Assert.Contains("Hello Web", result.Content);
        Assert.Contains("Content here.", result.Content);
        Assert.DoesNotContain("<h1>", result.Content);
        Assert.Equal("https://example.com/page", result.SourceId);
        Assert.Equal("web", result.SourceType);
    }

    [Fact]
    public async Task ShouldExtractTitleToMetadata_WhenLoadingWebUrl()
    {
        using var fixture = new WebPageLoaderTestsFixture();
        var html = "<html><head><title>Page Title</title></head><body><p>Text</p></body></html>";

        var result = await fixture
            .WithResponse(html)
            .LoadAsync(TestBaseUrl);

        Assert.True(result.Metadata.ContainsKey("title"));
        Assert.Equal("Page Title", result.Metadata["title"]);
    }

    [Fact]
    public async Task ShouldIncludeMetadata_WhenLoadingWebUrl()
    {
        using var fixture = new WebPageLoaderTestsFixture();

        var result = await fixture
            .WithResponse("<html><body>Hello</body></html>")
            .LoadAsync(TestBaseUrl);

        Assert.True(result.Metadata.ContainsKey(ParamUrl));
        Assert.Equal(TestBaseUrl, result.Metadata[ParamUrl]);
        Assert.True(result.Metadata.ContainsKey("status_code"));
        Assert.Equal(200, result.Metadata["status_code"]);
        Assert.True(result.Metadata.ContainsKey("fetched_at"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadHttpUrl()
    {
        using var fixture = new WebPageLoaderTestsFixture();
        var loader = fixture.WithResponse("").GetLoader();
        Assert.True(loader.CanLoad("http://example.com"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadHttpsUrl()
    {
        using var fixture = new WebPageLoaderTestsFixture();
        var loader = fixture.WithResponse("").GetLoader();
        Assert.True(loader.CanLoad("https://example.com/page"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadFilePath()
    {
        using var fixture = new WebPageLoaderTestsFixture();
        var loader = fixture.WithResponse("").GetLoader();
        Assert.False(loader.CanLoad("/tmp/file.txt"));
        Assert.False(loader.CanLoad("C:\\file.txt"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNullOrEmpty()
    {
        using var fixture = new WebPageLoaderTestsFixture();
        var loader = fixture.WithResponse("").GetLoader();
        Assert.False(loader.CanLoad(null!));
        Assert.False(loader.CanLoad(""));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenLoadingNonHttpUrl()
    {
        using var fixture = new WebPageLoaderTestsFixture();

        var act = () => fixture
            .WithResponse("")
            .LoadAsync("/tmp/file.txt");

        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task ShouldThrowHttpRequestException_WhenHttpErrorOccurs()
    {
        using var fixture = new WebPageLoaderTestsFixture();

        var act = () => fixture
            .WithResponse("Not Found", HttpStatusCode.NotFound)
            .LoadAsync("https://example.com/missing");

        await Assert.ThrowsAsync<HttpRequestException>(act);
    }
}
