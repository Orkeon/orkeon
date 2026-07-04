namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class PdfDocumentLoaderTests : IDisposable
{
    private readonly PdfDocumentLoaderTestsFixture _fixture = new();

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadPdfExtension()
    {
        var path = _fixture.CreateTempPdf("test");

        Assert.True(_fixture.CanLoad(path));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNonPdfExtension()
    {
        Assert.False(_fixture.CanLoad("document.txt"));
        Assert.False(_fixture.CanLoad("data.csv"));
        Assert.False(_fixture.CanLoad("page.html"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadPdfExtensionRegardlessOfExistence()
    {
        // CanLoad only checks the extension, not existence — existence check happens in LoadAsync
        var path = $"/nonexistent_{Guid.NewGuid()}.pdf";

        Assert.True(_fixture.CanLoad(path));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNullOrEmpty()
    {
        Assert.False(_fixture.CanLoad(null!));
        Assert.False(_fixture.CanLoad(""));
        Assert.False(_fixture.CanLoad("  "));
    }

    [Fact]
    public void ShouldReturnPdf_WhenGettingSupportedType()
    {
        var loader = _fixture.GetLoader();

        Assert.Equal("pdf", loader.SupportedType);
    }

    [Fact]
    public async Task ShouldExtractText_WhenLoadingPdfFile()
    {
        var path = _fixture.CreateTempPdf("Hello World");

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Hello World", result.Content);
        Assert.Equal("pdf", result.SourceType);
        Assert.Equal(path, result.SourceId);
    }

    [Fact]
    public async Task ShouldExtractMultiPageText_WhenLoadingMultiPagePdf()
    {
        var path = _fixture.CreateTempMultiPagePdf("Page One Content", "Page Two Content");

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Page One Content", result.Content);
        Assert.Contains("Page Two Content", result.Content);
        Assert.Equal(2, result.Metadata["pageCount"]);
    }

    [Fact]
    public async Task ShouldExtractMetadata_WhenLoadingPdfWithMetadata()
    {
        var path = _fixture.CreateTempPdfWithMetadata(
            "Some content",
            title: "Test Document",
            author: "Test Author",
            creator: "Test Creator");

        var result = await _fixture.LoadAsync(path);

        Assert.Equal("Test Document", result.Metadata["title"]);
        Assert.Equal("Test Author", result.Metadata["author"]);
        Assert.Equal("Test Creator", result.Metadata["creator"]);
        Assert.True(result.Metadata.ContainsKey("pageCount"));
        Assert.True(result.Metadata.ContainsKey("file_size"));
        Assert.True(result.Metadata.ContainsKey("file_name"));
        Assert.True(result.Metadata.ContainsKey("file_path"));
        Assert.Equal(".pdf", result.Metadata["extension"]);
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenLoadingNonExistentFile()
    {
        var path = $"/nonexistent_{Guid.NewGuid()}.pdf";

        var act = () => _fixture.LoadAsync(path);

        await Assert.ThrowsAsync<FileNotFoundException>(act);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenSourceIsEmpty()
    {
        var act = () => _fixture.LoadAsync("");

        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task ShouldReturnEmptyContent_WhenLoadingEmptyPdf()
    {
        var path = _fixture.CreateEmptyPdf();

        var result = await _fixture.LoadAsync(path);

        Assert.Empty(result.Content);
        Assert.Equal("pdf", result.SourceType);
        Assert.Equal(1, result.Metadata["pageCount"]);
    }

    [Fact]
    public async Task ShouldReturnFileMetadata_WhenLoadingPdf()
    {
        var path = _fixture.CreateTempPdf("Content for metadata test");

        var result = await _fixture.LoadAsync(path);

        Assert.True(result.Metadata.ContainsKey("file_name"));
        Assert.True(result.Metadata.ContainsKey("file_path"));
        Assert.True(result.Metadata.ContainsKey("file_size"));
        Assert.True(result.Metadata.ContainsKey("last_modified"));
        Assert.True((long)result.Metadata["file_size"] > 0);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
