using Orkeon.Tools.Data.Relational;
using Orkeon.Tools.Data.Tests.Integration.Fixtures;
using Testcontainers.MariaDb;

namespace Orkeon.Tools.Data.Tests.Integration;

/// <summary>
/// Integration tests for MariaDB using Testcontainers (MariaDbContainer).
/// MariaDB uses the same MySqlConnector ADO.NET provider as MySQL.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MariaDbIntegrationTests : DatabaseTestFixture, IAsyncLifetime
{
    private const string ProviderName = "MySqlConnector";
    private const string SkipReason = "Docker is not available or container failed to start.";

    private MariaDbContainer? _container;
    private RelationalDatabaseTool _tool = null!;
    private DatabaseSchemaTool _schemaTool = null!;
    private string _connectionString = "";
    private bool _containerStarted;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MariaDbBuilder("mariadb:11")
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
            CREATE TABLE sensors (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                location VARCHAR(200),
                reading DOUBLE NOT NULL,
                recorded_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX idx_sensors_location ON sensors(location);
            INSERT INTO sensors (name, location, reading) VALUES ('TempSensor1', 'Room A', 22.5);
            INSERT INTO sensors (name, location, reading) VALUES ('TempSensor2', 'Room B', 19.3);
            INSERT INTO sensors (name, location, reading) VALUES ('HumiditySensor1', 'Room A', 65.0);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── SELECT tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_AllRows_ReturnsAllSensors()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT id, name, location, reading FROM sensors ORDER BY id");

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.Equal(3, Convert.ToInt32(data["row_count"]));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Select_WithFilter_ReturnsRoomASensors()
    {
        SkipIfContainerUnavailable();

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name FROM sensors WHERE location = 'Room A' ORDER BY name");

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
            "SELECT name, reading FROM sensors WHERE reading > @threshold",
            parameters: new Dictionary<string, object> { ["@threshold"] = 20.0 });

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
            "INSERT INTO sensors (name, location, reading) VALUES ('PressureSensor1', 'Room C', 1013.25)",
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
            "UPDATE sensors SET reading = 23.0 WHERE name = 'TempSensor1'",
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
            "INSERT INTO sensors (name, location, reading) VALUES ('Temp', 'X', 0)",
            queryType: "Execute");
        await _tool.CallAsync(insertReq, TestContext.Current.CancellationToken);

        var request = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM sensors WHERE name = 'Temp'",
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
            "SELECT COUNT(*) FROM sensors",
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

        var request = BuildSchemaRequest(_connectionString, ProviderName, "All", "sensors");

        var result = await _schemaTool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object>>(result.Result);
        Assert.NotNull(data["tables"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Schema_Columns_ReturnsColumnDetails()
    {
        SkipIfContainerUnavailable();

        var request = BuildSchemaRequest(_connectionString, ProviderName, "Columns", "sensors");

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
            "INSERT INTO sensors (name, location, reading) VALUES ('CRUDSensor', 'Lab', 42.0)",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(insert, TestContext.Current.CancellationToken)).Success);

        // Read
        var select = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT name FROM sensors WHERE name = 'CRUDSensor'");
        var selectResult = await _tool.CallAsync(select, TestContext.Current.CancellationToken);
        Assert.True(selectResult.Success);
        var selectData = Assert.IsType<Dictionary<string, object>>(selectResult.Result);
        Assert.Equal(1, Convert.ToInt32(selectData["row_count"]));

        // Update
        var update = BuildRelationalRequest(
            _connectionString, ProviderName,
            "UPDATE sensors SET reading = 43.5 WHERE name = 'CRUDSensor'",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(update, TestContext.Current.CancellationToken)).Success);

        // Delete
        var delete = BuildRelationalRequest(
            _connectionString, ProviderName,
            "DELETE FROM sensors WHERE name = 'CRUDSensor'",
            queryType: "Execute");
        Assert.True((await _tool.CallAsync(delete, TestContext.Current.CancellationToken)).Success);

        // Verify
        var verify = BuildRelationalRequest(
            _connectionString, ProviderName,
            "SELECT COUNT(*) FROM sensors WHERE name = 'CRUDSensor'",
            queryType: "Scalar");
        var verifyResult = await _tool.CallAsync(verify, TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success);
        var verifyData = Assert.IsType<Dictionary<string, object>>(verifyResult.Result);
        Assert.Equal(0L, Convert.ToInt64(verifyData["scalar_result"]));
    }
}
