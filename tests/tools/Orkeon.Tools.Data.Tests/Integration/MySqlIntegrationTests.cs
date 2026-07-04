using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;
using Testcontainers.MySql;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for MySQL using Testcontainers (MySqlContainer).
/// </summary>
[Trait("Category", "Integration")]
public sealed class MySqlIntegrationTests : DatabaseTestFixture, IAsyncLifetime
{
    private const string ProviderName = "MySqlConnector";
    private const string SkipReason = "Docker is not available or container failed to start.";

    private MySqlContainer? _container;
    private RelationalDatabaseTool _tool = null!;
    private DatabaseSchemaTool _schemaTool = null!;
    private string _connectionString = "";
    private bool _containerStarted;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MySqlBuilder("mysql:8.0")
                .Build();
            // R5.6: bound container startup so a wedged Docker daemon or stalled
            // image pull cannot hang the test run; on timeout the tests skip.
            using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _container.StartAsync(startupCts.Token);
            _containerStarted = true;
        }
        catch
        {
            _containerStarted = false;
            return;
        }

        _connectionString = _container.GetConnectionString();
        _tool = CreateRelationalTool();
        _schemaTool = CreateSchemaTool();

        await SeedDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _tool?.Dispose();
        _schemaTool?.Dispose();
        if (_containerStarted && _container is not null)
            await _container.DisposeAsync();
    }

    private void SkipIfContainerUnavailable()
    {
        Assert.SkipWhen(!_containerStarted, SkipReason);
    }

    private async Task SeedDatabaseAsync()
    {
        var factory = ProviderFactory.GetProvider(ProviderName);
        await using var connection = factory.CreateConnection()!;
        connection.ConnectionString = _connectionString;
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE articles (
                id INT AUTO_INCREMENT PRIMARY KEY,
                title VARCHAR(200) NOT NULL,
                author VARCHAR(100),
                published_at DATETIME,
                views INT DEFAULT 0
            );
            CREATE INDEX idx_articles_author ON articles(author);
            INSERT INTO articles (title, author, published_at, views) VALUES ('First Post', 'Alice', '2026-01-01', 100);
            INSERT INTO articles (title, author, published_at, views) VALUES ('Second Post', 'Bob', '2026-02-15', 250);
            INSERT INTO articles (title, author, published_at, views) VALUES ('Third Post', 'Alice', '2026-03-01', 50);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── SELECT tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_AllRows_ReturnsAllArticles()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT id, title, author FROM articles ORDER BY id");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(3, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_WithFilter_ReturnsAuthorArticles()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT title FROM articles WHERE author = 'Alice' ORDER BY title");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(2, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_ParameterizedQuery_ReturnsFilteredResults()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT title, views FROM articles WHERE views > @minViews",
            parameters: new Dictionary<string, object> { ["@minViews"] = 75 });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(2, Convert.ToInt32(data["row_count"]));
    }

    // ── INSERT / UPDATE / DELETE ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Insert_AddsRow()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO articles (title, author, views) VALUES ('New Post', 'Charlie', 0)",
            queryType: "Execute");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["affected_rows"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Update_ModifiesRow()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE articles SET views = 300 WHERE title = 'Second Post'",
            queryType: "Execute");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["affected_rows"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Delete_RemovesRow()
    {
        SkipIfContainerUnavailable();

        var insertReq = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO articles (title, author, views) VALUES ('Temp', 'Temp', 0)",
            queryType: "Execute");
        await _tool.CallAsync(insertReq, TestContext.Current.CancellationToken);

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM articles WHERE title = 'Temp'",
            queryType: "Execute");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["affected_rows"]));
    }

    // ── Scalar test ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Scalar_Count_ReturnsCorrectValue()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM articles",
            queryType: "Scalar");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["scalar_result"]);
    }

    // ── Schema introspection ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_All_ReturnsCompleteMetadata()
    {
        SkipIfContainerUnavailable();

        var request = BuildSchemaRequest(_connectionString, ProviderName, "All", "articles");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
    }

    // ── Full CRUD cycle ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FullCrudCycle_CreateReadUpdateDelete_Succeeds()
    {
        SkipIfContainerUnavailable();

        // Create
        var insert = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO articles (title, author, views) VALUES ('CRUDItem', 'Test', 0)",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(insert, TestContext.Current.CancellationToken)).Success);

        // Read
        var select = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT title FROM articles WHERE title = 'CRUDItem'");
        var selectResult = await _tool.CallAsync(select, TestContext.Current.CancellationToken);
        Assert.True(selectResult.Success);
        var selectData = Assert.IsType<Dictionary<string, object>>(selectResult.Result);
        Assert.Equal(1, Convert.ToInt32(selectData["row_count"]));

        // Update
        var update = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE articles SET views = 999 WHERE title = 'CRUDItem'",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(update, TestContext.Current.CancellationToken)).Success);

        // Delete
        var delete = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM articles WHERE title = 'CRUDItem'",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(delete, TestContext.Current.CancellationToken)).Success);

        // Verify
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM articles WHERE title = 'CRUDItem'",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success);
        var verifyData = Assert.IsType<Dictionary<string, object>>(verifyResult.Result);
        Assert.Equal(0L, Convert.ToInt64(verifyData["scalar_result"]));
    }
}
