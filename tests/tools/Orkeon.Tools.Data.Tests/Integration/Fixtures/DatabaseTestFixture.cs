using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Data.Relational;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Data.Tests.Integration.Fixtures;

/// <summary>
/// Abstract base fixture for database integration tests.
/// Provides helper methods for creating tool instances and building requests.
/// Derived classes manage container lifecycle via IAsyncLifetime.
/// </summary>
public abstract class DatabaseTestFixture
{
    protected IDatabaseProviderFactory ProviderFactory { get; } = new DatabaseProviderFactory();
    protected IDatabaseSecurityPolicy SecurityPolicy { get; } = new DefaultDatabaseSecurityPolicy();

    /// <summary>
    /// Creates a <see cref="RelationalDatabaseTool"/> configured with real dependencies.
    /// </summary>
    protected RelationalDatabaseTool CreateRelationalTool()
        => new(ProviderFactory, SecurityPolicy);

    /// <summary>
    /// Creates a <see cref="DatabaseSchemaTool"/> configured with real dependencies.
    /// </summary>
    protected DatabaseSchemaTool CreateSchemaTool()
        => new(ProviderFactory);

    /// <summary>
    /// Builds a <see cref="ToolCallRequest"/> for the relational database tool.
    /// </summary>
    protected static ToolCallRequest BuildRelationalRequest(
        string connectionString,
        string providerName,
        string query,
        string queryType = "Select",
        Dictionary<string, object>? parameters = null)
    {
        var dict = new Dictionary<string, object?>
        {
            [ParamConnectionString] = connectionString,
            ["provider_name"] = providerName,
            [ParamQuery] = query,
            ["query_type"] = queryType,
        };

        if (parameters is not null)
            dict["parameters"] = parameters;

        return new ToolCallRequest("relational_database_query", dict);
    }

    /// <summary>
    /// Builds a <see cref="ToolCallRequest"/> for the database schema tool.
    /// </summary>
    protected static ToolCallRequest BuildSchemaRequest(
        string connectionString,
        string providerName,
        string schemaScope = "All",
        string? tableFilter = null)
    {
        var dict = new Dictionary<string, object?>
        {
            [ParamConnectionString] = connectionString,
            ["provider_name"] = providerName,
            ["schema_scope"] = schemaScope,
        };

        if (tableFilter is not null)
            dict["table_filter"] = tableFilter;

        return new ToolCallRequest("database_schema", dict);
    }

    /// <summary>
    /// Checks whether Docker is available by attempting to detect the environment.
    /// Returns true if likely available, false otherwise.
    /// </summary>
    protected static bool IsDockerAvailable()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit(TimeSpan.FromSeconds(5));
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
