using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Tool for executing SQL queries specifically on Microsoft SQL Server databases.
/// Pre-configures the ADO.NET provider to "Microsoft.Data.SqlClient" when not supplied by the caller.
/// </summary>
[ToolContract("sqlserver_query",
    Name = "sqlserver_query",
    Description = "Execute SQL queries on Microsoft SQL Server databases",
    Category = "Data Operations")]
public class SqlServerDatabaseTool : RelationalDatabaseTool
{
    private const string SqlServerProvider = "Microsoft.Data.SqlClient";

    /// <inheritdoc />
    public SqlServerDatabaseTool(
        IDatabaseProviderFactory providerFactory,
        IDatabaseSecurityPolicy securityPolicy,
        ILogger<SqlServerDatabaseTool>? logger = null)
        : base(providerFactory, securityPolicy, logger)
    {
    }

    /// <summary>
    /// Injects the SQL Server provider name before schema-level validation so that
    /// callers do not need to supply <c>provider_name</c> when using this tool.
    /// </summary>
    protected override ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue("provider_name", out var existing) ||
            existing is not string s || string.IsNullOrWhiteSpace(s))
        {
            parameters = new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["provider_name"] = SqlServerProvider
            };
        }

        return base.ValidateParameters(parameters);
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(RelationalDatabaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProviderName))
            request = request with { ProviderName = SqlServerProvider };

        return base.ValidateTypedRequest(request);
    }

    /// <inheritdoc />
    protected override Task<RelationalDatabaseResponse> ExecuteTypedAsync(
        RelationalDatabaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProviderName))
            request = request with { ProviderName = SqlServerProvider };

        return base.ExecuteTypedAsync(request, cancellationToken);
    }
}
