using System.Security.Cryptography;
using System.Text.Json;
using Orkeon.Analysis.Core.Serialization;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Cache;

/// <summary>
/// Saves the index of a root under <c>&lt;root&gt;/.orkeon/cache/raggable-tree/</c>: the tree, a manifest naming its
/// key and hashes, and a <c>.gitignore</c> that keeps the folder out of git.
/// </summary>
public sealed class RaggableTreeCache : IRaggableTreeCache
{
    public const string CacheDirectory = ConventionalNames.StateDirectory + "/cache/raggable-tree";
    public const string TreeFileName = "tree.json";
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions s_manifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IRaggableTreeSerializer _serializer;
    private readonly IFileSystemService _fs;

    public RaggableTreeCache(IRaggableTreeSerializer serializer, IFileSystemService fileSystem)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
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
            var json = await _fs.TryReadAllTextAsync(paths.Manifest, ct).ConfigureAwait(false);
            if (json is null) return null;
            var manifest = JsonSerializer.Deserialize<CacheManifest>(json, s_manifestOptions);
            if (manifest is null || !string.Equals(manifest.CacheKey, cacheKey, StringComparison.Ordinal)) return null;
            if (!await _fs.ExistsAsync(paths.Tree, ct).ConfigureAwait(false)) return null;

            using var buffer = new MemoryStream();
            var stream = await _fs.OpenReadStreamAsync(paths.Tree, ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            }
            if (!string.Equals(Sha256Of(buffer), manifest.TreeSha256, StringComparison.Ordinal)) return null;

            buffer.Position = 0;
            var snapshot = await _serializer.DeserializeAsync(buffer, ct).ConfigureAwait(false);
            if (snapshot is null || !string.Equals(snapshot.IndexId, manifest.IndexId, StringComparison.Ordinal)) return null;
            return new RaggableTreeCacheEntry(snapshot.Tree, snapshot.IndexId, manifest.CacheKey, manifest.Files, manifest.Markers);
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (FileAccessDeniedException) { return null; }
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
            using var buffer = new MemoryStream();
            await _serializer.SerializeAsync(entry.Tree, entry.IndexId, buffer, ct).ConfigureAwait(false);
            var manifest = new CacheManifest
            {
                CacheKey = entry.CacheKey,
                IndexId = entry.IndexId,
                TreeSha256 = Sha256Of(buffer),
                Files = new Dictionary<string, string>(entry.Files, StringComparer.Ordinal),
                Markers = new Dictionary<string, string>(entry.Markers, StringComparer.Ordinal),
            };

            await _fs.CreateDirectoryAsync(paths.Directory, ct).ConfigureAwait(false);
            await _fs.WriteAllTextAsync(paths.GitIgnore, "*\n", ct).ConfigureAwait(false);
            await _fs.WriteAllBytesAsync(paths.Tree, buffer.ToArray(), ct).ConfigureAwait(false);
            await _fs.WriteAllTextAsync(paths.Manifest, JsonSerializer.Serialize(manifest, s_manifestOptions), ct).ConfigureAwait(false);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (FileAccessDeniedException) { return false; }
    }

    public async Task InvalidateAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        var paths = PathsOf(rootPath);
        await TryDeleteAsync(paths.Manifest, ct).ConfigureAwait(false);
        await TryDeleteAsync(paths.Tree, ct).ConfigureAwait(false);
    }

    private static string Sha256Of(MemoryStream buffer) =>
        Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));

    private static CachePaths PathsOf(string virtualRoot)
    {
        var dir = $"{virtualRoot.TrimEnd('/', '\\')}/{CacheDirectory}";
        return new CachePaths(dir, $"{dir}/{TreeFileName}", $"{dir}/{ManifestFileName}", $"{dir}/.gitignore");
    }

    private async Task TryDeleteAsync(string virtualPath, CancellationToken ct)
    {
        try
        {
            if (await _fs.ExistsAsync(virtualPath, ct).ConfigureAwait(false))
                await _fs.DeleteAsync(virtualPath, recursive: false, ct).ConfigureAwait(false);
        }
        catch (IOException) { /* best-effort delete: missing or locked file is ignored */ }
        catch (UnauthorizedAccessException) { /* best-effort delete: insufficient rights is ignored */ }
        catch (FileAccessDeniedException) { /* best-effort delete: a read-only mount is ignored */ }
    }

    private readonly record struct CachePaths(string Directory, string Tree, string Manifest, string GitIgnore);
}
