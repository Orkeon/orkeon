using Microsoft.Data.Sqlite;
using Orkeon.Infrastructure.Knowledge.Sources;

namespace Orkeon.Infrastructure.Tests.Knowledge.Sources;

public sealed class DatabaseKnowledgeSourceTests : IDisposable
{
    private readonly DatabaseKnowledgeSourceTestsFixture _fixture = new();

    [Fact]
    public async Task GetContentAsync_ReturnsAllRows()
    {
        var connectionString = _fixture.SetupDatabase(
            out _,
            rows:
            [
                ("1", "Doc A", "First document content."),
                ("2", "Doc B", "Second document content."),
                ("3", "Doc C", "Third document content.")
            ]);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content",
            TitleColumn = "Title",
            IdColumn = "Id"
        });

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Contains("First document content.", result.Content);
        Assert.Contains("Second document content.", result.Content);
        Assert.Contains("Third document content.", result.Content);
        Assert.Equal(3, result.Metadata["row_count"]);
    }

    [Fact]
    public async Task GetContentAsync_HandlesEmptyTable()
    {
        var connectionString = _fixture.SetupDatabase(out _, rows: []);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content"
        });

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Content);
        Assert.Equal(0, result.Metadata["row_count"]);
    }

    [Fact]
    public async Task GetContentAsync_ExtractsIdAndTitle()
    {
        var connectionString = _fixture.SetupDatabase(
            out _,
            rows:
            [
                ("doc-42", "My Title", "Some content here.")
            ]);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content",
            TitleColumn = "Title",
            IdColumn = "Id"
        });

        // Use SearchAsync to get individual rows (GetContentAsync merges them)
        var results = (await source.SearchAsync("content", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Single(results);
        Assert.NotNull(results[0].Id);
        Assert.Equal("My Title", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_FiltersWithParameterizedQuery()
    {
        var connectionString = _fixture.SetupDatabase(
            out _,
            rows:
            [
                ("1", "Apple", "Fresh red apples from the orchard."),
                ("2", "Banana", "Yellow bananas from the tropics."),
                ("3", "Cherry", "Sweet cherries from the garden.")
            ]);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content",
            TitleColumn = "Title",
            IdColumn = "Id",
            SearchQuery = "SELECT Id, Title, Content FROM Documents WHERE Content LIKE '%' || @query || '%'"
        });

        var results = (await source.SearchAsync("apples", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Single(results);
        Assert.Contains("apples", results[0].Content);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToInMemoryFiltering()
    {
        var connectionString = _fixture.SetupDatabase(
            out _,
            rows:
            [
                ("1", "Apple", "Fresh red apples from the orchard."),
                ("2", "Banana", "Yellow bananas from the tropics."),
                ("3", "Cherry", "Sweet cherries from the garden.")
            ]);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content",
            TitleColumn = "Title",
            IdColumn = "Id"
            // No SearchQuery configured — should fall back to in-memory filtering
        });

        var results = (await source.SearchAsync("apples", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Single(results);
        Assert.Contains("apples", results[0].Content);
    }

    [Fact]
    public void Type_ReturnsDatabase()
    {
        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = "Data Source=:memory:",
            Query = "SELECT 1"
        });

        Assert.Equal("database", source.Type);
    }

    [Fact]
    public void Constructor_ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseKnowledgeSource(null!, SqliteFactory.Instance));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyConnectionString()
    {
        var options = new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = "",
            Query = "SELECT 1"
        };

        Assert.Throws<ArgumentException>(() =>
            new DatabaseKnowledgeSource(options, SqliteFactory.Instance));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyQuery()
    {
        var options = new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = "Data Source=:memory:",
            Query = ""
        };

        Assert.Throws<ArgumentException>(() =>
            new DatabaseKnowledgeSource(options, SqliteFactory.Instance));
    }

    [Fact]
    public async Task GetContentAsync_HandlesNullColumnValues()
    {
        var connectionString = _fixture.SetupDatabase(out _, includeNulls: true);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content",
            TitleColumn = "Title",
            IdColumn = "Id"
        });

        var result = await source.GetContentAsync(TestContext.Current.CancellationToken);

        // Should not throw; null content becomes empty string
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.Equal(1, result.Metadata["row_count"]);
    }

    [Fact]
    public async Task SearchAsync_ReturnsLimitedResults()
    {
        var rows = Enumerable.Range(1, 20)
            .Select(i => (i.ToString(), $"Title {i}", $"Content with keyword match {i}"))
            .ToArray();

        var connectionString = _fixture.SetupDatabase(out _, rows: rows);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content",
            TitleColumn = "Title",
            IdColumn = "Id"
        });

        var results = (await source.SearchAsync("keyword", limit: 5, TestContext.Current.CancellationToken)).ToList();

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyForNoMatch()
    {
        var connectionString = _fixture.SetupDatabase(
            out _,
            rows:
            [
                ("1", "Doc", "This has nothing relevant.")
            ]);

        var source = DatabaseKnowledgeSourceTestsFixture.CreateSource(new DatabaseKnowledgeSourceOptions
        {
            ConnectionString = connectionString,
            Query = "SELECT Id, Title, Content FROM Documents",
            ContentColumn = "Content"
        });

        var results = (await source.SearchAsync("elephant", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        Assert.Empty(results);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
