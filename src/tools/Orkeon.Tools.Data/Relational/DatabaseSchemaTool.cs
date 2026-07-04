using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Orkeon.Tools.Abstractions.Data;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Data.Relational.Models;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Tool for introspecting database schema: tables, columns, indexes, and foreign keys.
/// Works with any ADO.NET relational database supported by <see cref="IDatabaseProviderFactory"/>.
/// </summary>
[ToolContract("database_schema",
    Name = "database_schema",
    Description = "Inspect database schema (tables, columns, indexes, foreign keys)",
    Category = "Data Operations")]
public partial class DatabaseSchemaTool : ToolBase<DatabaseSchemaRequest, DatabaseSchemaResponse>
{
    private readonly IDatabaseProviderFactory _providerFactory;

    /// <summary>Initializes a new instance of <see cref="DatabaseSchemaTool"/>.</summary>
    /// <param name="providerFactory">Factory for creating database providers.</param>
    /// <param name="logger">Optional logger.</param>
    public DatabaseSchemaTool(
        IDatabaseProviderFactory providerFactory,
        ILogger<DatabaseSchemaTool>? logger = null) : base(logger)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DatabaseSchemaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            return "Connection string cannot be empty";

        if (string.IsNullOrWhiteSpace(request.ProviderName))
            return "Provider name cannot be empty";

        if (!_providerFactory.IsSupported(request.ProviderName))
            return $"Unsupported provider '{request.ProviderName}'. Supported: {string.Join(", ", _providerFactory.SupportedProviders)}";

        // Reject table filter values that contain SQL injection patterns.
        // TableFilter is a wildcard glob, not a SQL identifier, but semicolons/quotes/comments
        // indicate an injection attempt that should be rejected at the boundary.
        if (!string.IsNullOrEmpty(request.TableFilter) && SqlInjectionPatternRegex().IsMatch(request.TableFilter))
        {
            LogSuspiciousTableFilter(request.TableFilter);
            return $"Table filter '{request.TableFilter}' contains invalid characters. Only alphanumeric characters, underscores, and wildcards (*) are allowed.";
        }

        return null;
    }

    /// <inheritdoc />
    protected override Task<DatabaseSchemaResponse> ExecuteTypedAsync(
        DatabaseSchemaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<DatabaseSchemaResponse> ExecuteTypedCoreAsync()
        {
            var factory = _providerFactory.GetProvider(request.ProviderName);

            var connection = factory.CreateConnection()
                ?? throw new InvalidOperationException($"Provider '{request.ProviderName}' returned a null connection.");
            await using var __connection = connection.ConfigureAwait(false);
            connection.ConnectionString = request.ConnectionString;
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var filter = BuildWildcardRegex(request.TableFilter);
            var scope = request.SchemaScope;
            var isSqlite = IsSqliteConnection(connection);

            var tables = isSqlite
                ? await GetTablesSqliteAsync(connection, filter, scope, cancellationToken).ConfigureAwait(false)
                : await GetTablesGenericAsync(connection, filter, scope).ConfigureAwait(false);

            List<IndexInfo>? indexes = null;
            if (scope is SchemaScope.Indexes or SchemaScope.All)
                indexes = isSqlite
                    ? await GetIndexesSqliteAsync(connection, filter, cancellationToken).ConfigureAwait(false)
                    : await GetIndexesGenericAsync(connection, filter).ConfigureAwait(false);

            List<ForeignKeyInfo>? foreignKeys = null;
            if (scope is SchemaScope.ForeignKeys or SchemaScope.All)
                foreignKeys = isSqlite
                    ? await GetForeignKeysSqliteAsync(connection, filter, cancellationToken).ConfigureAwait(false)
                    : await GetForeignKeysGenericAsync(connection, filter).ConfigureAwait(false);

            var dbName = connection.Database;
            if (string.IsNullOrEmpty(dbName))
                dbName = ExtractDatabaseNameFromConnectionString(request.ConnectionString);

            LogSchemaIntrospectionCompleted(tables.Count, indexes?.Count ?? 0, foreignKeys?.Count ?? 0);

            return new DatabaseSchemaResponse
            {
                Tables = tables,
                Indexes = indexes,
                ForeignKeys = foreignKeys,
                DatabaseName = dbName,
                ProviderName = request.ProviderName,
            };
        }
    }

    // ── SQLite-specific introspection ────────────────────────────────────

    private static bool IsSqliteConnection(DbConnection connection)
        => connection.GetType().FullName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<List<TableInfo>> GetTablesSqliteAsync(
        DbConnection connection, Regex? filter, SchemaScope scope, CancellationToken ct)
    {
        var tables = new List<TableInfo>();

        var cmd = connection.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);

        var tableNames = new List<string>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (filter is null || filter.IsMatch(name))
                tableNames.Add(name);
        }

        foreach (var tableName in tableNames)
        {
            // Skip tables with non-allowlist names (e.g. attacker-controlled DB with malicious names).
            if (!IsSafeIdentifier(tableName))
                continue;

            var columns = new List<ColumnInfo>();
            if (scope is SchemaScope.Tables or SchemaScope.Columns or SchemaScope.All)
                columns = await GetColumnsSqliteAsync(connection, tableName, ct).ConfigureAwait(false);

            tables.Add(new TableInfo
            {
                Name = tableName,
                Columns = columns,
            });
        }

        return tables;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "SQLite PRAGMA does not support parameter binding; tableName is a table identifier validated by IsSafeIdentifier (^[a-zA-Z0-9_]+$) at the top of this method and double-quoted via QuoteSqliteIdentifier before interpolation. Identifiers cannot be parameterized.")]
    private static async Task<List<ColumnInfo>> GetColumnsSqliteAsync(
        DbConnection connection, string tableName, CancellationToken ct)
    {
        var columns = new List<ColumnInfo>();

        if (!IsSafeIdentifier(tableName))
            return columns;

        var cmd = connection.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
        // Rationale: PRAGMA does not support parameter binding; identifier is allowlist-validated
        // (^[a-zA-Z0-9_]+$) and double-quoted before interpolation — injection is not possible.
        cmd.CommandText = $"PRAGMA table_info({QuoteSqliteIdentifier(tableName)})";
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            columns.Add(new ColumnInfo
            {
                Name = reader.GetString(1),          // name
                DataType = reader.GetString(2),       // type
                IsNullable = reader.GetInt32(3) == 0, // notnull (0 = nullable)
                IsPrimaryKey = reader.GetInt32(5) != 0, // pk
                DefaultValue = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : reader.GetString(4),
            });
        }

        return columns;
    }

    private static async Task<List<IndexInfo>> GetIndexesSqliteAsync(
        DbConnection connection, Regex? filter, CancellationToken ct)
    {
        var indexes = new List<IndexInfo>();

        // Get all user-created indexes from sqlite_master
        var cmd = connection.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        cmd.CommandText = """
            SELECT name, tbl_name
            FROM sqlite_master
            WHERE type='index' AND name NOT LIKE 'sqlite_%'
            ORDER BY tbl_name, name
            """;
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);

        var indexEntries = new List<(string IndexName, string TableName)>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var tableName = reader.GetString(1);
            if (filter is null || filter.IsMatch(tableName))
                indexEntries.Add((reader.GetString(0), tableName));
        }

        foreach (var (indexName, tableName) in indexEntries)
        {
            // Skip indexes whose names contain non-allowlist characters (attacker-controlled DB defense).
            if (!IsSafeIdentifier(indexName))
                continue;

            var (columns, isUnique) = await GetIndexDetailSqliteAsync(connection, indexName, ct).ConfigureAwait(false);
            indexes.Add(new IndexInfo
            {
                Name = indexName,
                TableName = tableName,
                Columns = columns,
                IsUnique = isUnique,
            });
        }

        return indexes;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "SQLite PRAGMA does not support parameter binding; indexName is an index identifier validated by IsSafeIdentifier (^[a-zA-Z0-9_]+$) at the top of this method and double-quoted via QuoteSqliteIdentifier before interpolation. Identifiers cannot be parameterized.")]
    private static async Task<(List<string> Columns, bool IsUnique)> GetIndexDetailSqliteAsync(
        DbConnection connection, string indexName, CancellationToken ct)
    {
        var columns = new List<string>();
        var isUnique = false;

        if (!IsSafeIdentifier(indexName))
            return (columns, isUnique);

        var cmd = connection.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        // PRAGMA index_list gives us the unique flag; PRAGMA index_info gives columns
        // Rationale: PRAGMA does not support parameter binding; identifier is allowlist-validated
        // (^[a-zA-Z0-9_]+$) and double-quoted before interpolation — injection is not possible.
        cmd.CommandText = $"PRAGMA index_info({QuoteSqliteIdentifier(indexName)})";
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            columns.Add(reader.GetString(2)); // name column

        // Check uniqueness via index_list on any table that has this index
        var cmd2 = connection.CreateCommand();
        await using var __cmd2 = cmd2.ConfigureAwait(false);
        cmd2.CommandText = $"""
            SELECT "unique" FROM sqlite_master sm
            CROSS JOIN pragma_index_list(sm.name) il
            WHERE il.name = @idx
            LIMIT 1
            """;
        var param = cmd2.CreateParameter();
        param.ParameterName = "@idx";
        param.Value = indexName;
        cmd2.Parameters.Add(param);

        var result = await cmd2.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is long l) isUnique = l != 0;
        else if (result is int i) isUnique = i != 0;

        return (columns, isUnique);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "SQLite PRAGMA does not support parameter binding; tableName is a table identifier validated by IsSafeIdentifier (^[a-zA-Z0-9_]+$) before the PRAGMA and double-quoted via QuoteSqliteIdentifier before interpolation. Identifiers cannot be parameterized.")]
    private static async Task<List<ForeignKeyInfo>> GetForeignKeysSqliteAsync(
        DbConnection connection, Regex? filter, CancellationToken ct)
    {
        var foreignKeys = new List<ForeignKeyInfo>();

        // Get all table names first
        var tablesCmd = connection.CreateCommand();
        await using var __tablesCmd = tablesCmd.ConfigureAwait(false);
        tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        var tablesReader = await tablesCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __tablesReader = tablesReader.ConfigureAwait(false);

        var tableNames = new List<string>();
        while (await tablesReader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = tablesReader.GetString(0);
            if (filter is null || filter.IsMatch(name))
                tableNames.Add(name);
        }

        foreach (var tableName in tableNames)
        {
            // Skip tables with non-allowlist names (attacker-controlled DB defense).
            if (!IsSafeIdentifier(tableName))
                continue;

            var fkCmd = connection.CreateCommand();
            await using var __fkCmd = fkCmd.ConfigureAwait(false);
            // PRAGMA foreign_key_list returns: id, seq, table, from, to, on_update, on_delete, match
            // Rationale: PRAGMA does not support parameter binding; identifier is allowlist-validated
            // (^[a-zA-Z0-9_]+$) and double-quoted before interpolation — injection is not possible.
            fkCmd.CommandText = $"PRAGMA foreign_key_list({QuoteSqliteIdentifier(tableName)})";
            var fkReader = await fkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using var __fkReader = fkReader.ConfigureAwait(false);

            // Group by FK id
            var fkMap = new Dictionary<int, ForeignKeyInfo>();
            while (await fkReader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = fkReader.GetInt32(0);
                var targetTable = fkReader.GetString(2);
                var fromCol = fkReader.GetString(3);
                var toCol = fkReader.GetString(4);

                if (!fkMap.TryGetValue(id, out var fk))
                {
                    fk = new ForeignKeyInfo
                    {
                        Name = $"fk_{tableName}_{targetTable}_{id}",
                        SourceTable = tableName,
                        SourceColumns = [],
                        TargetTable = targetTable,
                        TargetColumns = [],
                    };
                    fkMap[id] = fk;
                }

                fk = fk with
                {
                    SourceColumns = [.. fk.SourceColumns, fromCol],
                    TargetColumns = [.. fk.TargetColumns, toCol],
                };
                fkMap[id] = fk;
            }

            foreignKeys.AddRange(fkMap.Values);
        }

        return foreignKeys;
    }

    // internal (not private) so the SQL-injection invariant can be proven by unit tests
    // (Orkeon.Tools.Data.Tests via InternalsVisibleTo) rather than only asserted in a justification.
    internal static string QuoteSqliteIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>
    /// Returns true when the identifier matches the allowlist <c>^[a-zA-Z0-9_]+$</c>.
    /// PRAGMA statements do not accept parameters, so only allowlisted identifiers may be interpolated.
    /// </summary>
    internal static bool IsSafeIdentifier(string identifier)
        => !string.IsNullOrEmpty(identifier) && SafeIdentifierRegex().IsMatch(identifier);

    [GeneratedRegex(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex SafeIdentifierRegex();

    // Detects semicolons, single quotes, double dashes (SQL comment), and path traversal sequences
    // that indicate a SQL injection attempt in a user-supplied filter value.
    [GeneratedRegex(@"[;'""]|--|\.\.\/|\.\.\\", RegexOptions.Compiled)]
    private static partial Regex SqlInjectionPatternRegex();

    // ── Generic ADO.NET GetSchema introspection ──────────────────────────

    private async Task<List<TableInfo>> GetTablesGenericAsync(
        DbConnection connection, Regex? filter, SchemaScope scope)
    {
        var tablesDataTable = await Task.Run(() => connection.GetSchema("Tables")).ConfigureAwait(false);
        var tables = new List<TableInfo>();

        foreach (DataRow row in tablesDataTable.Rows)
        {
            var tableName = GetStringColumn(row, "TABLE_NAME", "table_name");
            if (string.IsNullOrEmpty(tableName))
                continue;

            var tableType = GetStringColumn(row, "TABLE_TYPE", "table_type") ?? "";
            if (tableType.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase))
                continue;

            if (filter is not null && !filter.IsMatch(tableName))
                continue;

            var schema = GetStringColumn(row, "TABLE_SCHEMA", "table_schema");

            var columns = new List<ColumnInfo>();
            if (scope is SchemaScope.Tables or SchemaScope.Columns or SchemaScope.All)
                columns = await GetColumnsGenericAsync(connection, tableName).ConfigureAwait(false);

            tables.Add(new TableInfo
            {
                Name = tableName,
                Schema = schema,
                Columns = columns,
            });
        }

        return tables;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort schema introspection fault barrier: DbConnection.GetSchema can throw provider-specific exceptions when a metadata collection/restriction is unsupported; any failure is traced and a partial column list is returned rather than aborting the schema scan.")]
    private async Task<List<ColumnInfo>> GetColumnsGenericAsync(
        DbConnection connection, string tableName)
    {
        var columns = new List<ColumnInfo>();

        try
        {
            var restrictions = new string?[3];
            restrictions[2] = tableName;
            var columnsTable = await Task.Run(() => connection.GetSchema("Columns", restrictions)).ConfigureAwait(false);

            var pkColumns = await GetPrimaryKeyColumnsGenericAsync(connection, tableName).ConfigureAwait(false);

            foreach (DataRow row in columnsTable.Rows)
            {
                var colName = GetStringColumn(row, "COLUMN_NAME", "column_name") ?? "";
                if (string.IsNullOrEmpty(colName))
                    continue;

                columns.Add(new ColumnInfo
                {
                    Name = colName,
                    DataType = GetStringColumn(row, "DATA_TYPE", "data_type") ?? "",
                    IsNullable = IsNullableColumn(row),
                    IsPrimaryKey = pkColumns.Contains(colName, StringComparer.OrdinalIgnoreCase),
                    DefaultValue = GetStringColumn(row, "COLUMN_DEFAULT", "column_default"),
                    MaxLength = GetIntColumn(row, "CHARACTER_MAXIMUM_LENGTH", "character_maximum_length"),
                });
            }
        }
        catch (Exception ex)
        {
            // Best-effort: some providers may not support column restrictions.
            // Trace it so a partial schema is not mistaken for an exhaustive one.
            LogSchemaIntrospectionPartial(ex, "Columns", tableName);
        }

        return columns;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort schema introspection fault barrier: DbConnection.GetSchema can throw provider-specific exceptions when a metadata collection/restriction is unsupported; any failure is traced and a partial primary-key set is returned rather than aborting the schema scan.")]
    private async Task<HashSet<string>> GetPrimaryKeyColumnsGenericAsync(
        DbConnection connection, string tableName)
    {
        var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var restrictions = new string?[4];
            restrictions[2] = tableName;

            var indexColumns = await Task.Run(() => connection.GetSchema("IndexColumns", restrictions)).ConfigureAwait(false);

            foreach (DataRow row in indexColumns.Rows)
            {
                var indexName = GetStringColumn(row, "INDEX_NAME", "index_name",
                    "CONSTRAINT_NAME", "constraint_name") ?? "";

                if (indexName.Contains("pk", StringComparison.OrdinalIgnoreCase) ||
                    indexName.Contains("primary", StringComparison.OrdinalIgnoreCase))
                {
                    var colName = GetStringColumn(row, "COLUMN_NAME", "column_name") ?? "";
                    if (!string.IsNullOrEmpty(colName))
                        pkColumns.Add(colName);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort PK detection; trace failures so an incomplete key set is visible.
            LogSchemaIntrospectionPartial(ex, "PrimaryKeys", tableName);
        }

        return pkColumns;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort schema introspection fault barrier: DbConnection.GetSchema can throw provider-specific exceptions when a metadata collection/restriction is unsupported; any failure is traced and a partial index list is returned rather than aborting the schema scan.")]
    private async Task<List<IndexInfo>> GetIndexesGenericAsync(
        DbConnection connection, Regex? filter)
    {
        var indexes = new List<IndexInfo>();

        try
        {
            var indexesTable = await Task.Run(() => connection.GetSchema("Indexes")).ConfigureAwait(false);

            foreach (DataRow row in indexesTable.Rows)
            {
                var tableName = GetStringColumn(row, "TABLE_NAME", "table_name") ?? "";
                if (filter is not null && !filter.IsMatch(tableName))
                    continue;

                var indexName = GetStringColumn(row, "INDEX_NAME", "index_name") ?? "";
                if (string.IsNullOrEmpty(indexName))
                    continue;

                var isUnique = GetBoolColumn(row, "UNIQUE", "unique", "IS_UNIQUE", "is_unique");
                var indexColumns = await GetIndexColumnsGenericAsync(connection, tableName, indexName).ConfigureAwait(false);

                indexes.Add(new IndexInfo
                {
                    Name = indexName,
                    TableName = tableName,
                    Columns = indexColumns,
                    IsUnique = isUnique,
                });
            }
        }
        catch (Exception ex)
        {
            // Best-effort index introspection; trace so a partial index list is visible.
            LogSchemaIntrospectionPartial(ex, "Indexes", "*");
        }

        return indexes;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort schema introspection fault barrier: DbConnection.GetSchema can throw provider-specific exceptions when a metadata collection/restriction is unsupported; any failure is traced and a partial index-column list is returned rather than aborting the schema scan.")]
    private async Task<List<string>> GetIndexColumnsGenericAsync(
        DbConnection connection, string tableName, string indexName)
    {
        var columns = new List<string>();

        try
        {
            var restrictions = new string?[4];
            restrictions[2] = tableName;
            restrictions[3] = indexName;

            var table = await Task.Run(() => connection.GetSchema("IndexColumns", restrictions)).ConfigureAwait(false);

            foreach (DataRow row in table.Rows)
            {
                var colName = GetStringColumn(row, "COLUMN_NAME", "column_name") ?? "";
                if (!string.IsNullOrEmpty(colName))
                    columns.Add(colName);
            }
        }
        catch (Exception ex)
        {
            // Best-effort index-column introspection; trace partial results.
            LogSchemaIntrospectionPartial(ex, "IndexColumns", tableName);
        }

        return columns;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort schema introspection fault barrier: DbConnection.GetSchema can throw provider-specific exceptions when a metadata collection/restriction is unsupported; any failure is traced and a partial foreign-key set is returned rather than aborting the schema scan.")]
    private async Task<List<ForeignKeyInfo>> GetForeignKeysGenericAsync(
        DbConnection connection, Regex? filter)
    {
        var foreignKeys = new List<ForeignKeyInfo>();

        try
        {
            var fkTable = await Task.Run(() => connection.GetSchema("ForeignKeys")).ConfigureAwait(false);

            foreach (DataRow row in fkTable.Rows)
            {
                var tableName = GetStringColumn(row, "TABLE_NAME", "table_name") ?? "";
                if (filter is not null && !filter.IsMatch(tableName))
                    continue;

                var fkName = GetStringColumn(row, "CONSTRAINT_NAME", "constraint_name",
                    "FK_NAME", "fk_name") ?? "";
                var targetTable = GetStringColumn(row, "FKEY_TO_TABLE", "fkey_to_table",
                    "REFERENCED_TABLE_NAME", "referenced_table_name") ?? "";

                if (string.IsNullOrEmpty(fkName) && string.IsNullOrEmpty(targetTable))
                    continue;

                foreignKeys.Add(new ForeignKeyInfo
                {
                    Name = fkName,
                    SourceTable = tableName,
                    SourceColumns = GetForeignKeyColumns(row, "FKEY_FROM_COLUMN", "fkey_from_column"),
                    TargetTable = targetTable,
                    TargetColumns = GetForeignKeyColumns(row, "FKEY_TO_COLUMN", "fkey_to_column"),
                });
            }
        }
        catch (Exception ex)
        {
            // Best-effort FK introspection; trace so a partial FK set is visible.
            LogSchemaIntrospectionPartial(ex, "ForeignKeys", "*");
        }

        return foreignKeys;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Regex? BuildWildcardRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var escaped = Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal);
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    }

    private static string? GetStringColumn(DataRow row, params string[] candidateNames)
    {
        foreach (var name in candidateNames.Where(name => row.Table.Columns.Contains(name)))
        {
            var val = row[name];
            if (val is string s && !string.IsNullOrEmpty(s))
                return s;
        }
        return null;
    }

    private static int? GetIntColumn(DataRow row, params string[] candidateNames)
    {
        foreach (var name in candidateNames.Where(name => row.Table.Columns.Contains(name)))
        {
            var val = row[name];
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is short s) return s;
        }
        return null;
    }

    private static bool GetBoolColumn(DataRow row, params string[] candidateNames)
    {
        foreach (var name in candidateNames.Where(name => row.Table.Columns.Contains(name)))
        {
            var val = row[name];
            if (val is bool b) return b;
            if (val is int i) return i != 0;
            if (val is long l) return l != 0;
            if (val is string s)
                return s.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                       s.Equals("1", StringComparison.Ordinal);
        }
        return false;
    }

    private static bool IsNullableColumn(DataRow row)
    {
        var val = GetStringColumn(row, "IS_NULLABLE", "is_nullable");
        if (val is not null)
            return val.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                   val.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                   val.Equals("1", StringComparison.Ordinal);

        return GetBoolColumn(row, "IS_NULLABLE", "is_nullable", "NULLABLE", "nullable");
    }

    private static List<string> GetForeignKeyColumns(DataRow row, params string[] candidateNames)
    {
        var val = GetStringColumn(row, candidateNames);
        return val is not null ? [val] : [];
    }

    private static string ExtractDatabaseNameFromConnectionString(string connectionString)
    {
        var match = Regex.Match(connectionString, @"(?:Database|Initial Catalog)\s*=\s*([^;]+)",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
        if (match.Success)
            return match.Groups[1].Value.Trim();

        match = Regex.Match(connectionString, @"Data Source\s*=\s*([^;]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return "";
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Schema introspection completed: {TableCount} tables, {IndexCount} indexes, {FkCount} foreign keys")]
    private partial void LogSchemaIntrospectionCompleted(int tableCount, int indexCount, int fkCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Schema introspection for '{SchemaCollection}' (scope '{Target}') was partial; the reported schema may be incomplete.")]
    private partial void LogSchemaIntrospectionPartial(Exception ex, string schemaCollection, string target);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rejected table filter containing suspicious SQL pattern: {Filter}")]
    private partial void LogSuspiciousTableFilter(string filter);
}
