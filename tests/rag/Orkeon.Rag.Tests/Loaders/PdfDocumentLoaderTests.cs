using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Tests.Shared.FileSystem;
using UglyToad.PdfPig.Writer;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// Tests for the ported <see cref="PdfDocumentLoader"/> (new
/// <c>Orkeon.Rag.Abstractions</c> contract, VFS-backed). PDF fixtures are built
/// in memory with the PdfPig writer.
/// </summary>
public class PdfDocumentLoaderTests
{
    private static (PdfDocumentLoader Loader, FakeFileSystemService Fs) CreateLoader()
    {
        var fs = new FakeFileSystemService().AddMount("/kb");
        return (new PdfDocumentLoader(fs), fs);
    }

    private static byte[] BuildPdf(string text, string? title = null)
    {
        using var builder = new PdfDocumentBuilder();
        if (title is not null)
        {
            builder.DocumentInformation.Title = title;
        }

        var font = builder.AddStandard14Font(
            UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        if (!string.IsNullOrEmpty(text))
        {
            page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        }

        return builder.Build();
    }

    private static async Task<RagDocument> LoadSingleAsync(
        PdfDocumentLoader loader, string location)
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

    [Fact]
    public void CanLoad_PdfExtensionOnly()
    {
        var (loader, _) = CreateLoader();

        Assert.True(loader.CanLoad(new SourceDescriptor { Location = "/kb/doc.pdf" }));
        Assert.False(loader.CanLoad(new SourceDescriptor { Location = "/kb/doc.txt" }));
    }

    [Fact]
    public async Task LoadAsync_ExtractsTextAndPageCount()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/doc.pdf", BuildPdf("Retrieval augmented generation"));

        var doc = await LoadSingleAsync(loader, "/kb/doc.pdf");

        Assert.Contains("Retrieval augmented generation", doc.Content);
        Assert.Equal("1", doc.Metadata["page_count"]);
        Assert.Equal(".pdf", doc.Metadata["extension"]);
    }

    [Fact]
    public async Task LoadAsync_PdfTitle_ExposedAsDocumentTitle()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/doc.pdf", BuildPdf("body", title: "RAG Handbook"));

        var doc = await LoadSingleAsync(loader, "/kb/doc.pdf");

        Assert.Equal("RAG Handbook", doc.Title);
        Assert.Equal("RAG Handbook", doc.Metadata["title"]);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFound()
    {
        var (loader, _) = CreateLoader();

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await LoadSingleAsync(loader, "/kb/absent.pdf"));
    }
}
