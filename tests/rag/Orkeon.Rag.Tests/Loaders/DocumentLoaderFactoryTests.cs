using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Loaders;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// Tests for the new <see cref="DocumentLoaderFactory"/>: selection by
/// <c>CanLoad</c>, loud failure when no loader matches.
/// </summary>
public class DocumentLoaderFactoryTests
{
    private static DocumentLoaderFactory CreateFactory()
    {
        var fs = new FakeFileSystemService().AddMount("/kb");
        return new DocumentLoaderFactory(
        [
            new TextFileLoader(fs),
            new CsvDocumentLoader(fs),
            new HtmlDocumentLoader(fs),
            new PdfDocumentLoader(fs),
        ]);
    }

    [Theory]
    [InlineData("/kb/a.txt", typeof(TextFileLoader))]
    [InlineData("/kb/a.csv", typeof(CsvDocumentLoader))]
    [InlineData("/kb/a.html", typeof(HtmlDocumentLoader))]
    [InlineData("/kb/a.pdf", typeof(PdfDocumentLoader))]
    public void GetLoader_SelectsLoaderByExtension(string location, Type expectedLoader)
    {
        var factory = CreateFactory();

        var loader = factory.GetLoader(new SourceDescriptor { Location = location });

        Assert.IsType(expectedLoader, loader);
    }

    [Fact]
    public void GetLoader_NoLoaderMatches_ThrowsWithKnownLoaderList()
    {
        var factory = CreateFactory();

        var ex = Assert.Throws<RagComponentNotFoundException>(
            () => factory.GetLoader(new SourceDescriptor { Location = "/kb/a.unknown" }));

        Assert.Contains("document loader", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(TextFileLoader), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(PdfDocumentLoader), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetLoader_NoLoaderMatches_ReturnsFalseWithoutThrowing()
    {
        var factory = CreateFactory();

        var found = factory.TryGetLoader(new SourceDescriptor { Location = "/kb/a.unknown" }, out var loader);

        Assert.False(found);
        Assert.Null(loader);
    }

    [Fact]
    public void GetLoader_FirstMatchingLoaderWins()
    {
        var fs = new FakeFileSystemService().AddMount("/kb");
        var first = new TextFileLoader(fs);
        var second = new TextFileLoader(fs);
        var factory = new DocumentLoaderFactory([first, second]);

        var resolved = factory.GetLoader(new SourceDescriptor { Location = "/kb/a.txt" });

        Assert.Same(first, resolved);
    }
}
