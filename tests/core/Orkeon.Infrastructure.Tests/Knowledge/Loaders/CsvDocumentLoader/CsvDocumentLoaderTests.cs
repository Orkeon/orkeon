namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class CsvDocumentLoaderTests : IDisposable
{
    private readonly CsvDocumentLoaderTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldParseRows_WhenLoadingCsvFile()
    {
        var csvContent = "Name,Age,City\nAlice,30,Paris\nBob,25,London";
        var path = _fixture.CreateTempFile(csvContent);

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Name: Alice", result.Content);
        Assert.Contains("Age: 30", result.Content);
        Assert.Contains("City: Paris", result.Content);
        Assert.Contains("Name: Bob", result.Content);
        Assert.Equal("csv", result.SourceType);
    }

    [Fact]
    public async Task ShouldContainRowAndColumnCounts_WhenLoadingCsvFileMetadata()
    {
        var csvContent = "Name,Age,City\nAlice,30,Paris\nBob,25,London";
        var path = _fixture.CreateTempFile(csvContent);

        var result = await _fixture.LoadAsync(path);

        Assert.Equal(2, result.Metadata["row_count"]);
        Assert.Equal(3, result.Metadata["column_count"]);
    }

    [Fact]
    public async Task ShouldParseCorrectly_WhenCsvHasQuotedFields()
    {
        var csvContent = "Name,Description\nAlice,\"Has a, comma\"\nBob,\"Simple\"";
        var path = _fixture.CreateTempFile(csvContent);

        var result = await _fixture.LoadAsync(path);

        Assert.Contains("Description: Has a, comma", result.Content);
    }

    [Fact]
    public async Task ShouldReturnEmptyContent_WhenCsvIsEmpty()
    {
        var path = _fixture.CreateTempFile("");

        var result = await _fixture.LoadAsync(path);

        Assert.Empty(result.Content);
    }

    [Fact]
    public void ShouldReturnTrue_WhenCheckingCanLoadCsvFile()
    {
        Assert.True(_fixture.CanLoad("data.csv"));
        Assert.True(_fixture.CanLoad("DATA.CSV"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadTxtFile()
    {
        Assert.False(_fixture.CanLoad("data.txt"));
    }

    [Fact]
    public void ShouldReturnFalse_WhenCheckingCanLoadNullOrEmpty()
    {
        Assert.False(_fixture.CanLoad(null!));
        Assert.False(_fixture.CanLoad(""));
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenLoadingNonExistentFile()
    {
        var path = $"/nonexistent_{Guid.NewGuid()}.csv";

        var act = () => _fixture.LoadAsync(path);

        await Assert.ThrowsAsync<FileNotFoundException>(act);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
