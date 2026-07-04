using Microsoft.Data.Sqlite;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Relational;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Relational;

public sealed class RelationalDatabaseToolTests : IDisposable
{
    private readonly RelationalDatabaseTool _tool;
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public RelationalDatabaseToolTests()
    {
        _connectionString = $"Data Source=RelToolTest{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        // Keep an open connection so the in-memory DB stays alive
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT, active INTEGER);
            INSERT INTO users (name, email, active) VALUES ('Alice', 'alice@test.com', 1);
            INSERT INTO users (name, email, active) VALUES ('Bob', 'bob@test.com', 0);
            INSERT INTO users (name, email, active) VALUES ('Charlie', 'charlie@test.com', 1);
            """;
        cmd.ExecuteNonQuery();

        _tool = new RelationalDatabaseTool(
            new DatabaseProviderFactory(),
            new DefaultDatabaseSecurityPolicy());
    }

    // ── Validation tests ─────────────────────────────────────────────────

    [Fact]
    public async Task Validation_RejectsEmptyConnectionString()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "",
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Connection string", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsEmptyQuery()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = "",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsUnsupportedProvider()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Oracle.ManagedDataAccess",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unsupported provider", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsEmptyProviderName()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Provider name", result.Error);
    }

    // ── Security tests ────────────────────────────────────────────────────

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("CREATE TABLE test (id INT)")]
    [InlineData("GRANT SELECT ON users TO public")]
    public async Task Security_BlocksDdlStatements(string query)
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = query,
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("DDL", result.Error);
    }

    // ── SELECT tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task Select_ReturnsAllRows()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "SQLite",
            [ParamQuery] = "SELECT id, name, email FROM users ORDER BY id",
            ["query_type"] = "Select",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3, Convert.ToInt32(dict["row_count"]));
        Assert.Equal(3, Convert.ToInt32(dict["column_count"]));
        Assert.Equal("SQLite", dict["provider_name"]?.ToString());
    }

    [Fact]
    public async Task Select_RespectsMaxRows()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = "SELECT * FROM users",
            ["max_rows"] = 2,
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, Convert.ToInt32(dict["row_count"]));
    }

    // ── Execute (non-query) tests ─────────────────────────────────────────

    [Fact]
    public async Task Execute_ReturnsAffectedRows()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = "UPDATE users SET active = 1 WHERE id = 2",
            ["query_type"] = "Execute",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(1, Convert.ToInt32(dict["affected_rows"]));
    }

    // ── Scalar tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task Scalar_ReturnsSingleValue()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = "SELECT COUNT(*) FROM users",
            ["query_type"] = "Scalar",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3L, Convert.ToInt64(dict["scalar_result"]));
    }

    // ── Parameterized query tests ──────────────────────────────��──────────

    [Fact]
    public async Task Select_WithParameters_FiltersCorrectly()
    {
        var request = new ToolCallRequest("relational_database_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = _connectionString,
            ["provider_name"] = "Microsoft.Data.Sqlite",
            [ParamQuery] = "SELECT name FROM users WHERE active = @active",
            ["parameters"] = new Dictionary<string, object?> { ["active"] = 1 },
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, Convert.ToInt32(dict["row_count"]));
    }

    // ── Schema / metadata tests ───────────────────────────────────────────

    [Fact]
    public void Schema_HasCorrectNameAndDescription()
    {
        Assert.Equal("relational_database_query", _tool.Name);
        Assert.Contains("relational databases", _tool.Description);
        Assert.Equal("Data Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters[ParamConnectionString].Required);
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.True(_tool.Schema.Parameters["provider_name"].Required);
        Assert.False(_tool.Schema.Parameters["query_type"].Required);
        Assert.False(_tool.Schema.Parameters["max_rows"].Required);
        Assert.False(_tool.Schema.Parameters["timeout_seconds"].Required);
    }

    [Fact]
    public void ProviderFactory_ReportsCorrectSupportedProviders()
    {
        var factory = new DatabaseProviderFactory();

        Assert.True(factory.IsSupported("Npgsql"));
        Assert.True(factory.IsSupported("Microsoft.Data.SqlClient"));
        Assert.True(factory.IsSupported("MySqlConnector"));
        Assert.True(factory.IsSupported("Microsoft.Data.Sqlite"));
        Assert.True(factory.IsSupported("SQLite"));
        Assert.True(factory.IsSupported("PostgreSQL"));
        Assert.False(factory.IsSupported("Oracle"));
        Assert.False(factory.IsSupported(""));
    }

    [Fact]
    public void ProviderFactory_ThrowsForUnsupportedProvider()
    {
        var factory = new DatabaseProviderFactory();

        Assert.Throws<NotSupportedException>(() => factory.GetProvider("Oracle"));
    }

    [Fact]
    public void ProviderFactory_ThrowsForEmptyProvider()
    {
        var factory = new DatabaseProviderFactory();

        Assert.Throws<ArgumentException>(() => factory.GetProvider(""));
    }

    public void Dispose()
    {
        _tool.Dispose();
        _keepAlive.Dispose();
        GC.SuppressFinalize(this);
    }
}
