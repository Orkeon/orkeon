using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Tool for executing SQL queries on PostgreSQL databases.
/// Pre-configures the ADO.NET provider to Npgsql when not explicitly set.
/// </summary>
[ToolContract("postgres_query",
    Name = "postgres_query",
    Description = "Execute SQL queries on PostgreSQL databases",
    Category = "Data Operations")]
public class PostgresDatabaseTool : RelationalDatabaseTool
{
    /// <summary>
    /// Initializes a new instance of <see cref="PostgresDatabaseTool"/>.
    /// </summary>
    public PostgresDatabaseTool(
        IDatabaseProviderFactory providerFactory,
        IDatabaseSecurityPolicy securityPolicy,
        ILogger<PostgresDatabaseTool>? logger = null) : base(providerFactory, securityPolicy, logger)
    {
    }

    /// <inheritdoc />
    /// Injects <c>Npgsql</c> as the provider name before schema and typed validation.
    protected override ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.TryGetValue("provider_name", out var providerName) ||
            providerName is string s && string.IsNullOrWhiteSpace(s))
        {
            parameters["provider_name"] = "Npgsql";
        }

        return base.ValidateParameters(parameters);
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

    private static RelationalDatabaseRequest InjectProvider(RelationalDatabaseRequest request)
        => string.IsNullOrWhiteSpace(request.ProviderName)
            ? request with { ProviderName = "Npgsql" }
            : request;
}
