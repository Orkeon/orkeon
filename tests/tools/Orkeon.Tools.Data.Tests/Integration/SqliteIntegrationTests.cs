using Microsoft.Data.Sqlite;
using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for SQLite using in-memory databases (no Docker required).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SqliteIntegrationTests : DatabaseTestFixture, IDisposable
{
    private const string ProviderName = "Microsoft.Data.Sqlite";

    private readonly RelationalDatabaseTool _tool;
    private readonly DatabaseSchemaTool _schemaTool;
    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public SqliteIntegrationTests()
    {
        _connectionString = $"Data Source=IntegrationTest{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        SeedDatabase();

        _tool = CreateRelationalTool();
        _schemaTool = CreateSchemaTool();
    }

    private void SeedDatabase()
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                price REAL NOT NULL,
                category TEXT,
                in_stock INTEGER DEFAULT 1
            );
            CREATE INDEX idx_products_category ON products(category);
            CREATE UNIQUE INDEX idx_products_name ON products(name);
            INSERT INTO products (name, price, category, in_stock) VALUES ('Widget', 9.99, 'Hardware', 1);
            INSERT INTO products (name, price, category, in_stock) VALUES ('Gadget', 24.99, 'Electronics', 1);
            INSERT INTO products (name, price, category, in_stock) VALUES ('Doohickey', 4.50, 'Hardware', 0);

            CREATE TABLE orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                product_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                order_date TEXT NOT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            INSERT INTO orders (product_id, quantity, order_date) VALUES (1, 5, '2026-01-15');
            INSERT INTO orders (product_id, quantity, order_date) VALUES (2, 2, '2026-02-20');
            """;
        cmd.ExecuteNonQuery();
    }

    // ── SELECT tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_AllRows_ReturnsAllProducts()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT id, name, price, category FROM products ORDER BY id");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(3, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_WithWhereClause_ReturnsFilteredRows()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name, price FROM products WHERE category = 'Hardware' ORDER BY name");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(2, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_WithParameterizedQuery_ReturnsCorrectRows()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name FROM products WHERE price > @minPrice",
            parameters: new Dictionary<string, object> { ["@minPrice"] = 5.0 });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(2, Convert.ToInt32(data["row_count"]));
    }

    // ── INSERT tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Insert_IncreasesRowCount()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO products (name, price, category) VALUES ('Thingamajig', 14.99, 'Misc')",
            queryType: "Execute");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["affected_rows"]));

        // Verify the row is present
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) as cnt FROM products WHERE name = 'Thingamajig'",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success, verifyResult.Error);
    }

    // ── UPDATE tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Update_ModifiesExistingRow()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE products SET price = 19.99 WHERE name = 'Widget'",
            queryType: "Execute");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["affected_rows"]));

        // Verify the update
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT price FROM products WHERE name = 'Widget'");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success, verifyResult.Error);
    }

    // ── DELETE tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Delete_RemovesRow()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM products WHERE name = 'Doohickey'",
            queryType: "Execute");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(1, Convert.ToInt32(data["affected_rows"]));

        // Verify deletion
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM products",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success, verifyResult.Error);
    }

    // ── Scalar tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Scalar_Count_ReturnsCorrectValue()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM products",
            queryType: "Scalar");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["scalar_result"]);
        Assert.Equal(3L, Convert.ToInt64(data["scalar_result"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Scalar_Sum_ReturnsAggregateValue()
    {
        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT SUM(price) FROM products",
            queryType: "Scalar");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
    }

    // ── Schema introspection tests ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_Tables_ReturnsBothTables()
    {
        var request = BuildSchemaRequest(_connectionString, ProviderName, "Tables");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_Columns_ReturnsColumnDetails()
    {
        var request = BuildSchemaRequest(_connectionString, ProviderName, "Columns", "products");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_Indexes_ReturnsIndexMetadata()
    {
        var request = BuildSchemaRequest(_connectionString, ProviderName, "Indexes", "products");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["indexes"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_All_ReturnsCompleteMetadata()
    {
        var request = BuildSchemaRequest(_connectionString, ProviderName, "All");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
        Assert.NotNull(data["indexes"]);
        Assert.NotNull(data["foreign_keys"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_ForeignKeys_ReturnsOrdersFK()
    {
        var request = BuildSchemaRequest(_connectionString, ProviderName, "ForeignKeys", "orders");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["foreign_keys"]);
    }

    // ── Full CRUD cycle ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FullCrudCycle_CreateReadUpdateDelete_Succeeds()
    {
        // Create
        var insert = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO products (name, price, category) VALUES ('CRUDItem', 7.77, 'Test')",
            queryType: "Execute");
        var insertResult = await _tool.CallAsync(insert, TestContext.Current.CancellationToken);
        Assert.True(insertResult.Success, insertResult.Error);

        // Read
        var select = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT id, name, price FROM products WHERE name = 'CRUDItem'");
        var selectResult = await _tool.CallAsync(select, TestContext.Current.CancellationToken);
        Assert.True(selectResult.Success, selectResult.Error);
        var selectData = Assert.IsType<Dictionary<string, object>>(selectResult.Result);
        Assert.Equal(1, Convert.ToInt32(selectData["row_count"]));

        // Update
        var update = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE products SET price = 8.88 WHERE name = 'CRUDItem'",
            queryType: "Execute");
        var updateResult = await _tool.CallAsync(update, TestContext.Current.CancellationToken);
        Assert.True(updateResult.Success, updateResult.Error);

        // Delete
        var delete = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM products WHERE name = 'CRUDItem'",
            queryType: "Execute");
        var deleteResult = await _tool.CallAsync(delete, TestContext.Current.CancellationToken);
        Assert.True(deleteResult.Success, deleteResult.Error);

        // Verify gone
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM products WHERE name = 'CRUDItem'",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success, verifyResult.Error);
        var verifyData = Assert.IsType<Dictionary<string, object>>(verifyResult.Result);
        Assert.Equal(0L, Convert.ToInt64(verifyData["scalar_result"]));
    }

    public void Dispose()
    {
        _tool.Dispose();
        _schemaTool.Dispose();
        _keepAlive.Close();
        _keepAlive.Dispose();
    }
}
