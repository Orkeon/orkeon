using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Session;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// File-backed <see cref="ICategoryMemoryStore"/>: one <c>&lt;RootDirectory&gt;/&lt;project slug&gt;/memory.json</c> per project,
/// the project being the physical root mounted as <c>/workspace</c>, slugged by <see cref="ProjectKey.Slug"/> exactly as the
/// session file is. The file is read once, at the first access, and written after every mutation; a missing or empty file is
/// an empty store. The next id is persisted with the entries and only grows: an id is never reused after a delete.
/// </summary>
public sealed class FileCategoryMemoryStore : ICategoryMemoryStore
{
    private const string FileName = "memory.json";
    private const string WorkspaceRoot = "/workspace";

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IFileSystemService _fileSystem;
    private readonly string _rootDirectory;
    // A property, not a field: the store is a DI singleton nobody disposes (CA1001), as the agent memory store's documents are.
    private SemaphoreSlim Gate { get; } = new(1, 1);
    private string? _path;
    private List<MemoryEntry>? _entries;
    private int _nextId = 1;

    /// <summary>Creates the store over a file system and the virtual root folder of its files.</summary>
    public FileCategoryMemoryStore(IFileSystemService fileSystem, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _fileSystem = fileSystem;
        _rootDirectory = rootDirectory.TrimEnd('/');
    }

    /// <summary>The file the entries are kept in: <c>&lt;RootDirectory&gt;/&lt;project slug&gt;/memory.json</c>.</summary>
    public string FilePath => _path ??= ResolvePath();

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(string? category, bool newestFirst, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            return CategoryMemoryEntries.Select(entries, category, newestFirst);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> AddAsync(string category, string content, CancellationToken ct)
    {
        var trimmed = CategoryMemoryEntries.RequireContent(content);
        var normalized = CategoryMemoryEntries.NormalizeCategory(category);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            var id = _nextId;
            var next = new List<MemoryEntry>(entries)
            {
                new() { Id = id, Category = normalized, Content = trimmed, CreatedAt = CategoryMemoryEntries.Now() },
            };
            await WriteAsync(next, id + 1, ct).ConfigureAwait(false);
            return id;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceAsync(int id, string content, CancellationToken ct)
    {
        var trimmed = CategoryMemoryEntries.RequireContent(content);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            var index = entries.FindIndex(e => e.Id == id);
            if (index < 0) return false;
            var next = new List<MemoryEntry>(entries);
            next[index] = entries[index] with { Content = trimmed, CreatedAt = CategoryMemoryEntries.Now() };
            await WriteAsync(next, _nextId, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            var next = entries.Where(e => e.Id != id).ToList();
            if (next.Count == entries.Count) return false;
            await WriteAsync(next, _nextId, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<MemoryEntry?> GetAsync(int id, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(ct).ConfigureAwait(false);
            return entries.FirstOrDefault(e => e.Id == id);
        }
        finally
        {
            Gate.Release();
        }
    }

    // The project key is the physical root the host mounted as /workspace, as the REPL hands it to the session
    // buffer (not the virtual "/workspace", identical for every project); the path holds only its name and hash.
    private string ResolvePath()
    {
        var workspace = _fileSystem.ResolveAndValidate(WorkspaceRoot, FileAccessRights.Read);
        var projectKey = workspace.IsAllowed && !string.IsNullOrWhiteSpace(workspace.ResolvedPath)
            ? workspace.ResolvedPath
            : WorkspaceRoot;
        return $"{_rootDirectory}/{ProjectKey.Slug(projectKey)}/{FileName}";
    }

    private async Task<List<MemoryEntry>> LoadAsync(CancellationToken ct)
    {
        if (_entries is { } loaded) return loaded;
        var json = await _fileSystem.TryReadAllTextAsync(FilePath, ct).ConfigureAwait(false);
        var entries = new List<MemoryEntry>();
        var nextId = 1;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var document = JsonSerializer.Deserialize<FileDocument>(json, s_json);
                if (document?.Entries is { } persisted) entries = persisted.ToList();
                nextId = Math.Max(document?.NextId ?? 1, entries.Count == 0 ? 1 : entries.Max(e => e.Id) + 1);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"The memory file '{FilePath}' is unreadable; it is left untouched (its previous version, if any, is '{FilePath}.bak').", ex);
            }
        }

        _entries = entries;
        _nextId = nextId;
        return entries;
    }

    // The previous file is copied aside (the name does not end in .json) and put back when the write fails.
    private async Task WriteAsync(List<MemoryEntry> next, int nextId, CancellationToken ct)
    {
        var path = FilePath;
        var backup = path + ".bak";
        var hadFile = await _fileSystem.ExistsAsync(path, ct).ConfigureAwait(false);
        if (hadFile) await _fileSystem.CopyAsync(path, backup, true, ct).ConfigureAwait(false);
        try
        {
            var document = new FileDocument { NextId = nextId, Entries = next };
            await _fileSystem.WriteAllTextAsync(path, JsonSerializer.Serialize(document, s_json), ct).ConfigureAwait(false);
        }
        catch
        {
            if (hadFile) await _fileSystem.CopyAsync(backup, path, true, CancellationToken.None).ConfigureAwait(false);
            else await _fileSystem.DeleteAsync(path, false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _entries = next;
        _nextId = nextId;
    }

    private sealed class FileDocument
    {
        [JsonPropertyName("next_id")]
        public int NextId { get; set; } = 1;

        [JsonPropertyName("entries")]
        public List<MemoryEntry>? Entries { get; set; }
    }
}
