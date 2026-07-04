using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Tool for executing SQL queries on MariaDB databases.
/// Pre-configures the ADO.NET provider to MySqlConnector (compatible with MariaDB).
/// </summary>
[ToolContract("mariadb_query",
    Name = "mariadb_query",
    Description = "Execute SQL queries on MariaDB databases",
    Category = "Data Operations")]
public class MariaDbDatabaseTool : RelationalDatabaseTool
{
    private const string MariaDbProvider = "MySqlConnector";

    /// <inheritdoc />
    public MariaDbDatabaseTool(
        IDatabaseProviderFactory providerFactory,
        IDatabaseSecurityPolicy securityPolicy,
        ILogger<MariaDbDatabaseTool>? logger = null) : base(providerFactory, securityPolicy, logger)
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
            ["provider_name"] = MariaDbProvider
        };
        return merged;
    }

    private static RelationalDatabaseRequest InjectProvider(RelationalDatabaseRequest request)
        => string.IsNullOrWhiteSpace(request.ProviderName)
            ? request with { ProviderName = MariaDbProvider }
            : request;
}
