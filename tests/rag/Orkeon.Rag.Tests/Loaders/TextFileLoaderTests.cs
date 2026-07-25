using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// Tests for the ported <see cref="TextFileLoader"/> (new
/// <c>Orkeon.Rag.Abstractions</c> contract, VFS-backed).
/// </summary>
public class TextFileLoaderTests
{
    private static (TextFileLoader Loader, FakeFileSystemService Fs) CreateLoader()
    {
        var fs = new FakeFileSystemService().AddMount("/kb");
        return (new TextFileLoader(fs), fs);
    }

    private static async Task<List<RagDocument>> CollectAsync(
        TextFileLoader loader, SourceDescriptor source)
    {
        var documents = new List<RagDocument>();
        await foreach (var doc in loader.LoadAsync(source, TestContext.Current.CancellationToken))
        {
            documents.Add(doc);
        }

        return documents;
    }

    [Theory]
    [InlineData("/kb/notes.txt")]
    [InlineData("/kb/readme.md")]
    [InlineData("/kb/guide.markdown")]
    [InlineData("/kb/raw.text")]
    [InlineData("/kb/app.log")]
    public void CanLoad_SupportedExtensions_ReturnsTrue(string location)
    {
        var (loader, _) = CreateLoader();

        Assert.True(loader.CanLoad(new SourceDescriptor { Location = location }));
    }

    [Theory]
    [InlineData("/kb/data.csv")]
    [InlineData("/kb/page.html")]
    [InlineData("/kb/doc.pdf")]
    [InlineData("")]
    public void CanLoad_UnsupportedOrBlankLocation_ReturnsFalse(string location)
    {
        var (loader, _) = CreateLoader();

        Assert.False(loader.CanLoad(new SourceDescriptor { Location = location }));
    }

    [Fact]
    public void CanLoad_NonFileKindHint_ReturnsFalse()
    {
        var (loader, _) = CreateLoader();

        var source = new SourceDescriptor { Location = "/kb/notes.txt", Kind = "url" };

        Assert.False(loader.CanLoad(source));
    }

    [Fact]
    public void CanLoad_FileKindHint_ReturnsTrue()
    {
        var (loader, _) = CreateLoader();

        var source = new SourceDescriptor { Location = "/kb/notes.txt", Kind = "file" };

        Assert.True(loader.CanLoad(source));
    }

    [Fact]
    public async Task LoadAsync_YieldsSingleDocumentWithContentAndMetadata()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/notes.txt", "hello knowledge");

        var documents = await CollectAsync(loader, new SourceDescriptor { Location = "/kb/notes.txt" });

        var doc = Assert.Single(documents);
        Assert.Equal("hello knowledge", doc.Content);
        Assert.Equal("/kb/notes.txt", doc.Id);
        Assert.Equal("/kb/notes.txt", doc.SourceId);
        Assert.Equal("/kb/notes.txt", doc.Location);
        Assert.Equal("notes.txt", doc.Metadata["file_name"]);
        Assert.Equal("/kb/notes.txt", doc.Metadata["file_path"]);
        Assert.Equal(".txt", doc.Metadata["extension"]);
        Assert.True(doc.Metadata.ContainsKey("file_size"));
        Assert.True(doc.Metadata.ContainsKey("last_modified"));
    }

    [Fact]
    public async Task LoadAsync_ExplicitSourceId_UsedAsDocumentIdentity()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/notes.txt", "content");

        var source = new SourceDescriptor { Location = "/kb/notes.txt", SourceId = "kb-notes" };
        var documents = await CollectAsync(loader, source);

        var doc = Assert.Single(documents);
        Assert.Equal("kb-notes", doc.Id);
        Assert.Equal("kb-notes", doc.SourceId);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFound()
    {
        var (loader, _) = CreateLoader();

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await CollectAsync(loader, new SourceDescriptor { Location = "/kb/absent.txt" }));
    }
}
