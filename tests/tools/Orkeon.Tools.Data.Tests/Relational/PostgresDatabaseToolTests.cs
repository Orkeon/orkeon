using System.Data.Common;
using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Relational;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Relational;

public sealed class PostgresDatabaseToolTests : IDisposable
{
    private readonly StubDatabaseProviderFactory _providerFactory;
    private readonly StubDatabaseSecurityPolicy _securityPolicy;
    private readonly PostgresDatabaseTool _tool;

    public PostgresDatabaseToolTests()
    {
        _providerFactory = new StubDatabaseProviderFactory(
            supported: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Npgsql" },
            providers: ["Npgsql", "Microsoft.Data.SqlClient"]);
        _securityPolicy = new StubDatabaseSecurityPolicy();

        _tool = new PostgresDatabaseTool(_providerFactory, _securityPolicy);
    }

    public void Dispose() => _tool.Dispose();

    [Fact]
    public void ToolContract_HasCorrectName()
        => Assert.Equal("postgres_query", _tool.Name);

    [Fact]
    public void ToolContract_HasCorrectDescription()
        => Assert.Contains("PostgreSQL", _tool.Description);

    [Fact]
    public void ToolContract_HasCorrectCategory()
        => Assert.Equal("Data Operations", _tool.Category);

    [Fact]
    public async Task ProviderName_IsAutoInjected_WhenNotProvided()
    {
        var request = new ToolCallRequest("postgres_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Host=localhost;Database=mydb",
            [ParamQuery] = "SELECT 1",
        });

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains("Npgsql", _providerFactory.IsSupportedCalls);
    }

    [Fact]
    public async Task ProviderName_IsAutoInjected_WhenEmpty()
    {
        var request = new ToolCallRequest("postgres_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Host=localhost;Database=mydb",
            ["provider_name"] = "",
            [ParamQuery] = "SELECT 1",
        });

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains("Npgsql", _providerFactory.IsSupportedCalls);
    }

    [Fact]
    public async Task ProviderName_IsPreserved_WhenExplicitlySet()
    {
        _providerFactory.Allow("Microsoft.Data.SqlClient");

        var request = new ToolCallRequest("postgres_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Server=localhost;Database=mydb",
            ["provider_name"] = "Microsoft.Data.SqlClient",
            [ParamQuery] = "SELECT 1",
        });

        await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Npgsql should NOT be checked — the explicit provider is used.
        Assert.Contains("Microsoft.Data.SqlClient", _providerFactory.IsSupportedCalls);
        Assert.DoesNotContain("Npgsql", _providerFactory.IsSupportedCalls);
    }

    [Fact]
    public async Task Validation_RejectsEmptyConnectionString()
    {
        var request = new ToolCallRequest("postgres_query", new Dictionary<string, object?>
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
        var request = new ToolCallRequest("postgres_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Host=localhost;Database=mydb",
            [ParamQuery] = "",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Query", result.Error);
    }

    [Fact]
    public async Task Validation_RejectsUnsupportedProvider()
    {
        var request = new ToolCallRequest("postgres_query", new Dictionary<string, object?>
        {
            [ParamConnectionString] = "Data Source=localhost",
            ["provider_name"] = "Oracle",
            [ParamQuery] = "SELECT 1",
        });

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Unsupported provider", result.Error);
    }
}

internal sealed class StubDatabaseProviderFactory : IDatabaseProviderFactory
{
    private readonly HashSet<string> _supported;
    public IReadOnlyList<string> SupportedProviders { get; }
    public List<string> IsSupportedCalls { get; } = new();

    public StubDatabaseProviderFactory(HashSet<string> supported, IReadOnlyList<string> providers)
    {
        _supported = supported;
        SupportedProviders = providers;
    }

    public void Allow(string name) => _supported.Add(name);

    public DbProviderFactory GetProvider(string providerName)
        => throw new NotSupportedException($"Test stub does not provide a real {providerName} factory.");

    public bool IsSupported(string providerName)
    {
        IsSupportedCalls.Add(providerName);
        return _supported.Contains(providerName);
    }
}

internal sealed class StubDatabaseSecurityPolicy : IDatabaseSecurityPolicy
{
    public Func<string, DatabaseQueryOptions, ValidationResult> Validator { get; set; } = (_, _) => ValidationResult.Success();
    public Func<string, bool> IsDestructive { get; set; } = _ => false;

    public ValidationResult ValidateQuery(string query, DatabaseQueryOptions options) => Validator(query, options);
    public bool IsDestructiveOperation(string query) => IsDestructive(query);
}
