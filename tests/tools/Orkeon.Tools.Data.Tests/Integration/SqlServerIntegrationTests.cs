using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;
using Testcontainers.MsSql;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for SQL Server using Testcontainers (MsSqlContainer).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SqlServerIntegrationTests : DatabaseTestFixture, IAsyncLifetime
{
    private const string ProviderName = "Microsoft.Data.SqlClient";
    private const string SkipReason = "Docker is not available or container failed to start.";

    private MsSqlContainer? _container;
    private RelationalDatabaseTool _tool = null!;
    private DatabaseSchemaTool _schemaTool = null!;
    private string _connectionString = "";
    private bool _containerStarted;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
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
            CREATE TABLE employees (
                id INT IDENTITY(1,1) PRIMARY KEY,
                name NVARCHAR(100) NOT NULL,
                department NVARCHAR(50),
                salary DECIMAL(10,2) NOT NULL,
                active BIT DEFAULT 1
            );
            CREATE INDEX idx_employees_dept ON employees(department);
            INSERT INTO employees (name, department, salary) VALUES ('Alice', 'Engineering', 95000.00);
            INSERT INTO employees (name, department, salary) VALUES ('Bob', 'Marketing', 72000.00);
            INSERT INTO employees (name, department, salary, active) VALUES ('Charlie', 'Engineering', 110000.00, 0);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── SELECT tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_AllRows_ReturnsAllEmployees()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT id, name, department, salary FROM employees ORDER BY id");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(3, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_WithFilter_ReturnsMatchingRows()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name FROM employees WHERE department = 'Engineering'");

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
            "SELECT name, salary FROM employees WHERE salary > @minSalary",
            parameters: new Dictionary<string, object> { ["@minSalary"] = 80000m });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(2, Convert.ToInt32(data["row_count"]));
    }

    // ── INSERT/UPDATE/DELETE tests ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Execute_Insert_AddsNewRow()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "INSERT INTO employees (name, department, salary) VALUES ('Diana', 'Sales', 65000.00)",
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
            "UPDATE employees SET salary = 76000.00 WHERE name = 'Bob'",
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
            "INSERT INTO employees (name, department, salary) VALUES ('Temp', 'Temp', 10000)",
            queryType: "Execute");
        await _tool.CallAsync(insertReq, TestContext.Current.CancellationToken);

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM employees WHERE name = 'Temp'",
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
            "SELECT COUNT(*) FROM employees",
            queryType: "Scalar");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["scalar_result"]);
    }

    // ── Schema introspection ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_All_ReturnsTablesColumnsIndexes()
    {
        SkipIfContainerUnavailable();

        var request = BuildSchemaRequest(_connectionString, ProviderName, "All", "employees");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_Tables_ReturnsEmployeesTable()
    {
        SkipIfContainerUnavailable();

        var request = BuildSchemaRequest(_connectionString, ProviderName, "Tables");

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
            "INSERT INTO employees (name, department, salary) VALUES ('CRUDTest', 'QA', 55000)",
            queryType: "Execute");
        var insertResult = await _tool.CallAsync(insert, TestContext.Current.CancellationToken);
        Assert.True(insertResult.Success, insertResult.Error);

        // Read
        var select = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name, salary FROM employees WHERE name = 'CRUDTest'");
        var selectResult = await _tool.CallAsync(select, TestContext.Current.CancellationToken);
        Assert.True(selectResult.Success, selectResult.Error);
        var selectData = Assert.IsType<Dictionary<string, object>>(selectResult.Result);
        Assert.Equal(1, Convert.ToInt32(selectData["row_count"]));

        // Update
        var update = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE employees SET salary = 60000 WHERE name = 'CRUDTest'",
            queryType: "Execute");
        var updateResult = await _tool.CallAsync(update, TestContext.Current.CancellationToken);
        Assert.True(updateResult.Success, updateResult.Error);

        // Delete
        var delete = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM employees WHERE name = 'CRUDTest'",
            queryType: "Execute");
        var deleteResult = await _tool.CallAsync(delete, TestContext.Current.CancellationToken);
        Assert.True(deleteResult.Success, deleteResult.Error);

        // Verify
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM employees WHERE name = 'CRUDTest'",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success, verifyResult.Error);
    }
}
