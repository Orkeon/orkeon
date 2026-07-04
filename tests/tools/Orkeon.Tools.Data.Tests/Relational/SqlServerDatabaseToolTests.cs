using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Relational;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Relational;

public sealed class SqlServerDatabaseToolTests : IDisposable
{
    private readonly StubDatabaseProviderFactory _providerFactory;
    private readonly StubDatabaseSecurityPolicy _securityPolicy;
    private readonly SqlServerDatabaseTool _tool;

    public SqlServerDatabaseToolTests()
    {
        _providerFactory = new StubDatabaseProviderFactory(
            supported: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Microsoft.Data.SqlClient" },
            providers: ["Microsoft.Data.SqlClient"]);
        _securityPolicy = new StubDatabaseSecurityPolicy();

        _tool = new SqlServerDatabaseTool(_providerFactory, _securityPolicy);
    }

    public void Dispose() => _tool.Dispose();

    [Fact]
    public void ToolContract_HasCorrectName() => Assert.Equal("sqlserver_query", _tool.Name);

    [Fact]
    public void ToolContract_HasCorrectDescription() => Assert.Contains("SQL Server", _tool.Description);

    [Fact]
    public void ToolContract_HasCorrectCategory() => Assert.Equal("Data Operations", _tool.Category);

    [Fact]
    public async Task Validation_InjectsSqlClientProviderWhenNotSet()
    {
        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=.;Database=test;",
            [ParamQuery] = "SELECT 1",
        });

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains("Microsoft.Data.SqlClient", _providerFactory.IsSupportedCalls);
    }

    [Fact]
    public async Task Validation_InjectsSqlClientProviderWhenEmptyString()
    {
        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=.;Database=test;",
            ["provider_name"] = "",
            [ParamQuery] = "SELECT 1",
        });

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains("Microsoft.Data.SqlClient", _providerFactory.IsSupportedCalls);
    }

    [Fact]
    public async Task Validation_PreservesCallerSuppliedProviderName()
    {
        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Host=localhost;",
            ["provider_name"] = "Npgsql",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unsupported provider", result.Error);
        Assert.Contains("Npgsql", _providerFactory.IsSupportedCalls);
    }

    [Fact]
    public async Task Validation_RejectsEmptyConnectionString()
    {
        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
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
        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=.;Database=test;",
            [ParamQuery] = "",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsUnsupportedProvider()
    {
        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Data Source=.;",
            ["provider_name"] = "Oracle",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unsupported provider", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsSecurityPolicyViolation()
    {
        _securityPolicy.Validator = (q, _) =>
            q == "DROP TABLE users"
                ? ValidationResult.Failure(ParamQuery, "DDL statements are not allowed")
                : ValidationResult.Success();

        var request = new ToolCallRequest("sqlserver_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=.;Database=test;",
            [ParamQuery] = "DROP TABLE users",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("DDL", result.Error);
    }
}
