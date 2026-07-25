using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Loaders;

/// <summary>
/// Tests for the ported <see cref="CsvDocumentLoader"/> (new
/// <c>Orkeon.Rag.Abstractions</c> contract, VFS-backed).
/// </summary>
public class CsvDocumentLoaderTests
{
    private static (CsvDocumentLoader Loader, FakeFileSystemService Fs) CreateLoader()
    {
        var fs = new FakeFileSystemService().AddMount("/kb");
        return (new CsvDocumentLoader(fs), fs);
    }

    private static async Task<RagDocument> LoadSingleAsync(
        CsvDocumentLoader loader, string location)
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
    public void CanLoad_CsvExtensionOnly()
    {
        var (loader, _) = CreateLoader();

        Assert.True(loader.CanLoad(new SourceDescriptor { Location = "/kb/data.csv" }));
        Assert.False(loader.CanLoad(new SourceDescriptor { Location = "/kb/data.txt" }));
    }

    [Fact]
    public async Task LoadAsync_ConvertsRowsToHeaderValuePairs()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/people.csv", "name,age\nAda,36\nAlan,41\n");

        var doc = await LoadSingleAsync(loader, "/kb/people.csv");

        Assert.Contains("name: Ada, age: 36", doc.Content);
        Assert.Contains("name: Alan, age: 41", doc.Content);
        Assert.Equal("2", doc.Metadata["row_count"]);
        Assert.Equal("2", doc.Metadata["column_count"]);
    }

    [Fact]
    public async Task LoadAsync_QuotedFieldsWithCommasAndEscapedQuotes()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/quotes.csv", "title,notes\n\"Hello, world\",\"She said \"\"hi\"\"\"\n");

        var doc = await LoadSingleAsync(loader, "/kb/quotes.csv");

        Assert.Contains("title: Hello, world", doc.Content);
        Assert.Contains("notes: She said \"hi\"", doc.Content);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ProducesEmptyContentWithZeroCounts()
    {
        var (loader, fs) = CreateLoader();
        fs.AddFile("/kb/empty.csv", "");

        var doc = await LoadSingleAsync(loader, "/kb/empty.csv");

        Assert.Equal(string.Empty, doc.Content);
        Assert.Equal("0", doc.Metadata["row_count"]);
        Assert.Equal("0", doc.Metadata["column_count"]);
    }
}
