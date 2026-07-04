using static Orkeon.Tests.Shared.Constants.TestUrlConstants;
namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class DocumentLoaderFactoryTests : IDisposable
{
    private readonly DocumentLoaderFactoryTestsFixture _fixture = new();

    [Fact]
    public void ShouldReturnTextLoader_WhenGettingLoaderForTextType()
    {
        var loader = _fixture.GetLoader("text");

        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.TextFileLoader>(loader);
    }

    [Fact]
    public void ShouldReturnCsvLoader_WhenGettingLoaderForCsvType()
    {
        var loader = _fixture.GetLoader("csv");

        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.CsvDocumentLoader>(loader);
    }

    [Fact]
    public void ShouldReturnHtmlLoader_WhenGettingLoaderForHtmlType()
    {
        var loader = _fixture.GetLoader("html");

        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.HtmlDocumentLoader>(loader);
    }

    [Fact]
    public void ShouldReturnWebPageLoader_WhenGettingLoaderForWebType()
    {
        var loader = _fixture.GetLoader("web");

        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.WebPageLoader>(loader);
    }

    [Fact]
    public void ShouldReturnLoader_WhenTypeIsCaseInsensitive()
    {
        var loader = _fixture.GetLoader("TEXT");

        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.TextFileLoader>(loader);
    }

    [Fact]
    public void ShouldThrowNotSupported_WhenTypeIsUnknown()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _fixture.GetLoader("xml"));
        Assert.Contains("xml", ex.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenTypeIsNullOrEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => _fixture.GetLoader(null!));
        Assert.Throws<ArgumentException>(() => _fixture.GetLoader(""));
    }

    [Fact]
    public void ShouldReturnTextLoader_WhenSourceIsTxtFile()
    {
        var loader = _fixture.GetLoaderForSource("document.txt");

        Assert.NotNull(loader);
        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.TextFileLoader>(loader);
    }

    [Fact]
    public void ShouldReturnTextLoader_WhenSourceIsMdFile()
    {
        var loader = _fixture.GetLoaderForSource("readme.md");

        Assert.NotNull(loader);
        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.TextFileLoader>(loader);
    }

    [Fact]
    public void ShouldReturnCsvLoader_WhenSourceIsCsvFile()
    {
        var loader = _fixture.GetLoaderForSource("data.csv");

        Assert.NotNull(loader);
        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.CsvDocumentLoader>(loader);
    }

    [Fact]
    public void ShouldReturnHtmlLoader_WhenSourceIsHtmlFile()
    {
        var loader = _fixture.GetLoaderForSource("page.html");

        Assert.NotNull(loader);
        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.HtmlDocumentLoader>(loader);
    }

    [Fact]
    public void ShouldReturnWebPageLoader_WhenSourceIsHttpUrl()
    {
        var loader = _fixture.GetLoaderForSource(TestBaseUrl);

        Assert.NotNull(loader);
        Assert.IsType<global::Orkeon.Infrastructure.Knowledge.Loaders.WebPageLoader>(loader);
    }

    [Fact]
    public void ShouldReturnNull_WhenSourceHasUnknownExtension()
    {
        var loader = _fixture.GetLoaderForSource("file.xyz");

        Assert.Null(loader);
    }

    [Fact]
    public void ShouldReturnNull_WhenSourceIsNullOrEmpty()
    {
        Assert.Null(_fixture.GetLoaderForSource(null!));
        Assert.Null(_fixture.GetLoaderForSource(""));
    }

    [Fact]
    public void ShouldReturnAllTypes_WhenGettingSupportedTypes()
    {
        var types = _fixture.GetSupportedTypes();

        Assert.Contains("text", types);
        Assert.Contains("csv", types);
        Assert.Contains("html", types);
        Assert.Contains("web", types);
        Assert.Equal(4, types.Count());
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
