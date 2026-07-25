using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Infrastructure.Persistence.Sqlite;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IMemoryProvider"/> (FON-002 / R3.2).
/// Persists memory items — content, embedding vector, importance and metadata — in a
/// single table, following the plumbing conventions of
/// <see cref="Orkeon.Infrastructure.Checkpointing.SqliteStateStore"/> (one long-lived
/// connection, parameterized commands, <c>ON CONFLICT</c> upserts, ISO-8601 timestamps).
/// </summary>
/// <remarks>
/// <para>
/// CRUD facade only — search entry points (full-text <c>LIKE</c> and cosine vector
/// similarity) live in <c>SqliteMemoryProvider.Search.cs</c>; logging delegates in
/// <c>SqliteMemoryProvider.Logging.cs</c>.
/// </para>
/// <para>
/// <see cref="Orkeon.Domain.Memory.IMemoryProvider"/> is intentionally re-listed in the
/// base list so that <c>StoreWithEmbeddingAsync</c>/<c>SearchSimilarAsync</c> calls made
/// through the interface dispatch to this provider's implementations rather than the
/// interface's empty default bodies (interface mapping is otherwise frozen at
/// <see cref="MemoryProviderBase"/>).
/// </para>
/// <para>
/// At-rest encryption: wrap this provider in
/// <see cref="EncryptedMemoryProviderDecorator"/> — content is encrypted before
/// hitting SQLite while embeddings and metadata stay clear for search/indexing.
/// </para>
/// </remarks>
public sealed partial class SqliteMemoryProvider : MemoryProviderBase, IMemoryProvider, IDisposable
{
    private readonly SqliteMemoryOptions _options;
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private const string ColumnList =
        "key, item_id, content, embedding, importance, source, relevance, tags_json, " +
        "created_by, created_at, last_accessed_at, access_count, custom_properties_json";

    /// <inheritdoc />
    public override string Name => "SQLite";

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteMemoryProvider"/> and opens the
    /// underlying connection (a single long-lived connection, required for
    /// <c>Data Source=:memory:</c> databases to outlive individual operations).
    /// </summary>
    /// <param name="options">SQLite memory provider options (connection string, table name).</param>
    /// <param name="fileSystem">Virtual file system used to govern the database file path (VFS-70 / 2F-A). Required.</param>
    /// <param name="logger">Optional logger.</param>
    public SqliteMemoryProvider(
        IOptions<SqliteMemoryOptions> options,
        IFileSystemService fileSystem,
        ILogger<SqliteMemoryProvider>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _options = options.Value;

        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateTableName(_options.TableName);

        // VFS-70 (2F-A): resolve + validate the file Data Source through the VFS before
        // handing it to the SQLite engine. :memory: databases pass through untouched.
        var governedConnectionString = SqliteDataSourceGovernor.Govern(_options.ConnectionString, fileSystem);

        _connection = new SqliteConnection(governedConnectionString);
        _connection.Open();
        EnsureTablesExist();
        LogDatabaseInitialized(_options.TableName);
    }

    /// <summary>
    /// Guards the table identifier (the only non-parameterizable SQL fragment)
    /// against injection from configuration.
    /// </summary>
    private static void ValidateTableName(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!TableNameRegex().IsMatch(tableName))
        {
            throw new ArgumentException(
                $"Invalid SQLite table name '{tableName}': only letters, digits and underscores are allowed (must not start with a digit).",
                nameof(tableName));
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex TableNameRegex();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragment is the {_options.TableName} identifier (validated at construction via ValidateTableName regex ^[A-Za-z_][A-Za-z0-9_]*$; identifiers cannot be parameterized in DDL); no caller values are interpolated.")]
    private void EnsureTablesExist()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {_options.TableName} (
                key TEXT PRIMARY KEY,
                item_id TEXT NOT NULL,
                content TEXT NOT NULL,
                embedding BLOB NULL,
                importance REAL NOT NULL,
                source TEXT NOT NULL,
                relevance REAL NOT NULL,
                tags_json TEXT NULL,
                created_by TEXT NULL,
                created_at TEXT NOT NULL,
                last_accessed_at TEXT NULL,
                access_count INTEGER NOT NULL DEFAULT 0,
                custom_properties_json TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_source ON {_options.TableName} (source);
            CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_created_at ON {_options.TableName} (created_at);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragments are the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized) and the {ColumnList} const; all caller values are passed as command parameters.")]
    public override async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);

        var record = SqliteMemoryRecord.FromMemoryItem(key, item);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = UpsertSql;
            AddRecordParameters(cmd, record);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            LogStoredMemoryItem(key);
        }
        catch (Exception ex)
        {
            LogException(ex, "StoreAsync", key);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragments are the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized) and the {ColumnList} const; all caller values are passed as command parameters.")]
    public override async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT {ColumnList} FROM {_options.TableName} WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                LogMemoryItemNotFound(key);
                return null;
            }

            LogRetrievedMemoryItem(key);
            return ReadRecord(reader).ToMemoryItem();
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAsync", key);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragment is the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized); all caller values are passed as command parameters.")]
    public override async Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);

        var record = SqliteMemoryRecord.FromMemoryItem(key, item);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"""
                UPDATE {_options.TableName} SET
                    item_id = @itemId,
                    content = @content,
                    embedding = @embedding,
                    importance = @importance,
                    source = @source,
                    relevance = @relevance,
                    tags_json = @tagsJson,
                    created_by = @createdBy,
                    created_at = @createdAt,
                    last_accessed_at = @lastAccessedAt,
                    access_count = @accessCount,
                    custom_properties_json = @customPropertiesJson
                WHERE key = @key
                """;
            AddRecordParameters(cmd, record);

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                LogCannotUpdateNonExistent(key);
                return false;
            }

            LogUpdatedMemoryItem(key);
            return true;
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateAsync", key);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragment is the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized); the key value is passed as a command parameter.")]
    public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {_options.TableName} WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var removed = affected > 0;
            LogDeletedMemoryItem(key, removed);
            return removed;
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteAsync", key);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragment is the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized); no caller values are interpolated.")]
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {_options.TableName}";

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            LogClearedMemoryItems(affected);
        }
        catch (Exception ex)
        {
            LogException(ex, "ClearAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragment is the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized); no caller values are interpolated.")]
    public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {_options.TableName}";

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var count = Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
            LogCountedMemoryItems(count);
            return count;
        }
        catch (Exception ex)
        {
            LogException(ex, "CountAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragment is the {_options.TableName} identifier (validated at construction via ValidateTableName regex; identifiers cannot be parameterized); the skip/take values are passed as command parameters.")]
    public override async Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT key FROM {_options.TableName} ORDER BY key LIMIT @take OFFSET @skip";
            cmd.Parameters.AddWithValue("@take", take);
            cmd.Parameters.AddWithValue("@skip", skip);

            var keys = new List<string>();
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                keys.Add(reader.GetString(0));
            }

            LogListedKeys(keys.Count, skip, take);
            return keys;
        }
        catch (Exception ex)
        {
            LogException(ex, "ListKeysAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stores a memory item with an associated embedding vector
    /// (re-implements the <see cref="IMemoryProvider"/> default so interface
    /// dispatch persists the embedding in the BLOB column).
    /// </summary>
    public Task StoreWithEmbeddingAsync(
        string key,
        MemoryItem item,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(embedding);

        item.SetEmbedding(embedding);
        return StoreAsync(key, item, cancellationToken);
    }

    /// <summary>
    /// Shared <c>INSERT … ON CONFLICT(key) DO UPDATE</c> statement used by
    /// <see cref="StoreAsync"/> and the batch upsert capability. The only interpolated
    /// fragments are the validated table identifier and the <see cref="ColumnList"/> const.
    /// </summary>
    private string UpsertSql => $"""
        INSERT INTO {_options.TableName} ({ColumnList})
        VALUES (@key, @itemId, @content, @embedding, @importance, @source, @relevance, @tagsJson,
                @createdBy, @createdAt, @lastAccessedAt, @accessCount, @customPropertiesJson)
        ON CONFLICT(key) DO UPDATE SET
            item_id = @itemId,
            content = @content,
            embedding = @embedding,
            importance = @importance,
            source = @source,
            relevance = @relevance,
            tags_json = @tagsJson,
            created_by = @createdBy,
            created_at = @createdAt,
            last_accessed_at = @lastAccessedAt,
            access_count = @accessCount,
            custom_properties_json = @customPropertiesJson
        """;

    private static void AddRecordParameters(SqliteCommand cmd, SqliteMemoryRecord record)
    {
        cmd.Parameters.AddWithValue("@key", record.Key);
        cmd.Parameters.AddWithValue("@itemId", record.ItemId);
        cmd.Parameters.AddWithValue("@content", record.Content);
        cmd.Parameters.AddWithValue("@embedding", (object?)SqliteMemoryRecord.EmbeddingToBytes(record.Embedding) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@importance", record.Importance);
        cmd.Parameters.AddWithValue("@source", record.Source);
        cmd.Parameters.AddWithValue("@relevance", record.Relevance);
        cmd.Parameters.AddWithValue("@tagsJson", (object?)record.TagsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", (object?)record.CreatedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastAccessedAt", (object?)record.LastAccessedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@accessCount", record.AccessCount);
        cmd.Parameters.AddWithValue("@customPropertiesJson", (object?)record.CustomPropertiesJson ?? DBNull.Value);
    }

    private static SqliteMemoryRecord ReadRecord(SqliteDataReader reader)
    {
        return new SqliteMemoryRecord
        {
            Key = reader.GetString(0),
            ItemId = reader.GetString(1),
            Content = reader.GetString(2),
            Embedding = reader.IsDBNull(3) ? null : SqliteMemoryRecord.BytesToEmbedding(reader.GetFieldValue<byte[]>(3)),
            Importance = reader.GetFloat(4),
            Source = reader.GetString(5),
            Relevance = reader.GetDouble(6),
            TagsJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            LastAccessedAt = reader.IsDBNull(10)
                ? null
                : DateTime.Parse(reader.GetString(10), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            AccessCount = reader.GetInt32(11),
            CustomPropertiesJson = reader.IsDBNull(12) ? null : reader.GetString(12)
        };
    }

    /// <summary>
    /// Disposes the SQLite connection and the internal gate.
    /// </summary>
    public void Dispose()
    {
        _connection.Dispose();
        _gate.Dispose();
    }
}
