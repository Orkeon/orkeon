using System.Globalization;
using System.Text.Json;
using Orkeon.Application.Interfaces.Checkpointing;
using Microsoft.Data.Sqlite;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Persistence.Sqlite;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// SQLite-backed implementation of <see cref="IStateStore"/>.
/// Stores session state as serialized JSON in a single table,
/// with an append-only version history table for time-travel support.
/// </summary>
public sealed class SqliteStateStore : IStateStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteStateStore"/>.
    /// </summary>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=checkpoints.db" or "Data Source=:memory:").</param>
    /// <param name="fileSystem">Virtual file system used to govern the database file path (VFS-70 / 2F-A). Required.</param>
    public SqliteStateStore(string connectionString, IFileSystemService fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(fileSystem);

        // VFS-70 (2F-A): resolve + validate the file Data Source through the VFS before
        // handing it to the SQLite engine. :memory: databases pass through untouched.
        var governedConnectionString = SqliteDataSourceGovernor.Govern(connectionString, fileSystem);

        _connection = new SqliteConnection(governedConnectionString);
        _connection.Open();
        EnsureTablesExist();
    }

    private void EnsureTablesExist()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                crew_id TEXT NOT NULL,
                state_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS session_versions (
                version_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                state_json TEXT NOT NULL,
                step_id TEXT,
                label TEXT,
                forked_from_session TEXT,
                forked_from_version TEXT,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_sv_session_version ON session_versions (session_id, version DESC);
            CREATE INDEX IF NOT EXISTS idx_sv_session_step ON session_versions (session_id, step_id);
            CREATE INDEX IF NOT EXISTS idx_sv_session_created ON session_versions (session_id, created_at);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return SaveCoreAsync();

        async Task SaveCoreAsync()
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (id, crew_id, state_json, created_at, updated_at)
                VALUES (@id, @crewId, @stateJson, @createdAt, @updatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    crew_id = @crewId,
                    state_json = @stateJson,
                    updated_at = @updatedAt
                """;
            cmd.Parameters.AddWithValue("@id", state.SessionId);
            cmd.Parameters.AddWithValue("@crewId", state.CrewId);
            cmd.Parameters.AddWithValue("@stateJson", json);
            cmd.Parameters.AddWithValue("@createdAt", state.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@updatedAt", state.UpdatedAt.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT state_json FROM sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<SessionState?> GetLatestForCrewAsync(string crewId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT state_json FROM sessions WHERE crew_id = @crewId ORDER BY updated_at DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@crewId", crewId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        var summaries = new List<SessionSummary>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT state_json FROM sessions ORDER BY updated_at DESC";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
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
    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Time-travel methods ──

    /// <inheritdoc />
    public Task<VersionedState> SaveVersionedAsync(SessionState state, string? stepId = null, string? label = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return SaveVersionedCoreAsync();

        async Task<VersionedState> SaveVersionedCoreAsync()
        {
        // Get the next version number for this session
        int nextVersion;
        using (var countCmd = _connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM session_versions WHERE session_id = @sessionId";
            countCmd.Parameters.AddWithValue("@sessionId", state.SessionId);
            var maxVersion = await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            nextVersion = Convert.ToInt32(maxVersion, CultureInfo.InvariantCulture) + 1;
        }

        var versionId = Guid.NewGuid().ToString();
        var stateJson = JsonSerializer.Serialize(state, JsonOptions);
        var createdAt = DateTime.UtcNow;

        using (var insertCmd = _connection.CreateCommand())
        {
            insertCmd.CommandText = """
                INSERT INTO session_versions (version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at)
                VALUES (@versionId, @sessionId, @version, @stateJson, @stepId, @label, NULL, NULL, @createdAt)
                """;
            insertCmd.Parameters.AddWithValue("@versionId", versionId);
            insertCmd.Parameters.AddWithValue("@sessionId", state.SessionId);
            insertCmd.Parameters.AddWithValue("@version", nextVersion);
            insertCmd.Parameters.AddWithValue("@stateJson", stateJson);
            insertCmd.Parameters.AddWithValue("@stepId", (object?)stepId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@label", (object?)label ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Also update the current session state
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
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        var sql = "SELECT version_id, session_id, version, state_json, step_id, label, created_at FROM session_versions WHERE session_id = @sessionId ORDER BY version DESC";
        if (limit.HasValue)
            sql += " LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        if (limit.HasValue)
            cmd.Parameters.AddWithValue("@limit", limit.Value);

        var summaries = new List<VersionSummary>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
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
                CreatedAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
            });
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<VersionedState?> GetVersionAsync(string sessionId, string versionId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at
            FROM session_versions
            WHERE session_id = @sessionId AND version_id = @versionId
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@versionId", versionId);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadVersionedState(reader);
    }

    /// <inheritdoc />
    public async Task<VersionedState?> GetAtAsync(string sessionId, DateTime timestamp, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at
            FROM session_versions
            WHERE session_id = @sessionId AND created_at <= @timestamp
            ORDER BY created_at DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadVersionedState(reader);
    }

    /// <inheritdoc />
    public async Task<VersionedState?> GetByStepAsync(string sessionId, string stepId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at
            FROM session_versions
            WHERE session_id = @sessionId AND step_id = @stepId
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@stepId", stepId);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadVersionedState(reader);
    }

    /// <inheritdoc />
    public async Task<VersionedState> ForkAsync(string sourceSessionId, string sourceVersionId, string? label = null, CancellationToken ct = default)
    {
        var sourceVersion = await GetVersionAsync(sourceSessionId, sourceVersionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Version '{sourceVersionId}' not found in session '{sourceSessionId}'");

        var newSessionId = Guid.NewGuid().ToString();
        var newVersionId = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow;

        var forkedState = sourceVersion.State with
        {
            SessionId = newSessionId,
            UpdatedAt = createdAt
        };

        var stateJson = JsonSerializer.Serialize(forkedState, JsonOptions);
        var finalLabel = label ?? $"Fork from {sourceSessionId}@v{sourceVersion.Version}";

        // Use a transaction for atomicity
        var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var __transaction = transaction.ConfigureAwait(false);
        try
        {
            // Insert the forked version
            using (var insertCmd = _connection.CreateCommand())
            {
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO session_versions (version_id, session_id, version, state_json, step_id, label, forked_from_session, forked_from_version, created_at)
                    VALUES (@versionId, @sessionId, 1, @stateJson, NULL, @label, @forkedFromSession, @forkedFromVersion, @createdAt)
                    """;
                insertCmd.Parameters.AddWithValue("@versionId", newVersionId);
                insertCmd.Parameters.AddWithValue("@sessionId", newSessionId);
                insertCmd.Parameters.AddWithValue("@stateJson", stateJson);
                insertCmd.Parameters.AddWithValue("@label", finalLabel);
                insertCmd.Parameters.AddWithValue("@forkedFromSession", sourceSessionId);
                insertCmd.Parameters.AddWithValue("@forkedFromVersion", sourceVersionId);
                insertCmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Also insert into sessions table
            using (var sessCmd = _connection.CreateCommand())
            {
                sessCmd.Transaction = transaction;
                sessCmd.CommandText = """
                    INSERT INTO sessions (id, crew_id, state_json, created_at, updated_at)
                    VALUES (@id, @crewId, @stateJson, @createdAt, @updatedAt)
                    """;
                sessCmd.Parameters.AddWithValue("@id", newSessionId);
                sessCmd.Parameters.AddWithValue("@crewId", forkedState.CrewId);
                sessCmd.Parameters.AddWithValue("@stateJson", stateJson);
                sessCmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
                sessCmd.Parameters.AddWithValue("@updatedAt", createdAt.ToString("O"));
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

    private static VersionedState ReadVersionedState(SqliteDataReader reader)
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
            CreatedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Disposes the SQLite connection.
    /// </summary>
    public void Dispose()
    {
        _connection.Dispose();
    }
}
