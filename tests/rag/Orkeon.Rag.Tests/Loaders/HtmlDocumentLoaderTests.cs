using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// Tests for the ported <see cref="HtmlDocumentLoader"/> (new
/// <c>Orkeon.Rag.Abstractions</c> contract, VFS-backed).
/// </summary>
public class HtmlDocumentLoaderTests
{
    private static (HtmlDocumentLoader Loader, FakeFileSystemService Fs) CreateLoader()
    {
        var fs = new FakeFileSystemService().AddMount("/kb");
        return (new HtmlDocumentLoader(fs), fs);
    }

    private static async Task<RagDocument> LoadSingleAsync(
        HtmlDocumentLoader loader, string location)
    {
        RagDocument? result = null;
        await foreach (var doc in loader.LoadAsync(
            new SourceDescriptor { Location = location }, TestContext.Current.CancellationToken))
        {
            result = doc;
        }

        Assert.NotNull(result);
        return result;
    }

    [Theory]
    [InlineData("/kb/page.html", true)]
    [InlineData("/kb/page.htm", true)]
    [InlineData("/kb/page.txt", false)]
    public void CanLoad_HtmlExtensionsOnly(string location, bool expected)
    {
        var (loader, _) = CreateLoader();

        Assert.Equal(expected, loader.CanLoad(new SourceDescriptor { Location = location }));
    }

    [Fact]
    public async Task LoadAsync_StripsScriptsStylesAndTags()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/page.html",
            "<html><head><title>My Page</title><style>body{color:red}</style></head>" +
            "<body><script>alert('x')</script><p>Visible   text</p></body></html>");

        var doc = await LoadSingleAsync(loader, "/kb/page.html");

        Assert.Contains("Visible text", doc.Content);
        Assert.DoesNotContain("alert", doc.Content);
        Assert.DoesNotContain("color:red", doc.Content);
        Assert.DoesNotContain("<p>", doc.Content);
    }

    [Fact]
    public async Task LoadAsync_ExtractsTitleIntoMetadataAndDocumentTitle()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/page.html", "<html><head><title>My Page</title></head><body>x</body></html>");

        var doc = await LoadSingleAsync(loader, "/kb/page.html");

        Assert.Equal("My Page", doc.Metadata["title"]);
        Assert.Equal("My Page", doc.Title);
    }

    [Fact]
    public async Task LoadAsync_NoTitle_FallsBackToFileName()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/page.html", "<html><body>x</body></html>");

        var doc = await LoadSingleAsync(loader, "/kb/page.html");

        Assert.Equal("page.html", doc.Title);
        Assert.False(doc.Metadata.ContainsKey("title"));
    }
}
