namespace Orkeon.Infrastructure.Tests.Knowledge.Sources;

public sealed class DirectoryKnowledgeSourceTests : IDisposable
{
    private readonly DirectoryKnowledgeSourceTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldLoadAllFiles_WhenGettingContent()
    {
        var source = _fixture
            .WithFile("file1.txt", "Content from file one.")
            .WithFile("file2.txt", "Content from file two.")
            .WithFile("file3.md", "Content from file three.")
            .CreateSource();

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Content from file one", result.Content);
        Assert.Contains("Content from file two", result.Content);
        Assert.Contains("Content from file three", result.Content);
    }

    [Fact]
    public async Task ShouldSearchAcrossFiles_WhenSearching()
    {
        var source = _fixture
            .WithFile("animals.txt", "The quick brown fox jumps over the lazy dog.")
            .WithFile("weather.txt", "The sun shines brightly on a warm day.")
            .CreateSource();

        var results = await source.SearchAsync("fox", cancellationToken: TestContext.Current.CancellationToken);

        var resultList = results.ToList();
        Assert.NotEmpty(resultList);
        Assert.Contains("fox", resultList.First().Content);
    }

    [Fact]
    public async Task ShouldSkipUnsupportedFiles_WhenGettingContent()
    {
        var source = _fixture
            .WithFile("supported.txt", "Supported content.")
            .WithFile("unsupported.xyz", "Unsupported content.")
            .CreateSource();

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Supported content", result.Content);
        Assert.DoesNotContain("Unsupported content", result.Content);
    }

    [Fact]
    public async Task ShouldLoadHtmlFiles_WhenGettingContent()
    {
        var source = _fixture
            .WithFile("page.html", "<html><body><p>HTML content here.</p></body></html>")
            .WithFile("notes.txt", "Text content here.")
            .CreateSource();

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Contains("HTML content here", result.Content);
        Assert.Contains("Text content here", result.Content);
    }

    [Fact]
    public void ShouldReturnDirectoryName_WhenAccessingName()
    {
        var source = _fixture.CreateSource();

        // Virtual root "/" resolves to "root" per spec
        Assert.Equal("root", source.Name);
    }

    [Fact]
    public void ShouldReturnDirectory_WhenAccessingType()
    {
        var source = _fixture.CreateSource();

        Assert.Equal("directory", source.Type);
    }

    [Fact]
    public async Task ShouldThrowDirectoryNotFound_WhenDirectoryDoesNotExist()
    {
        using var emptyFixture = new DirectoryKnowledgeSourceTestsFixture();
        // Create a source pointing at a virtual sub-path that doesn't exist under the fixture root
        var source = emptyFixture.CreateSourceAtPath("/nonexistent-subdir");

        var act = () => source.GetContentAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(act);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchHasNoMatch()
    {
        var source = _fixture
            .WithFile("file.txt", "The quick brown fox.")
            .CreateSource();

        var results = await source.SearchAsync("elephant", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
