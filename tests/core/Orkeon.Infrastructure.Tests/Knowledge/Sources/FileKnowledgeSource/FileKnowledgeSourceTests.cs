using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Tests.Knowledge.Sources;

public sealed class FileKnowledgeSourceTests : IDisposable
{
    private readonly FileKnowledgeSourceTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldLoadFile_WhenGettingContent()
    {
        var content = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var path = _fixture.CreateTempFile(".txt", content);
        var source = _fixture.CreateSource(path);

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Contains("First paragraph", result.Content);
        Assert.Contains("Second paragraph", result.Content);
        Assert.Contains("Third paragraph", result.Content);
        Assert.Equal(path, result.Source);
    }

    [Fact]
    public async Task ShouldFindMatchingChunks_WhenSearching()
    {
        var content = "The quick brown fox.\n\nThe lazy dog slept.\n\nThe bright sun shone.";
        var path = _fixture.CreateTempFile(".txt", content);
        var source = _fixture.CreateSource(path,
            new ChunkingOptions { ChunkSize = 30, ChunkOverlap = 0 });

        var results = await source.SearchAsync("fox", cancellationToken: TestContext.Current.CancellationToken);

        var resultList = results.ToList();
        Assert.NotEmpty(resultList);
        Assert.Contains("fox", resultList.First().Content);
    }

    [Fact]
    public async Task ShouldReturnChunks_WhenSearchQueryIsEmpty()
    {
        var content = "Some text content.";
        var path = _fixture.CreateTempFile(".txt", content);
        var source = _fixture.CreateSource(path);

        var results = await source.SearchAsync("", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchHasNoMatch()
    {
        var content = "The quick brown fox.";
        var path = _fixture.CreateTempFile(".txt", content);
        var source = _fixture.CreateSource(path);

        var results = await source.SearchAsync("elephant", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public void ShouldReturnValidId_WhenAccessingId()
    {
        var path = "/tmp/test.txt";
        var source = _fixture.CreateSource(path);

        Assert.NotNull(source.Id);
    }

    [Fact]
    public void ShouldReturnFileName_WhenAccessingName()
    {
        var path = "/tmp/myfile.txt";
        var source = _fixture.CreateSource(path);

        Assert.Equal("myfile.txt", source.Name);
    }

    [Fact]
    public void ShouldReturnFile_WhenAccessingType()
    {
        var source = _fixture.CreateSource("/tmp/test.txt");

        Assert.Equal("file", source.Type);
    }

    [Fact]
    public async Task ShouldReturnMetadataWithChunkCount_WhenGettingContent()
    {
        var content = "Some text.";
        var path = _fixture.CreateTempFile(".txt", content);
        var source = _fixture.CreateSource(path);

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Metadata.ContainsKey("chunk_count"));
        Assert.True(result.Metadata.ContainsKey("source_type"));
        Assert.Equal("file", result.Metadata["source_type"]);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
