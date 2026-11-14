using System.Diagnostics;
using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Constants.Database;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Tool for executing SQL queries on relational databases via ADO.NET.
/// Supports SQL Server, PostgreSQL, MySQL/MariaDB, and SQLite.
/// </summary>
[ToolContract("relational_database_query",
    Name = "relational_database_query",
    Description = "Execute SQL queries on relational databases (SQL Server, PostgreSQL, MySQL, MariaDB, SQLite)",
    Category = "Data Operations")]
public partial class RelationalDatabaseTool : ToolBase<RelationalDatabaseRequest, RelationalDatabaseResponse>
{
    private readonly IDatabaseProviderFactory _providerFactory;
    private readonly IDatabaseSecurityPolicy _securityPolicy;

    /// <summary>
    /// Initializes a new instance of <see cref="RelationalDatabaseTool"/>.
    /// </summary>
    public RelationalDatabaseTool(
        IDatabaseProviderFactory providerFactory,
        IDatabaseSecurityPolicy securityPolicy,
        ILogger<RelationalDatabaseTool>? logger = null) : base(logger)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _securityPolicy = securityPolicy ?? throw new ArgumentNullException(nameof(securityPolicy));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(RelationalDatabaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return "Connection string cannot be empty";

        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        if (string.IsNullOrWhiteSpace(request.ProviderName))
            return "Provider name cannot be empty";

        if (!_providerFactory.IsSupported(request.ProviderName))
            return $"Unsupported provider '{request.ProviderName}'. Supported: {string.Join(", ", _providerFactory.SupportedProviders)}";

        // A small model fills every optional field and sends 0: refused, so a SELECT never
        // reads as an empty result (max_rows) and no query waits without bound (timeout_seconds).
        // Execute and Scalar never read max_rows, so its value is left alone there.
        if (request.QueryType == RelationalQueryType.Select && request.MaxRows < 1)
            return $"max_rows must be at least 1 (got {request.MaxRows}); omit it for the default of {DatabaseDefaults.DefaultMaxRows}";

        if (request.TimeoutSeconds < 1)
            return $"timeout_seconds must be at least 1 (got {request.TimeoutSeconds}); omit it for the default of {DatabaseDefaults.DefaultTimeoutSeconds}";

        var queryOptions = new DatabaseQueryOptions
        {
            MaxRows = request.MaxRows,
            TimeoutSeconds = request.TimeoutSeconds,
        };

        var validationResult = _securityPolicy.ValidateQuery(request.Query, queryOptions);
        if (!validationResult.IsValid)
            return string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));

        return null;
    }

    /// <inheritdoc />
    protected override Task<RelationalDatabaseResponse> ExecuteTypedAsync(
        RelationalDatabaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<RelationalDatabaseResponse> ExecuteTypedCoreAsync()
        {
            var factory = _providerFactory.GetProvider(request.ProviderName);
            var sw = Stopwatch.StartNew();

            var connection = factory.CreateConnection()
                ?? throw new InvalidOperationException($"Provider '{request.ProviderName}' returned a null connection.");
            await using var __connection = connection.ConfigureAwait(false);
            connection.ConnectionString = request.ConnectionString;
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var response = request.QueryType switch
            {
                RelationalQueryType.Select => await ExecuteSelectAsync(connection, request, cancellationToken).ConfigureAwait(false),
                RelationalQueryType.Execute => await ExecuteNonQueryAsync(connection, request, cancellationToken).ConfigureAwait(false),
                RelationalQueryType.Scalar => await ExecuteScalarAsync(connection, request, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown query type: {request.QueryType}")
            };

            sw.Stop();
            return response with { ExecutionTimeMs = sw.ElapsedMilliseconds, ProviderName = request.ProviderName };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Executes caller-supplied SQL as the tool's documented core capability; values are parameterizable via request.Parameters (AddParameters) and the query is gated by IDatabaseSecurityPolicy.ValidateQuery in ValidateTypedRequest.")]
    private async Task<RelationalDatabaseResponse> ExecuteSelectAsync(
        System.Data.Common.DbConnection connection,
        RelationalDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var __command = command.ConfigureAwait(false);
        command.CommandText = request.Query;
        command.CommandTimeout = request.TimeoutSeconds;
        AddParameters(command, request.Parameters);

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        var rows = new List<Dictionary<string, object>>();
        while (rows.Count < request.MaxRows && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[columns[i]] = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false)
                    ? "NULL"
                    : reader.GetValue(i);
            rows.Add(row);
        }

        // The limit was reached: one more read tells whether the result set held rows left out.
        var truncated = rows.Count == request.MaxRows
            && await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        LogSelectQueryCompleted(Logger, rows.Count, columns.Count);

        return new RelationalDatabaseResponse
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ColumnCount = columns.Count,
            Truncated = truncated,
            QueryType = RelationalQueryType.Select,
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Executes caller-supplied SQL as the tool's documented core capability; values are parameterizable via request.Parameters (AddParameters) and the query is gated by IDatabaseSecurityPolicy.ValidateQuery in ValidateTypedRequest.")]
    private async Task<RelationalDatabaseResponse> ExecuteNonQueryAsync(
        System.Data.Common.DbConnection connection,
        RelationalDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var __command = command.ConfigureAwait(false);
        command.CommandText = request.Query;
        command.CommandTimeout = request.TimeoutSeconds;
        AddParameters(command, request.Parameters);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogNonQueryCompleted(Logger, affected);

        return new RelationalDatabaseResponse
        {
            AffectedRows = affected,
            QueryType = RelationalQueryType.Execute,
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Executes caller-supplied SQL as the tool's documented core capability; values are parameterizable via request.Parameters (AddParameters) and the query is gated by IDatabaseSecurityPolicy.ValidateQuery in ValidateTypedRequest.")]
    private async Task<RelationalDatabaseResponse> ExecuteScalarAsync(
        System.Data.Common.DbConnection connection,
        RelationalDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var __command = command.ConfigureAwait(false);
        command.CommandText = request.Query;
        command.CommandTimeout = request.TimeoutSeconds;
        AddParameters(command, request.Parameters);

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        LogScalarQueryCompleted(Logger, scalar ?? "NULL");

        return new RelationalDatabaseResponse
        {
            ScalarResult = scalar is DBNull ? null : scalar,
            QueryType = RelationalQueryType.Scalar,
        };
    }

    private static void AddParameters(System.Data.Common.DbCommand command, IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null) return;

        foreach (var (name, value) in parameters)
        {
            var param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            command.Parameters.Add(param);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SELECT returned {RowCount} rows with {ColCount} columns")]
    private static partial void LogSelectQueryCompleted(ILogger logger, int rowCount, int colCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Non-query affected {RowCount} rows")]
    private static partial void LogNonQueryCompleted(ILogger logger, int rowCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scalar query returned: {Result}")]
    private static partial void LogScalarQueryCompleted(ILogger logger, object result);
}
