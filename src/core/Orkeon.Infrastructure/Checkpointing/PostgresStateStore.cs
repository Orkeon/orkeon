using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Checkpointing;
using Microsoft.Extensions.Options;
using Npgsql;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// Validates PostgreSQL schema names using a strict whitelist approach to prevent SQL injection.
/// Schema names are interpolated into SQL commands and cannot be parameterized,
/// so validation must reject any value that is not a safe PostgreSQL identifier.
/// </summary>
internal static partial class SchemaNameValidator
{
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ValidSchemaNameRegex();

    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "public", "information_schema", "pg_catalog", "schema", "all",
        "current_user", "session_user", "user"
    };

    /// <summary>
    /// Validates that <paramref name="schemaName"/> is a safe PostgreSQL identifier.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the schema name is empty, too long, contains invalid characters,
    /// or matches a reserved PostgreSQL keyword.
    /// </exception>
    public static void ValidateSchemaName(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
            throw new ArgumentException("Schema name cannot be empty.", nameof(schemaName));

        if (schemaName.Length > 63)
            throw new ArgumentException("Schema name cannot exceed 63 characters.", nameof(schemaName));

        if (!ValidSchemaNameRegex().IsMatch(schemaName))
            throw new ArgumentException(
                "Schema name must contain only alphanumeric characters and underscores, " +
                "and must start with a letter or underscore.", nameof(schemaName));

        if (ReservedKeywords.Contains(schemaName))
            throw new ArgumentException(
                $"Schema name '{schemaName}' is a reserved PostgreSQL keyword.",
                nameof(schemaName));
    }
}

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IStateStore"/> with full time-travel support.
/// Uses Npgsql directly (no EF Core). Stores session state as JSONB for efficient querying.
/// </summary>
public sealed class PostgresStateStore : IStateStore, IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _schema;
    private readonly bool _autoMigrate;
    private bool _migrated;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>
    /// Initializes a new instance of <see cref="PostgresStateStore"/>.
    /// </summary>
    public PostgresStateStore(IOptions<PostgresStateStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.ConnectionString))
            throw new ArgumentException("ConnectionString is required", nameof(options));

        SchemaNameValidator.ValidateSchemaName(opts.SchemaName);

        _schema = opts.SchemaName;
        _autoMigrate = opts.AutoMigrate;
        _dataSource = NpgsqlDataSource.Create(opts.ConnectionString);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated; it is validated at construction via SchemaNameValidator (regex ^[a-zA-Z_][a-zA-Z0-9_]*$ + reserved-keyword denylist) and SQL identifiers cannot be parameterized. No caller values are interpolated.")]
    private async Task EnsureMigratedAsync(CancellationToken ct)
    {
        if (_migrated || !_autoMigrate)
            return;

        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        var migrateSql = $"""
            CREATE SCHEMA IF NOT EXISTS {_schema};

            CREATE TABLE IF NOT EXISTS {_schema}.sessions (
                id TEXT PRIMARY KEY,
                crew_id TEXT NOT NULL,
                state_json JSONB NOT NULL,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS {_schema}.session_versions (
                version_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                state_json JSONB NOT NULL,
                step_id TEXT,
                label TEXT,
                forked_from_session TEXT,
                forked_from_version TEXT,
                created_at TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_sv_session_version ON {_schema}.session_versions (session_id, version DESC);
            CREATE INDEX IF NOT EXISTS idx_sv_session_step ON {_schema}.session_versions (session_id, step_id);
            CREATE INDEX IF NOT EXISTS idx_sv_session_created ON {_schema}.session_versions (session_id, created_at);
            """;
        cmd.CommandText = migrateSql; // NOSONAR: _schema is a validated PostgreSQL identifier (alphanumeric+underscore only), checked at construction via SchemaNameValidator; DDL cannot use parameterized identifiers
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _migrated = true;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(state, JsonOptions);

        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        var upsertSql = $"""
            INSERT INTO {_schema}.sessions (id, crew_id, state_json, created_at, updated_at)
            VALUES (@id, @crewId, @stateJson::jsonb, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                crew_id = @crewId,
                state_json = @stateJson::jsonb,
                updated_at = @updatedAt
            """;
        cmd.CommandText = upsertSql; // NOSONAR: _schema is a validated PostgreSQL identifier (alphanumeric+underscore only), checked at construction via SchemaNameValidator; all values are parameterized
        cmd.Parameters.AddWithValue("id", state.SessionId);
        cmd.Parameters.AddWithValue("crewId", state.CrewId);
        cmd.Parameters.AddWithValue("stateJson", json);
        cmd.Parameters.AddWithValue("createdAt", state.CreatedAt);
        cmd.Parameters.AddWithValue("updatedAt", state.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        cmd.CommandText = $"SELECT state_json::text FROM {_schema}.sessions WHERE id = @id"; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
        cmd.Parameters.AddWithValue("id", sessionId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<SessionState?> GetLatestForCrewAsync(string crewId, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        cmd.CommandText = $"SELECT state_json::text FROM {_schema}.sessions WHERE crew_id = @crewId ORDER BY updated_at DESC LIMIT 1"; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
        cmd.Parameters.AddWithValue("crewId", crewId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); the query contains no caller input.")]
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var summaries = new List<SessionSummary>();

        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        cmd.CommandText = $"SELECT state_json::text FROM {_schema}.sessions ORDER BY updated_at DESC"; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); no user input in this query

        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
            if (state != null)
            {
                summaries.Add(new SessionSummary
                {
                    SessionId = state.SessionId,
                    CrewId = state.CrewId,
                    Phase = state.Phase,
                    CompletedTaskCount = state.TaskCheckpoints.Count(t => t.Value.Status == CheckpointStatus.Completed),
                    CreatedAt = state.CreatedAt,
                    UpdatedAt = state.UpdatedAt
                });
            }
        }

        return summaries;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        cmd.CommandText = $"DELETE FROM {_schema}.sessions WHERE id = @id"; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
        cmd.Parameters.AddWithValue("id", sessionId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Time-travel methods ──

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<VersionedState> SaveVersionedAsync(SessionState state, string? stepId = null, string? label = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);

        // Get next version number
        int nextVersion;
        var countCmd = conn.CreateCommand();
        await using (countCmd.ConfigureAwait(false))
        {
            countCmd.CommandText = $"SELECT COALESCE(MAX(version), 0) FROM {_schema}.session_versions WHERE session_id = @sessionId"; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
            countCmd.Parameters.AddWithValue("sessionId", state.SessionId);
            nextVersion = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) + 1;
        }

        var versionId = Guid.NewGuid().ToString();
        var stateJson = JsonSerializer.Serialize(state, JsonOptions);
        var createdAt = DateTime.UtcNow;

        var insertCmd = conn.CreateCommand();
        await using (insertCmd.ConfigureAwait(false))
        {
            var insertVersionSql = $"""
                INSERT INTO {_schema}.session_versions (version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at)
                VALUES (@versionId, @sessionId, @version, @stateJson::jsonb, @stepId, @label, NULL, NULL, @createdAt)
                """;
            insertCmd.CommandText = insertVersionSql; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
            insertCmd.Parameters.AddWithValue("versionId", versionId);
            insertCmd.Parameters.AddWithValue("sessionId", state.SessionId);
            insertCmd.Parameters.AddWithValue("version", nextVersion);
            insertCmd.Parameters.AddWithValue("stateJson", stateJson);
            insertCmd.Parameters.AddWithValue("stepId", (object?)stepId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("label", (object?)label ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("createdAt", createdAt);
            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await SaveAsync(state, ct).ConfigureAwait(false);

        return new VersionedState
        {
            VersionId = versionId,
            SessionId = state.SessionId,
            Version = nextVersion,
            State = state,
            StepId = stepId,
            Label = label,
            CreatedAt = createdAt
        };
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);

        var sql = $"SELECT version_id, session_id, version, state_json::text, step_id, label, created_at FROM {_schema}.session_versions WHERE session_id = @sessionId ORDER BY version DESC"; // NOSONAR: _schema is a validated PostgreSQL identifier (alphanumeric+underscore only), checked at construction via SchemaNameValidator; all values are parameterized
        if (limit.HasValue)
            sql += " LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        if (limit.HasValue)
            cmd.Parameters.AddWithValue("limit", limit.Value);

        var summaries = new List<VersionSummary>();
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var stateJson = reader.GetString(3);
            var state = JsonSerializer.Deserialize<SessionState>(stateJson, JsonOptions);

            summaries.Add(new VersionSummary
            {
                VersionId = reader.GetString(0),
                SessionId = reader.GetString(1),
                Version = reader.GetInt32(2),
                Phase = state?.Phase ?? SessionPhase.Pending,
                CompletedTaskCount = state?.TaskCheckpoints.Count(t => t.Value.Status == CheckpointStatus.Completed) ?? 0,
                StepId = await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<string>(4, ct).ConfigureAwait(false),
                Label = await reader.IsDBNullAsync(5, ct).ConfigureAwait(false) ? null : await reader.GetFieldValueAsync<string>(5, ct).ConfigureAwait(false),
                CreatedAt = reader.GetDateTime(6)
            });
        }

        return summaries;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<VersionedState?> GetVersionAsync(string sessionId, string versionId, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        var getVersionSql = $"""
            SELECT version_id, session_id, version, state_json::text, step_id, label, forked_from_session, forked_from_version, created_at
            FROM {_schema}.session_versions
            WHERE session_id = @sessionId AND version_id = @versionId
            """;
        cmd.CommandText = getVersionSql; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("versionId", versionId);

        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadVersionedState(reader);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<VersionedState?> GetAtAsync(string sessionId, DateTime timestamp, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        var getAtSql = $"""
            SELECT version_id, session_id, version, state_json::text, step_id, label, forked_from_session, forked_from_version, created_at
            FROM {_schema}.session_versions
            WHERE session_id = @sessionId AND created_at <= @timestamp
            ORDER BY created_at DESC
            LIMIT 1
            """;
        cmd.CommandText = getAtSql; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("timestamp", timestamp);

        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadVersionedState(reader);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<VersionedState?> GetByStepAsync(string sessionId, string stepId, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var cmd = conn.CreateCommand();
        await using var __cmd = cmd.ConfigureAwait(false);
        var getByStepSql = $"""
            SELECT version_id, session_id, version, state_json::text, step_id, label, forked_from_session, forked_from_version, created_at
            FROM {_schema}.session_versions
            WHERE session_id = @sessionId AND step_id = @stepId
            LIMIT 1
            """;
        cmd.CommandText = getByStepSql; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("stepId", stepId);

        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using var __reader = reader.ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadVersionedState(reader);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "Only the {_schema} identifier is interpolated (validated at construction via SchemaNameValidator; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public async Task<VersionedState> ForkAsync(string sourceSessionId, string sourceVersionId, string? label = null, CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);
        var sourceVersion = await GetVersionAsync(sourceSessionId, sourceVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{sourceVersionId}' not found in session '{sourceSessionId}'");

        var newSessionId = Guid.NewGuid().ToString();
        var newVersionId = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow;
        var finalLabel = label ?? $"Fork from {sourceSessionId}@v{sourceVersion.Version}";

        var forkedState = sourceVersion.State with
        {
            SessionId = newSessionId,
            UpdatedAt = createdAt
        };

        var stateJson = JsonSerializer.Serialize(forkedState, JsonOptions);

        var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var __conn = conn.ConfigureAwait(false);
        var transaction = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var __transaction = transaction.ConfigureAwait(false);
        try
        {
            var insertCmd = conn.CreateCommand();
            await using (insertCmd.ConfigureAwait(false))
            {
                insertCmd.Transaction = transaction;
                var forkVersionSql = $"""
                    INSERT INTO {_schema}.session_versions (version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at)
                    VALUES (@versionId, @sessionId, 1, @stateJson::jsonb, NULL, @label, @forkedFromSession, @forkedFromVersion, @createdAt)
                    """;
                insertCmd.CommandText = forkVersionSql; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
                insertCmd.Parameters.AddWithValue("versionId", newVersionId);
                insertCmd.Parameters.AddWithValue("sessionId", newSessionId);
                insertCmd.Parameters.AddWithValue("stateJson", stateJson);
                insertCmd.Parameters.AddWithValue("label", finalLabel);
                insertCmd.Parameters.AddWithValue("forkedFromSession", sourceSessionId);
                insertCmd.Parameters.AddWithValue("forkedFromVersion", sourceVersionId);
                insertCmd.Parameters.AddWithValue("createdAt", createdAt);
                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var sessCmd = conn.CreateCommand();
            await using (sessCmd.ConfigureAwait(false))
            {
                sessCmd.Transaction = transaction;
                var forkSessionSql = $"""
                    INSERT INTO {_schema}.sessions (id, crew_id, state_json, created_at, updated_at)
                    VALUES (@id, @crewId, @stateJson::jsonb, @createdAt, @updatedAt)
                    """;
                sessCmd.CommandText = forkSessionSql; // NOSONAR: _schema validated at construction via SchemaNameValidator (alphanumeric+underscore only); all values parameterized
                sessCmd.Parameters.AddWithValue("id", newSessionId);
                sessCmd.Parameters.AddWithValue("crewId", forkedState.CrewId);
                sessCmd.Parameters.AddWithValue("stateJson", stateJson);
                sessCmd.Parameters.AddWithValue("createdAt", createdAt);
                sessCmd.Parameters.AddWithValue("updatedAt", createdAt);
                await sessCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        return new VersionedState
        {
            VersionId = newVersionId,
            SessionId = newSessionId,
            Version = 1,
            State = forkedState,
            Label = finalLabel,
            ForkedFromSession = sourceSessionId,
            ForkedFromVersion = sourceVersionId,
            CreatedAt = createdAt
        };
    }

    /// <inheritdoc />
    public async Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default)
    {
        var fromVersion = await GetVersionAsync(sessionId, fromVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{fromVersionId}' not found");
        var toVersion = await GetVersionAsync(sessionId, toVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{toVersionId}' not found");

        return ComputeDiff(fromVersion, toVersion);
    }

    private static VersionedState ReadVersionedState(NpgsqlDataReader reader)
    {
        var stateJson = reader.GetString(3);
        var state = JsonSerializer.Deserialize<SessionState>(stateJson, JsonOptions)!;

        return new VersionedState
        {
            VersionId = reader.GetString(0),
            SessionId = reader.GetString(1),
            Version = reader.GetInt32(2),
            State = state,
            StepId = reader.IsDBNull(4) ? null : reader.GetString(4),
            Label = reader.IsDBNull(5) ? null : reader.GetString(5),
            ForkedFromSession = reader.IsDBNull(6) ? null : reader.GetString(6),
            ForkedFromVersion = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt = reader.GetDateTime(8)
        };
    }

    private static StateDiff ComputeDiff(VersionedState from, VersionedState to)
    {
        var fromTasks = from.State.TaskCheckpoints;
        var toTasks = to.State.TaskCheckpoints;

        var allTaskIds = fromTasks.Keys.Union(toTasks.Keys).ToList();
        var addedIds = toTasks.Keys.Except(fromTasks.Keys).ToList();
        var removedIds = fromTasks.Keys.Except(toTasks.Keys).ToList();

        var taskDiffs = new List<TaskDiff>();
        foreach (var taskId in allTaskIds)
        {
            var hasFrom = fromTasks.TryGetValue(taskId, out var fromTask);
            var hasTo = toTasks.TryGetValue(taskId, out var toTask);

            if (hasFrom && hasTo
                && (fromTask!.Status != toTask!.Status || fromTask.Output != toTask.Output))
            {
                taskDiffs.Add(new TaskDiff
                {
                    TaskId = taskId,
                    FromStatus = fromTask.Status,
                    ToStatus = toTask.Status,
                    OutputChanged = fromTask.Output != toTask.Output
                });
            }
        }

        return new StateDiff
        {
            FromVersionId = from.VersionId,
            ToVersionId = to.VersionId,
            PhaseChanged = from.State.Phase != to.State.Phase,
            FromPhase = from.State.Phase,
            ToPhase = to.State.Phase,
            TaskDiffs = taskDiffs,
            AddedTaskIds = addedIds,
            RemovedTaskIds = removedIds
        };
    }

    /// <summary>Disposes the data source.</summary>
    public void Dispose()
    {
        _dataSource.Dispose();
    }

    /// <summary>Asynchronously disposes the data source.</summary>
    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
