using System.Collections.Concurrent;
using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.Session;

/// <summary>File-backed <see cref="IScriptAgentMemoryStore"/>: one JSON file per project and agent.</summary>
public sealed class FileScriptAgentMemoryStore : IScriptAgentMemoryStore
{
    // Folder of a crew without a name: FileName only yields lower-case letters, digits, - and _, so no crew name can map to it.
    private const string NoCrewFolder = "~none";

    private readonly IFileSystemService _fileSystem;
    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, FileDocument> _documents = new(StringComparer.Ordinal);

    /// <summary>Creates the store over a file system and the virtual root folder of its files.</summary>
    public FileScriptAgentMemoryStore(IFileSystemService fileSystem, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _fileSystem = fileSystem;
        _rootDirectory = rootDirectory.TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task StoreAsync(string agentKey, string key, string valueJson, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueJson);
        if (key.Contains('\n', StringComparison.Ordinal) || key.Contains('\r', StringComparison.Ordinal))
            throw new ArgumentException("A memory key cannot hold a newline.", nameof(key));
        using var parsed = JsonDocument.Parse(valueJson);
        var value = parsed.RootElement.Clone();
        var document = DocumentFor(agentKey);
        await document.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(document, ct).ConfigureAwait(false);
            var next = new Dictionary<string, JsonElement>(entries, StringComparer.Ordinal) { [key] = value };
            await WriteAsync(document, next, ct).ConfigureAwait(false);
        }
        finally
        {
            document.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string agentKey, string key, CancellationToken ct)
    {
        var document = DocumentFor(agentKey);
        await document.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(document, ct).ConfigureAwait(false);
            return entries.TryGetValue(key, out var value) ? value.GetRawText() : null;
        }
        finally
        {
            document.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScriptAgentMemoryHit>> SearchAsync(string agentKey, string query, int limit, CancellationToken ct)
    {
        var document = DocumentFor(agentKey);
        await document.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(document, ct).ConfigureAwait(false);
            return entries
                .Select(e => new ScriptAgentMemoryHit { Key = e.Key, ValueJson = e.Value.GetRawText() })
                .Where(h => string.IsNullOrWhiteSpace(query)
                    || h.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || h.ValueJson.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit <= 0 ? 5 : limit)
                .ToList();
        }
        finally
        {
            document.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string agentKey, string key, CancellationToken ct)
    {
        var document = DocumentFor(agentKey);
        await document.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadAsync(document, ct).ConfigureAwait(false);
            if (!entries.ContainsKey(key)) return false;
            var next = new Dictionary<string, JsonElement>(entries, StringComparer.Ordinal);
            next.Remove(key);
            await WriteAsync(document, next, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            document.Gate.Release();
        }
    }

    private FileDocument DocumentFor(string agentKey)
    {
        var key = ScriptAgentMemoryKey.Parse(agentKey);
        var path = $"{_rootDirectory}/{ProjectKey.Slug(key.Project)}/{(key.Crew is null ? NoCrewFolder : FileName(key.Crew))}/{FileName(key.Agent)}.json";
        return _documents.GetOrAdd(path, p => new FileDocument(p));
    }

    // A plain lower-case name is its own file name; any other name becomes <name>-<hash of the whole name>.
    private static string FileName(string agentName)
        => agentName.Length <= 64 && agentName.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_')
            ? agentName
            : ProjectKey.Slug(agentName);

    private async Task<IReadOnlyDictionary<string, JsonElement>> LoadAsync(FileDocument document, CancellationToken ct)
    {
        if (document.Entries is { } loaded) return loaded;
        var json = await _fileSystem.TryReadAllTextAsync(document.Path, ct).ConfigureAwait(false);
        var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (parsed is not null) entries = new Dictionary<string, JsonElement>(parsed, StringComparer.Ordinal);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"The agent memory file '{document.Path}' is unreadable; it is left untouched (its previous version, if any, is '{document.Path}.bak').", ex);
            }
        }

        document.Entries = entries;
        return entries;
    }

    // The previous file is copied aside (the name does not end in .json) and put back when the write fails.
    private async Task WriteAsync(FileDocument document, Dictionary<string, JsonElement> next, CancellationToken ct)
    {
        var backup = document.Path + ".bak";
        var hadFile = await _fileSystem.ExistsAsync(document.Path, ct).ConfigureAwait(false);
        if (hadFile) await _fileSystem.CopyAsync(document.Path, backup, true, ct).ConfigureAwait(false);
        try
        {
            await _fileSystem.WriteAllTextAsync(document.Path, JsonSerializer.Serialize(next), ct).ConfigureAwait(false);
        }
        catch
        {
            if (hadFile) await _fileSystem.CopyAsync(backup, document.Path, true, CancellationToken.None).ConfigureAwait(false);
            else await _fileSystem.DeleteAsync(document.Path, false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        document.Entries = next;
    }

    private sealed class FileDocument(string path)
    {
        public string Path { get; } = path;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IReadOnlyDictionary<string, JsonElement>? Entries { get; set; }
    }
}
