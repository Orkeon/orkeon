using static Orkeon.Tests.Shared.Constants.TestDataConstants;
namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class TextFileLoaderTests : IDisposable
{
    private readonly TextFileLoaderTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnContent_WhenLoadingTextFile()
    {
        var content = "Hello, world!\nThis is a test file.";
        var path = _fixture.CreateTempFile(".txt", content);

        var result = await _fixture.LoadAsync(path);

        Assert.Equal(content, result.Content);
        Assert.Equal(path, result.SourceId);
        Assert.Equal("text", result.SourceType);
        Assert.True(result.Metadata.ContainsKey("file_name"));
        Assert.Equal(".txt", result.Metadata["extension"]);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenLoadingMarkdownFile()
    {
        var content = "# Heading\n\nSome **bold** text.";
        var path = _fixture.CreateTempFile(".md", content);

        var result = await _fixture.LoadAsync(path);

        Assert.Equal(content, result.Content);
        Assert.Equal(".md", result.Metadata["extension"]);
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadTxtFile()
    {
        Assert.True(_fixture.CanLoad("document.txt"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadMdFile()
    {
        Assert.True(_fixture.CanLoad("readme.md"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadPdfFile()
    {
        Assert.False(_fixture.CanLoad("document.pdf"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadCsvFile()
    {
        Assert.False(_fixture.CanLoad("data.csv"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNullOrEmpty()
    {
        Assert.False(_fixture.CanLoad(null!));
        Assert.False(_fixture.CanLoad(""));
        Assert.False(_fixture.CanLoad("  "));
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenLoadingNonExistentFile()
    {
        var path = $"/nonexistent_{Guid.NewGuid()}.txt";

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
    public async Task ShouldReturnFileMetadata_WhenLoadingFile()
    {
        var content = TestContent;
        var path = _fixture.CreateTempFile(".txt", content);

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
