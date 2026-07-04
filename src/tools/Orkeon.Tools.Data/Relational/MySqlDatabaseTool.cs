using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Tool for executing SQL queries on MySQL databases.
/// Pre-configures the ADO.NET provider to MySqlConnector.
/// </summary>
[ToolContract("mysql_query",
    Name = "mysql_query",
    Description = "Execute SQL queries on MySQL databases",
    Category = "Data Operations")]
public class MySqlDatabaseTool : RelationalDatabaseTool
{
    private const string MysqlProvider = "MySqlConnector";

    /// <inheritdoc />
    public MySqlDatabaseTool(
        IDatabaseProviderFactory providerFactory,
        IDatabaseSecurityPolicy securityPolicy,
        ILogger<MySqlDatabaseTool>? logger = null) : base(providerFactory, securityPolicy, logger)
    {
    }

    /// <inheritdoc />
    protected override ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return base.ValidateParameters(InjectProvider(parameters));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(RelationalDatabaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.ValidateTypedRequest(InjectProvider(request));
    }

    /// <inheritdoc />
    protected override Task<RelationalDatabaseResponse> ExecuteTypedAsync(
        RelationalDatabaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.ExecuteTypedAsync(InjectProvider(request), cancellationToken);
    }

    private static Dictionary<string, object?> InjectProvider(Dictionary<string, object?> parameters)
    {
        if (parameters.TryGetValue("provider_name", out var val) &&
            val is string s && !string.IsNullOrWhiteSpace(s))
            return parameters;

        var merged = new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["provider_name"] = MysqlProvider
        };
        return merged;
    }

    private static RelationalDatabaseRequest InjectProvider(RelationalDatabaseRequest request)
        => string.IsNullOrWhiteSpace(request.ProviderName)
            ? request with { ProviderName = MysqlProvider }
            : request;
}
