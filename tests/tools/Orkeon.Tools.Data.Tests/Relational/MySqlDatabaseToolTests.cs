using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Relational;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Relational;

public sealed class MySqlDatabaseToolTests : IDisposable
{
    private readonly MySqlDatabaseTool _tool;

    public MySqlDatabaseToolTests()
    {
        _tool = new MySqlDatabaseTool(
            new DatabaseProviderFactory(),
            new DefaultDatabaseSecurityPolicy());
    }

    public void Dispose() => _tool.Dispose();

    // ── Tool contract ─────────────────────────────────────────────────────

    [Fact]
    public void ToolContract_HasCorrectName()
    {
        Assert.Equal("mysql_query", _tool.Name);
    }

    [Fact]
    public void ToolContract_HasCorrectDescription()
    {
        Assert.Contains("MySQL", _tool.Description);
    }

    [Fact]
    public void ToolContract_HasCorrectCategory()
    {
        Assert.Equal("Data Operations", _tool.Category);
    }

    // ── Provider injection ────────────────────────────────────────────────

    [Fact]
    public async Task Validation_InjectsMySqlConnectorWhenProviderNameIsOmitted()
    {
        // Without provider injection, this would fail with "Provider name cannot be empty".
        // With injection, it should fail at a later stage (bad connection / DDL / etc.)
        // Here we use a DDL query so it fails on the security policy, not the provider check.
        var request = new ToolCallRequest("mysql_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=localhost;Database=test;User=root;Password=pass",
            [ParamQuery] = "DROP TABLE users",
            // provider_name intentionally omitted
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        // Should fail on DDL security, not on missing provider name
        Assert.DoesNotContain("Provider name", result.Error);
        Assert.Contains("DDL", result.Error);
    }

    [Fact]
    public async Task Validation_PreservesExplicitProviderName()
    {
        // When a valid provider is given explicitly, validation should accept it
        var request = new ToolCallRequest("mysql_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=localhost;Database=test;User=root;Password=pass",
            ["provider_name"] = "MySqlConnector",
            [ParamQuery] = "DROP TABLE users",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.DoesNotContain("Unsupported provider", result.Error);
        Assert.DoesNotContain("Provider name", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsUnsupportedExplicitProvider()
    {
        var request = new ToolCallRequest("mysql_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=localhost",
            ["provider_name"] = "Oracle.ManagedDataAccess",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unsupported provider", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsEmptyConnectionString()
    {
        var request = new ToolCallRequest("mysql_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Connection string", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsEmptyQuery()
    {
        var request = new ToolCallRequest("mysql_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=localhost;Database=test;User=root;Password=pass",
            [ParamQuery] = "",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error);
    }

    [Fact]
    public async Task Security_BlocksDdlStatements()
    {
        var request = new ToolCallRequest("mysql_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=localhost;Database=test;User=root;Password=pass",
            [ParamQuery] = "DROP TABLE users",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("DDL", result.Error);
    }

    // ── Schema ────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_HasExpectedParameters()
    {
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamConnectionString));
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamQuery));
        Assert.True(_tool.Schema.Parameters.ContainsKey("provider_name"));
    }
}
