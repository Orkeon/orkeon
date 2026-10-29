using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Serialization;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Cache;

/// <summary>
/// Saves the index of a root in one SQLite database, <c>&lt;root&gt;/.orkeon/cache/raggable-tree/index.db</c>, next to a
/// <c>.gitignore</c> that keeps the folder out of git. The nodes are stored by the file they come from, gzip-compressed
/// JSON, so a save after a refresh rewrites only the groups whose nodes changed; every save is one transaction.
/// The schema is described in <c>docs/architecture/raggable-tree.md</c>, "Index cache".
/// </summary>
public sealed class RaggableTreeCache : IRaggableTreeCache
{
    public const string CacheDirectory = ConventionalNames.StateDirectory + "/cache/raggable-tree";
    public const string DatabaseFileName = "index.db";

    // PRAGMA user_version of the database: another value drops the tables and creates them again.
    private const int SchemaVersion = 1;

    private const string CreateSchemaSql = """
        DROP TABLE IF EXISTS node_runs;
        DROP TABLE IF EXISTS node_groups;
        DROP TABLE IF EXISTS indexed_files;
        DROP TABLE IF EXISTS package_markers;
        DROP TABLE IF EXISTS cache_state;

        CREATE TABLE cache_state (
            id          INTEGER PRIMARY KEY CHECK (id = 1),
            cache_key   TEXT    NOT NULL,
            index_id    TEXT    NOT NULL,
            generation  INTEGER NOT NULL,
            node_count  INTEGER NOT NULL,
            edge_count  INTEGER NOT NULL,
            edges       BLOB    NOT NULL,
            saved_at    TEXT    NOT NULL
        );

        CREATE TABLE indexed_files (
            path    TEXT PRIMARY KEY,
            sha256  TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE package_markers (
            path    TEXT PRIMARY KEY,
            sha256  TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE node_groups (
            group_id        INTEGER PRIMARY KEY,
            virtual_path    TEXT    NOT NULL UNIQUE,
            node_count      INTEGER NOT NULL CHECK (node_count > 0),
            content_sha256  TEXT    NOT NULL,
            generation      INTEGER NOT NULL,
            nodes           BLOB    NOT NULL
        );

        CREATE TABLE node_runs (
            position    INTEGER PRIMARY KEY,
            group_id    INTEGER NOT NULL REFERENCES node_groups (group_id) ON DELETE RESTRICT,
            node_count  INTEGER NOT NULL CHECK (node_count > 0)
        );

        CREATE INDEX ix_node_runs_group_id ON node_runs (group_id);

        PRAGMA user_version = 1;
        """;

    private const string ClearSql = """
        DELETE FROM node_runs;
        DELETE FROM node_groups;
        DELETE FROM indexed_files;
        DELETE FROM package_markers;
        DELETE FROM cache_state;
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
                return await ReadEntryAsync(connection, cacheKey, ct).ConfigureAwait(false);
            }
        }
        catch (SqliteException) { return null; }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (FileAccessDeniedException) { return null; }
    }

    private static async Task<RaggableTreeCacheEntry?> ReadEntryAsync(SqliteConnection connection, string cacheKey, CancellationToken ct)
    {
        string indexId;
        List<RaggableEdge> edges;
        using (var state = connection.CreateCommand())
        {
            state.CommandText = "SELECT cache_key, index_id, edges FROM cache_state WHERE id = 1";
            var reader = await state.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
                if (!string.Equals(reader.GetString(0), cacheKey, StringComparison.Ordinal)) return null;
                indexId = reader.GetString(1);
                edges = [.. Decompress<List<EdgeDto>>(reader.GetStream(2)).Select(NodeMapper.ToModel)];
            }
        }

        var files = await ReadPairsAsync(connection, files: true, ct).ConfigureAwait(false);
        var markers = await ReadPairsAsync(connection, files: false, ct).ConfigureAwait(false);

        var groups = new Dictionary<long, List<RaggableNode>>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT group_id, nodes FROM node_groups";
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
            select.CommandText = "SELECT group_id, node_count FROM node_runs ORDER BY position";
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
        return new RaggableTreeCacheEntry(new RaggableTree(nodes, edges, index), indexId, cacheKey, files, markers);
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
                var generation = await NextGenerationAsync(connection, tx, entry.CacheKey, ct).ConfigureAwait(false);
                await ReplacePairsAsync(connection, tx, entry.Files, files: true, ct).ConfigureAwait(false);
                await ReplacePairsAsync(connection, tx, entry.Markers, files: false, ct).ConfigureAwait(false);
                await WriteNodesAsync(connection, tx, entry.Tree.Nodes, generation, ct).ConfigureAwait(false);
                await WriteStateAsync(connection, tx, entry, generation, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        }
    }

    // The generation of this save: one more than the saved one under the same key; under another key, everything
    // saved is dropped and the count starts again.
    private static async Task<long> NextGenerationAsync(SqliteConnection connection, SqliteTransaction tx, string cacheKey, CancellationToken ct)
    {
        using var select = Command(connection, tx);
        select.CommandText = "SELECT cache_key, generation FROM cache_state WHERE id = 1";
        string? savedKey = null;
        long generation = 0;
        var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                savedKey = reader.GetString(0);
                generation = reader.GetInt64(1);
            }
        }
        if (string.Equals(savedKey, cacheKey, StringComparison.Ordinal)) return generation + 1;

        using var clear = Command(connection, tx);
        clear.CommandText = ClearSql;
        await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return 1;
    }

    private static async Task ReplacePairsAsync(
        SqliteConnection connection, SqliteTransaction tx, IReadOnlyDictionary<string, string> pairs, bool files, CancellationToken ct)
    {
        using var delete = Command(connection, tx);
        if (files) delete.CommandText = "DELETE FROM indexed_files";
        else delete.CommandText = "DELETE FROM package_markers";
        await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        using var insert = Command(connection, tx);
        if (files) insert.CommandText = "INSERT INTO indexed_files (path, sha256) VALUES ($path, $sha256)";
        else insert.CommandText = "INSERT INTO package_markers (path, sha256) VALUES ($path, $sha256)";
        var path = insert.Parameters.Add("$path", SqliteType.Text);
        var sha = insert.Parameters.Add("$sha256", SqliteType.Text);
        foreach (var (key, value) in pairs)
        {
            path.Value = key;
            sha.Value = value;
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    // Groups the nodes by the file they come from, rewrites a group only when its content changed, and records the
    // runs that give the order of the nodes back.
    private static async Task WriteNodesAsync(
        SqliteConnection connection, SqliteTransaction tx, IReadOnlyList<RaggableNode> nodes, long generation, CancellationToken ct)
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
            select.CommandText = "SELECT group_id, virtual_path, content_sha256 FROM node_groups";
            var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    saved[reader.GetString(1)] = (reader.GetInt64(0), reader.GetString(2));
            }
        }

        using (var deleteRuns = Command(connection, tx))
        {
            deleteRuns.CommandText = "DELETE FROM node_runs";
            await deleteRuns.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var upsert = Command(connection, tx))
        {
            upsert.CommandText = """
                INSERT INTO node_groups (virtual_path, node_count, content_sha256, generation, nodes)
                VALUES ($path, $count, $sha256, $generation, $nodes)
                ON CONFLICT (virtual_path) DO UPDATE SET
                    node_count = excluded.node_count,
                    content_sha256 = excluded.content_sha256,
                    generation = excluded.generation,
                    nodes = excluded.nodes
                RETURNING group_id
                """;
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
            delete.CommandText = "DELETE FROM node_groups WHERE virtual_path = $path";
            var path = delete.Parameters.Add("$path", SqliteType.Text);
            foreach (var gone in saved.Keys.Where(p => !groups.ContainsKey(p)))
            {
                path.Value = gone;
                await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        using (var insert = Command(connection, tx))
        {
            insert.CommandText = "INSERT INTO node_runs (position, group_id, node_count) VALUES ($position, $group, $count)";
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

    private static async Task WriteStateAsync(
        SqliteConnection connection, SqliteTransaction tx, RaggableTreeCacheEntry entry, long generation, CancellationToken ct)
    {
        using var upsert = Command(connection, tx);
        upsert.CommandText = """
            INSERT OR REPLACE INTO cache_state (id, cache_key, index_id, generation, node_count, edge_count, edges, saved_at)
            VALUES (1, $key, $index_id, $generation, $node_count, $edge_count, $edges, $saved_at)
            """;
        upsert.Parameters.AddWithValue("$key", entry.CacheKey);
        upsert.Parameters.AddWithValue("$index_id", entry.IndexId);
        upsert.Parameters.AddWithValue("$generation", generation);
        upsert.Parameters.AddWithValue("$node_count", entry.Tree.Nodes.Count);
        upsert.Parameters.AddWithValue("$edge_count", entry.Tree.Edges.Count);
        upsert.Parameters.Add("$edges", SqliteType.Blob).Value = Compress(
            JsonSerializer.SerializeToUtf8Bytes(entry.Tree.Edges.Select(NodeMapper.ToDto).ToList(), RaggableTreeSerializer.Options));
        upsert.Parameters.AddWithValue("$saved_at", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

    private static async Task<Dictionary<string, string>> ReadPairsAsync(SqliteConnection connection, bool files, CancellationToken ct)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        using var select = connection.CreateCommand();
        if (files) select.CommandText = "SELECT path, sha256 FROM indexed_files";
        else select.CommandText = "SELECT path, sha256 FROM package_markers";
        var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                pairs[reader.GetString(0)] = reader.GetString(1);
        }
        return pairs;
    }

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
