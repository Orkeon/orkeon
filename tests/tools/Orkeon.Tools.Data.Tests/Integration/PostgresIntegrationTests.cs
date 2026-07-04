using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;
using Testcontainers.PostgreSql;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for PostgreSQL using Testcontainers (PostgreSqlContainer).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresIntegrationTests : DatabaseTestFixture, IAsyncLifetime
{
    private const string ProviderName = "Npgsql";
    private const string SkipReason = "Docker is not available or container failed to start.";

    private PostgreSqlContainer? _container;
    private RelationalDatabaseTool _tool = null!;
    private DatabaseSchemaTool _schemaTool = null!;
    private string _connectionString = "";
    private bool _containerStarted;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
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
            CREATE TABLE customers (
                id SERIAL PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                email VARCHAR(200) UNIQUE,
                country VARCHAR(50),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX idx_customers_country ON customers(country);
            INSERT INTO customers (name, email, country) VALUES ('Alice', 'alice@example.com', 'US');
            INSERT INTO customers (name, email, country) VALUES ('Bob', 'bob@example.com', 'UK');
            INSERT INTO customers (name, email, country) VALUES ('Charlie', 'charlie@example.com', 'US');
            INSERT INTO customers (name, email, country) VALUES ('Diana', 'diana@example.com', 'DE');
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── SELECT tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_AllRows_ReturnsAllCustomers()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT id, name, email, country FROM customers ORDER BY id");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(4, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_WithFilter_ReturnsUSCustomers()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name FROM customers WHERE country = 'US' ORDER BY name");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(2, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_ParameterizedQuery_ReturnsMatchingRows()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name, email FROM customers WHERE country = @country",
            parameters: new Dictionary<string, object> { ["@country"] = "UK" });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["row_count"]));
    }

    // ── INSERT / UPDATE / DELETE ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Insert_AddsRow()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO customers (name, email, country) VALUES ('Eve', 'eve@example.com', 'FR')",
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
            "UPDATE customers SET country = 'GB' WHERE name = 'Bob'",
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
            "INSERT INTO customers (name, email, country) VALUES ('Temp', 'temp@test.com', 'XX')",
            queryType: "Execute");
        await _tool.CallAsync(insertReq, TestContext.Current.CancellationToken);

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM customers WHERE name = 'Temp'",
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
            "SELECT COUNT(*) FROM customers",
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

        var request = BuildSchemaRequest(_connectionString, ProviderName, "All", "customers");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_Indexes_ReturnsIndexInfo()
    {
        SkipIfContainerUnavailable();

        var request = BuildSchemaRequest(_connectionString, ProviderName, "Indexes", "customers");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["indexes"]);
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
            "INSERT INTO customers (name, email, country) VALUES ('CRUDTest', 'crud@test.com', 'JP')",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(insert, TestContext.Current.CancellationToken)).Success);

        // Read
        var select = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name FROM customers WHERE name = 'CRUDTest'");
        var selectResult = await _tool.CallAsync(select, TestContext.Current.CancellationToken);
        Assert.True(selectResult.Success);
        var selectData = Assert.IsType<Dictionary<string, object>>(selectResult.Result);
        Assert.Equal(1, Convert.ToInt32(selectData["row_count"]));

        // Update
        var update = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE customers SET country = 'CN' WHERE name = 'CRUDTest'",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(update, TestContext.Current.CancellationToken)).Success);

        // Delete
        var delete = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM customers WHERE name = 'CRUDTest'",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(delete, TestContext.Current.CancellationToken)).Success);

        // Verify gone
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM customers WHERE name = 'CRUDTest'",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success);
        var verifyData = Assert.IsType<Dictionary<string, object>>(verifyResult.Result);
        Assert.Equal(0L, Convert.ToInt64(verifyData["scalar_result"]));
    }
}
