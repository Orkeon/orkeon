using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Serialization;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Cache;

/// <summary>
/// Saves the indexes of a root in one SQLite database, <c>&lt;root&gt;/.orkeon/cache/raggable-tree/index.db</c>, next to
/// a <c>.gitignore</c> that keeps the folder out of git. The database keeps one entry per cache key, at most
/// <see cref="MaxEntries"/>: a save past it drops the entries least recently used. The nodes of an entry are stored by
/// the file they come from, gzip-compressed JSON, so a save after a refresh rewrites only the groups whose nodes
/// changed; every save is one transaction. The schema is described in <c>docs/architecture/raggable-tree.md</c>,
/// "Index cache".
/// </summary>
public sealed class RaggableTreeCache : IRaggableTreeCache
{
    public const string CacheDirectory = ConventionalNames.StateDirectory + "/cache/raggable-tree";
    public const string DatabaseFileName = "index.db";

    /// <summary>How many keys one database keeps; a save past it drops the entries least recently used.</summary>
    public const int MaxEntries = 3;

    // PRAGMA user_version of the database, also written at the end of CreateSchemaSql: another value drops the tables and creates them again.
    private const int SchemaVersion = 3;

    private const string CreateSchemaSql = """
        DROP TABLE IF EXISTS node_runs;
        DROP TABLE IF EXISTS node_groups;
        DROP TABLE IF EXISTS indexed_files;
        DROP TABLE IF EXISTS package_markers;
        DROP TABLE IF EXISTS cache_state;
        DROP TABLE IF EXISTS cache_entries;

        CREATE TABLE cache_entries (
            entry_id    INTEGER PRIMARY KEY,
            cache_key   TEXT    NOT NULL UNIQUE,
            runtime     TEXT    NOT NULL,
            index_id    TEXT    NOT NULL,
            generation  INTEGER NOT NULL CHECK (generation > 0),
            node_count  INTEGER NOT NULL,
            edge_count  INTEGER NOT NULL,
            edges       BLOB    NOT NULL,
            saved_at    TEXT    NOT NULL,
            used_at     TEXT    NOT NULL
        );

        CREATE TABLE indexed_files (
            entry_id        INTEGER NOT NULL REFERENCES cache_entries (entry_id) ON DELETE CASCADE,
            path            TEXT    NOT NULL,
            sha256          TEXT    NOT NULL,
            size_bytes      INTEGER NOT NULL,
            modified_ticks  INTEGER,
            PRIMARY KEY (entry_id, path)
        ) WITHOUT ROWID;

        CREATE TABLE package_markers (
            entry_id    INTEGER NOT NULL REFERENCES cache_entries (entry_id) ON DELETE CASCADE,
            path        TEXT    NOT NULL,
            sha256      TEXT    NOT NULL,
            PRIMARY KEY (entry_id, path)
        ) WITHOUT ROWID;

        CREATE TABLE node_groups (
            group_id        INTEGER PRIMARY KEY,
            entry_id        INTEGER NOT NULL REFERENCES cache_entries (entry_id) ON DELETE CASCADE,
            virtual_path    TEXT    NOT NULL,
            node_count      INTEGER NOT NULL CHECK (node_count > 0),
            content_sha256  TEXT    NOT NULL,
            generation      INTEGER NOT NULL,
            nodes           BLOB    NOT NULL,
            UNIQUE (entry_id, virtual_path),
            UNIQUE (entry_id, group_id)
        );

        CREATE TABLE node_runs (
            entry_id    INTEGER NOT NULL REFERENCES cache_entries (entry_id) ON DELETE CASCADE,
            position    INTEGER NOT NULL,
            group_id    INTEGER NOT NULL,
            node_count  INTEGER NOT NULL CHECK (node_count > 0),
            PRIMARY KEY (entry_id, position),
            FOREIGN KEY (entry_id, group_id) REFERENCES node_groups (entry_id, group_id)
        ) WITHOUT ROWID;

        CREATE INDEX ix_node_runs_group ON node_runs (entry_id, group_id);

        PRAGMA user_version = 3;
        """;

    private readonly IFileSystemService _fs;

    public RaggableTreeCache(IFileSystemService fileSystem)
    {
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public Task<RaggableTreeCacheEntry?> LoadAsync(string rootPath, string cacheKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        return LoadCoreAsync(rootPath, cacheKey, ct);
    }

    private async Task<RaggableTreeCacheEntry?> LoadCoreAsync(string rootPath, string cacheKey, CancellationToken ct)
    {
        var paths = PathsOf(rootPath);
        try
        {
            if (!await _fs.ExistsAsync(paths.Database, ct).ConfigureAwait(false)) return null;
            var physical = Resolve(paths.Database, FileAccessRights.Read | FileAccessRights.Write);
            if (physical is null) return null;

            var connection = await OpenAsync(physical, SqliteOpenMode.ReadWrite, ct).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                if (await SchemaVersionOfAsync(connection, ct).ConfigureAwait(false) != SchemaVersion) return null;
                var loaded = await ReadEntryAsync(connection, cacheKey, ct).ConfigureAwait(false);
                if (loaded is null) return null;
                await TouchAsync(connection, loaded.Value.EntryId, ct).ConfigureAwait(false);
                return loaded.Value.Entry;
            }
        }
        catch (SqliteException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (FileAccessDeniedException) { return null; }
    }

    private static async Task<(long EntryId, RaggableTreeCacheEntry Entry)?> ReadEntryAsync(
        SqliteConnection connection, string cacheKey, CancellationToken ct)
    {
        long entryId;
        string indexId;
        string runtime;
        List<RaggableEdge> edges;
        using (var state = connection.CreateCommand())
        {
            state.CommandText = "SELECT entry_id, index_id, edges, runtime FROM cache_entries WHERE cache_key = $key";
            state.Parameters.AddWithValue("$key", cacheKey);
            var reader = await state.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
                entryId = reader.GetInt64(0);
                indexId = reader.GetString(1);
                runtime = reader.GetString(3);
                edges = [.. Decompress<List<EdgeDto>>(reader.GetStream(2)).Select(NodeMapper.ToModel)];
            }
        }

        var files = await ReadFilesAsync(connection, entryId, ct).ConfigureAwait(false);
        var markers = await ReadMarkersAsync(connection, entryId, ct).ConfigureAwait(false);

        var groups = new Dictionary<long, List<RaggableNode>>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT group_id, nodes FROM node_groups WHERE entry_id = $entry";
            select.Parameters.AddWithValue("$entry", entryId);
            var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    groups[reader.GetInt64(0)] = [.. Decompress<List<NodeDto>>(reader.GetStream(1)).Select(NodeMapper.ToModel)];
            }
        }

        // The runs give the tree's node order back: each takes the next node_count nodes of its group.
        var nodes = new List<RaggableNode>(groups.Values.Sum(g => g.Count));
        var taken = new Dictionary<long, int>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT group_id, node_count FROM node_runs WHERE entry_id = $entry ORDER BY position";
            select.Parameters.AddWithValue("$entry", entryId);
            var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var groupId = reader.GetInt64(0);
                    var count = reader.GetInt32(1);
                    if (!groups.TryGetValue(groupId, out var group)) return null;
                    var start = taken.GetValueOrDefault(groupId);
                    if (start + count > group.Count) return null;
                    nodes.AddRange(group.GetRange(start, count));
                    taken[groupId] = start + count;
                }
            }
        }
        if (groups.Any(g => taken.GetValueOrDefault(g.Key) != g.Value.Count)) return null;

        var index = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.Fqn)) index.TryAdd(node.Fqn, node);
        }
        return (entryId, new RaggableTreeCacheEntry(new RaggableTree(nodes, edges, index), indexId, cacheKey, files, markers) { Runtime = runtime });
    }

    // Marks the entry used now, so the eviction keeps it. A busy database leaves the mark as it was.
    private static async Task TouchAsync(SqliteConnection connection, long entryId, CancellationToken ct)
    {
        try
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE cache_entries SET used_at = $now WHERE entry_id = $entry";
            update.Parameters.AddWithValue("$now", Now());
            update.Parameters.AddWithValue("$entry", entryId);
            await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException) { /* the mark is best-effort: the entry read back is still valid */ }
    }

    public Task<bool> SaveAsync(string rootPath, RaggableTreeCacheEntry entry, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentNullException.ThrowIfNull(entry);
        return SaveCoreAsync(rootPath, entry, ct);
    }

    private async Task<bool> SaveCoreAsync(string rootPath, RaggableTreeCacheEntry entry, CancellationToken ct)
    {
        var paths = PathsOf(rootPath);
        try
        {
            await _fs.CreateDirectoryAsync(paths.Directory, ct).ConfigureAwait(false);
            await _fs.WriteAllTextAsync(paths.GitIgnore, "*\n", ct).ConfigureAwait(false);
            var physical = Resolve(paths.Database, FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create);
            if (physical is null) return false;

            try
            {
                await WriteEntryAsync(physical, entry, ct).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // A database that is not one, or is damaged, is deleted and written again once.
                await DeleteDatabaseAsync(paths, ct).ConfigureAwait(false);
                await WriteEntryAsync(physical, entry, ct).ConfigureAwait(false);
            }
            return true;
        }
        catch (SqliteException) { return false; }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (FileAccessDeniedException) { return false; }
    }

    private static async Task WriteEntryAsync(string physical, RaggableTreeCacheEntry entry, CancellationToken ct)
    {
        var connection = await OpenAsync(physical, SqliteOpenMode.ReadWriteCreate, ct).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode = WAL";
                await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            if (await SchemaVersionOfAsync(connection, ct).ConfigureAwait(false) != SchemaVersion)
            {
                using var create = connection.CreateCommand();
                create.CommandText = CreateSchemaSql;
                await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var (entryId, generation) = await WriteStateAsync(connection, tx, entry, ct).ConfigureAwait(false);
                await ReplaceFilesAsync(connection, tx, entryId, entry.Files, ct).ConfigureAwait(false);
                await ReplaceMarkersAsync(connection, tx, entryId, entry.Markers, ct).ConfigureAwait(false);
                await WriteNodesAsync(connection, tx, entryId, entry.Tree.Nodes, generation, ct).ConfigureAwait(false);
                await EvictAsync(connection, tx, entry.Runtime, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            await CompactAsync(connection, ct).ConfigureAwait(false);
        }
    }

    // Writes the row of the key: one more generation than the saved one under it, or a new entry at generation 1.
    private static async Task<(long EntryId, long Generation)> WriteStateAsync(
        SqliteConnection connection, SqliteTransaction tx, RaggableTreeCacheEntry entry, CancellationToken ct)
    {
        using var upsert = Command(connection, tx);
        upsert.CommandText = """
            INSERT INTO cache_entries (cache_key, runtime, index_id, generation, node_count, edge_count, edges, saved_at, used_at)
            VALUES ($key, $runtime, $index_id, 1, $node_count, $edge_count, $edges, $now, $now)
            ON CONFLICT (cache_key) DO UPDATE SET
                runtime = excluded.runtime,
                index_id = excluded.index_id,
                generation = generation + 1,
                node_count = excluded.node_count,
                edge_count = excluded.edge_count,
                edges = excluded.edges,
                saved_at = excluded.saved_at,
                used_at = excluded.used_at
            RETURNING entry_id, generation
            """;
        upsert.Parameters.AddWithValue("$key", entry.CacheKey);
        upsert.Parameters.AddWithValue("$runtime", entry.Runtime);
        upsert.Parameters.AddWithValue("$index_id", entry.IndexId);
        upsert.Parameters.AddWithValue("$node_count", entry.Tree.Nodes.Count);
        upsert.Parameters.AddWithValue("$edge_count", entry.Tree.Edges.Count);
        upsert.Parameters.Add("$edges", SqliteType.Blob).Value = Compress(
            JsonSerializer.SerializeToUtf8Bytes(entry.Tree.Edges.Select(NodeMapper.ToDto).ToList(), RaggableTreeSerializer.Options));
        upsert.Parameters.AddWithValue("$now", Now());
        var reader = await upsert.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            await reader.ReadAsync(ct).ConfigureAwait(false);
            return (reader.GetInt64(0), reader.GetInt64(1));
        }
    }

    private static async Task ReplaceFilesAsync(
        SqliteConnection connection, SqliteTransaction tx, long entryId, IReadOnlyDictionary<string, FileStamp> files, CancellationToken ct)
    {
        using (var delete = Command(connection, tx))
        {
            delete.CommandText = "DELETE FROM indexed_files WHERE entry_id = $entry";
            delete.Parameters.AddWithValue("$entry", entryId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        using var insert = Command(connection, tx);
        insert.CommandText = """
            INSERT INTO indexed_files (entry_id, path, sha256, size_bytes, modified_ticks)
            VALUES ($entry, $path, $sha256, $size, $ticks)
            """;
        insert.Parameters.AddWithValue("$entry", entryId);
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var sha = insert.Parameters.Add("$sha256", SqliteType.Text);
        var size = insert.Parameters.Add("$size", SqliteType.Integer);
        var ticks = insert.Parameters.Add("$ticks", SqliteType.Integer);
        foreach (var (key, stamp) in files)
        {
            path.Value = key;
            sha.Value = stamp.Sha256;
            size.Value = stamp.SizeBytes;
            ticks.Value = stamp.LastModified is { } modified ? modified.UtcTicks : DBNull.Value;
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceMarkersAsync(
        SqliteConnection connection, SqliteTransaction tx, long entryId, IReadOnlyDictionary<string, string> markers, CancellationToken ct)
    {
        using (var delete = Command(connection, tx))
        {
            delete.CommandText = "DELETE FROM package_markers WHERE entry_id = $entry";
            delete.Parameters.AddWithValue("$entry", entryId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        using var insert = Command(connection, tx);
        insert.CommandText = "INSERT INTO package_markers (entry_id, path, sha256) VALUES ($entry, $path, $sha256)";
        insert.Parameters.AddWithValue("$entry", entryId);
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var sha = insert.Parameters.Add("$sha256", SqliteType.Text);
        foreach (var (key, value) in markers)
        {
            path.Value = key;
            sha.Value = value;
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    // Groups the nodes by the file they come from, rewrites a group only when its content changed, and records the
    // runs that give the order of the nodes back.
    private static async Task WriteNodesAsync(
        SqliteConnection connection, SqliteTransaction tx, long entryId, IReadOnlyList<RaggableNode> nodes, long generation, CancellationToken ct)
    {
        var groups = new Dictionary<string, List<RaggableNode>>(StringComparer.Ordinal);
        var runs = new List<(string Path, int Count)>();
        foreach (var node in nodes)
        {
            var path = node.VirtualFilePath ?? string.Empty;
            if (!groups.TryGetValue(path, out var group)) groups[path] = group = [];
            group.Add(node);
            if (runs.Count > 0 && string.Equals(runs[^1].Path, path, StringComparison.Ordinal))
                runs[^1] = (path, runs[^1].Count + 1);
            else
                runs.Add((path, 1));
        }

        var saved = new Dictionary<string, (long Id, string Sha)>(StringComparer.Ordinal);
        using (var select = Command(connection, tx))
        {
            select.CommandText = "SELECT group_id, virtual_path, content_sha256 FROM node_groups WHERE entry_id = $entry";
            select.Parameters.AddWithValue("$entry", entryId);
            var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    saved[reader.GetString(1)] = (reader.GetInt64(0), reader.GetString(2));
            }
        }

        using (var deleteRuns = Command(connection, tx))
        {
            deleteRuns.CommandText = "DELETE FROM node_runs WHERE entry_id = $entry";
            deleteRuns.Parameters.AddWithValue("$entry", entryId);
            await deleteRuns.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var upsert = Command(connection, tx))
        {
            upsert.CommandText = """
                INSERT INTO node_groups (entry_id, virtual_path, node_count, content_sha256, generation, nodes)
                VALUES ($entry, $path, $count, $sha256, $generation, $nodes)
                ON CONFLICT (entry_id, virtual_path) DO UPDATE SET
                    node_count = excluded.node_count,
                    content_sha256 = excluded.content_sha256,
                    generation = excluded.generation,
                    nodes = excluded.nodes
                RETURNING group_id
                """;
            upsert.Parameters.AddWithValue("$entry", entryId);
            var path = upsert.Parameters.Add("$path", SqliteType.Text);
            var count = upsert.Parameters.Add("$count", SqliteType.Integer);
            var sha = upsert.Parameters.Add("$sha256", SqliteType.Text);
            upsert.Parameters.AddWithValue("$generation", generation);
            var blob = upsert.Parameters.Add("$nodes", SqliteType.Blob);
            foreach (var (groupPath, group) in groups)
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(group.Select(NodeMapper.ToDto).ToList(), RaggableTreeSerializer.Options);
                var contentSha = Convert.ToHexStringLower(SHA256.HashData(json));
                if (saved.TryGetValue(groupPath, out var existing) && string.Equals(existing.Sha, contentSha, StringComparison.Ordinal))
                {
                    ids[groupPath] = existing.Id;
                    continue;
                }
                path.Value = groupPath;
                count.Value = group.Count;
                sha.Value = contentSha;
                blob.Value = Compress(json);
                ids[groupPath] = (long)(await upsert.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }
        }

        using (var delete = Command(connection, tx))
        {
            delete.CommandText = "DELETE FROM node_groups WHERE entry_id = $entry AND virtual_path = $path";
            delete.Parameters.AddWithValue("$entry", entryId);
            var path = delete.Parameters.Add("$path", SqliteType.Text);
            foreach (var gone in saved.Keys.Where(p => !groups.ContainsKey(p)))
            {
                path.Value = gone;
                await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        using (var insert = Command(connection, tx))
        {
            insert.CommandText = "INSERT INTO node_runs (entry_id, position, group_id, node_count) VALUES ($entry, $position, $group, $count)";
            insert.Parameters.AddWithValue("$entry", entryId);
            var position = insert.Parameters.Add("$position", SqliteType.Integer);
            var group = insert.Parameters.Add("$group", SqliteType.Integer);
            var count = insert.Parameters.Add("$count", SqliteType.Integer);
            for (var i = 0; i < runs.Count; i++)
            {
                position.Value = i;
                group.Value = ids[runs[i].Path];
                count.Value = runs[i].Count;
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    // Drops the entries of another runtime, which no key of this one can hit (#248), then keeps the MaxEntries
    // entries used last; deleting an entry cascades to its files, markers, groups and runs.
    private static async Task EvictAsync(SqliteConnection connection, SqliteTransaction tx, string runtime, CancellationToken ct)
    {
        using var delete = Command(connection, tx);
        delete.CommandText = """
            DELETE FROM cache_entries WHERE runtime <> $runtime;
            DELETE FROM cache_entries WHERE entry_id NOT IN (
                SELECT entry_id FROM cache_entries ORDER BY used_at DESC, entry_id DESC LIMIT $max);
            """;
        delete.Parameters.AddWithValue("$runtime", runtime);
        delete.Parameters.AddWithValue("$max", MaxEntries);
        await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // A file never shrinks on its own: once more than half of its pages are free (an eviction of a large entry),
    // VACUUM gives them back to the disk. Below that the next save reuses them. Best-effort: the entry is already
    // committed, and a failed VACUUM (a busy database, no room for its copy) must not reach the save's catch, which
    // deletes the database.
    private static async Task CompactAsync(SqliteConnection connection, CancellationToken ct)
    {
        try
        {
            using var pages = connection.CreateCommand();
            pages.CommandText = "SELECT (SELECT freelist_count FROM pragma_freelist_count) * 2 > (SELECT page_count FROM pragma_page_count)";
            if (Convert.ToInt64(await pages.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
                return;
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM";
            await vacuum.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException) { /* the next save that finds the pages free tries again */ }
    }

    public async Task InvalidateAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        await DeleteDatabaseAsync(PathsOf(rootPath), ct).ConfigureAwait(false);
    }

    private static async Task<SqliteConnection> OpenAsync(string physical, SqliteOpenMode mode, CancellationToken ct)
    {
        // No pooling: the file is closed with the connection, so it can be deleted or replaced at once.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = physical,
            Mode = mode,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 30,
        }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<long> SchemaVersionOfAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA user_version";
        return (long)(await pragma.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static async Task<Dictionary<string, FileStamp>> ReadFilesAsync(SqliteConnection connection, long entryId, CancellationToken ct)
    {
        var files = new Dictionary<string, FileStamp>(StringComparer.Ordinal);
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT path, sha256, size_bytes, modified_ticks FROM indexed_files WHERE entry_id = $entry";
        select.Parameters.AddWithValue("$entry", entryId);
        var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                DateTimeOffset? modified = await reader.IsDBNullAsync(3, ct).ConfigureAwait(false) ? null : new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero);
                files[reader.GetString(0)] = new FileStamp(reader.GetString(1), reader.GetInt64(2), modified);
            }
        }
        return files;
    }

    private static async Task<Dictionary<string, string>> ReadMarkersAsync(SqliteConnection connection, long entryId, CancellationToken ct)
    {
        var markers = new Dictionary<string, string>(StringComparer.Ordinal);
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT path, sha256 FROM package_markers WHERE entry_id = $entry";
        select.Parameters.AddWithValue("$entry", entryId);
        var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                markers[reader.GetString(0)] = reader.GetString(1);
        }
        return markers;
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction tx)
    {
        var command = connection.CreateCommand();
        command.Transaction = tx;
        return command;
    }

    private static byte[] Compress(byte[] json)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(json);
        }
        return buffer.ToArray();
    }

    private static T Decompress<T>(Stream blob)
    {
        using (blob)
        using (var gzip = new GZipStream(blob, CompressionMode.Decompress))
        {
            return JsonSerializer.Deserialize<T>(gzip, RaggableTreeSerializer.Options)
                ?? throw new InvalidDataException("A cached blob holds no value.");
        }
    }

    private string? Resolve(string virtualPath, FileAccessRights rights)
    {
        var resolution = _fs.ResolveAndValidate(virtualPath, rights);
        return resolution.IsAllowed ? resolution.ResolvedPath : null;
    }

    private async Task DeleteDatabaseAsync(CachePaths paths, CancellationToken ct)
    {
        foreach (var path in new[] { paths.Database, $"{paths.Database}-wal", $"{paths.Database}-shm" })
        {
            try
            {
                if (await _fs.ExistsAsync(path, ct).ConfigureAwait(false))
                    await _fs.DeleteAsync(path, recursive: false, ct).ConfigureAwait(false);
            }
            catch (IOException) { /* best-effort delete: missing or locked file is ignored */ }
            catch (UnauthorizedAccessException) { /* best-effort delete: insufficient rights is ignored */ }
            catch (FileAccessDeniedException) { /* best-effort delete: a read-only mount is ignored */ }
        }
    }

    private static CachePaths PathsOf(string virtualRoot)
    {
        var dir = $"{virtualRoot.TrimEnd('/', '\\')}/{CacheDirectory}";
        return new CachePaths(dir, $"{dir}/{DatabaseFileName}", $"{dir}/.gitignore");
    }

    private readonly record struct CachePaths(string Directory, string Database, string GitIgnore);
}
