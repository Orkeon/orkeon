using Orkeon.Constants.FileSystem;
using System.Text.Json;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Serialization;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Cache;

public sealed class RaggableTreeCache : IRaggableTreeCache
{
    public const string CacheDirectoryName = ConventionalNames.StateDirectory;
    public const string TreeFileName = "raggable-tree.json";
    public const string ManifestFileName = "raggable-tree-manifest.json";

    private static readonly JsonSerializerOptions s_manifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly IRaggableTreeSerializer _serializer;
    private readonly IFileSystemDiscoverer _discoverer;
    private readonly IFileSystemService _fs;
    private readonly Dictionary<string, string> _adapterVersions;
    private readonly EmbeddingCacheProfile? _embeddingProfile;

    /// <summary>
    /// Constructs a cache. <paramref name="embeddingProfile"/> identifies the active
    /// embedding provider (Provider, Model, Dimensions). When non-null, a cache whose
    /// manifest carries a different profile is treated as a clean miss — preventing
    /// silent corruption when switching from e.g. OpenAI 1536-dim to a local 384-dim
    /// provider. When null (legacy / no embeddings), the embedding profile is not
    /// checked.
    /// </summary>
    public RaggableTreeCache(
        IRaggableTreeSerializer serializer,
        IFileSystemDiscoverer discoverer,
        IFileSystemService fileSystem,
        IReadOnlyDictionary<string, string>? adapterVersions = null,
        EmbeddingCacheProfile? embeddingProfile = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _discoverer = discoverer ?? throw new ArgumentNullException(nameof(discoverer));
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _adapterVersions = adapterVersions is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(adapterVersions, StringComparer.OrdinalIgnoreCase);
        _embeddingProfile = embeddingProfile;
    }

    public async Task<RaggableTreeCacheLoadResult> LoadAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        var vpaths = GetVirtualPaths(rootPath);
        if (!await _fs.ExistsAsync(vpaths.Tree, ct).ConfigureAwait(false)) return RaggableTreeCacheLoadResult.Miss;
        if (!await IsValidAsync(rootPath, ct).ConfigureAwait(false)) return RaggableTreeCacheLoadResult.Miss;

        try
        {
            var stream = await _fs.OpenReadStreamAsync(vpaths.Tree, ct).ConfigureAwait(false);
            await using var __stream = stream.ConfigureAwait(false);
            var snapshot = await _serializer.DeserializeAsync(stream, ct).ConfigureAwait(false);
            return snapshot is null
                ? RaggableTreeCacheLoadResult.Miss
                : new RaggableTreeCacheLoadResult(snapshot.Tree, snapshot.IndexId);
        }
        catch (JsonException) { return RaggableTreeCacheLoadResult.Miss; }
        catch (NotSupportedException) { return RaggableTreeCacheLoadResult.Miss; }
        catch (IOException) { return RaggableTreeCacheLoadResult.Miss; }
    }

    public Task SaveAsync(RaggableTree tree, string indexId, string rootPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrEmpty(indexId);
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        return SaveCoreAsync(tree, indexId, rootPath, ct);
    }

    private async Task SaveCoreAsync(RaggableTree tree, string indexId, string rootPath, CancellationToken ct)
    {
        var vpaths = GetVirtualPaths(rootPath);
        await _fs.CreateDirectoryAsync(vpaths.Directory, ct).ConfigureAwait(false);

        var stream = await _fs.OpenWriteStreamAsync(vpaths.Tree, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await _serializer.SerializeAsync(tree, indexId, stream, ct).ConfigureAwait(false);
        }

        var manifest = await BuildManifestAsync(indexId, rootPath, ct).ConfigureAwait(false);
        var manifestJson = JsonSerializer.Serialize(manifest, s_manifestOptions);
        await _fs.WriteAllTextAsync(vpaths.Manifest, manifestJson, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsValidAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        var vpaths = GetVirtualPaths(rootPath);
        if (!await _fs.ExistsAsync(vpaths.Tree, ct).ConfigureAwait(false)) return false;
        if (!await _fs.ExistsAsync(vpaths.Manifest, ct).ConfigureAwait(false)) return false;

        CacheManifest? manifest;
        try
        {
            var json = await _fs.TryReadAllTextAsync(vpaths.Manifest, ct).ConfigureAwait(false);
            if (json is null) return false;
            manifest = JsonSerializer.Deserialize<CacheManifest>(json, s_manifestOptions);
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }

        if (manifest is null) return false;
        if (!AdapterVersionsMatch(manifest.AdapterVersions)) return false;
        if (!EmbeddingProfileMatches(manifest.Embedding)) return false;

        var discovery = await _discoverer.DiscoverAllAsync(
            new DiscoveryRequest { RootPath = rootPath }, ct).ConfigureAwait(false);

        if (discovery.Files.Length != manifest.Files.Count) return false;

        foreach (var file in discovery.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!manifest.Files.TryGetValue(file.RelativePath, out var knownSha)) return false;
            if (!string.Equals(knownSha, file.Sha256, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public async Task InvalidateAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        var vpaths = GetVirtualPaths(rootPath);
        await TryDeleteAsync(vpaths.Tree, ct).ConfigureAwait(false);
        await TryDeleteAsync(vpaths.Manifest, ct).ConfigureAwait(false);
    }

    private bool AdapterVersionsMatch(Dictionary<string, string> stored)
    {
        if (stored.Count != _adapterVersions.Count) return false;
        foreach (var kv in _adapterVersions)
        {
            if (!stored.TryGetValue(kv.Key, out var v)) return false;
            if (!string.Equals(v, kv.Value, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>
    /// Verifies that the embedding profile recorded in the cache manifest matches
    /// the active provider's profile. Three cases:
    /// <list type="bullet">
    /// <item>Active profile is null → no gating; any stored profile is accepted (legacy).</item>
    /// <item>Active profile non-null, stored profile null → cache predates LE-10 gating; treated as stale.</item>
    /// <item>Both non-null → ordinal compare on (Provider, Model, Dimensions).</item>
    /// </list>
    /// </summary>
    private bool EmbeddingProfileMatches(EmbeddingCacheProfile? stored)
    {
        if (_embeddingProfile is null) return true;
        if (stored is null) return false;
        if (!string.Equals(stored.Provider, _embeddingProfile.Provider, StringComparison.Ordinal)) return false;
        if (!string.Equals(stored.Model, _embeddingProfile.Model, StringComparison.Ordinal)) return false;
        if (stored.Dimensions != _embeddingProfile.Dimensions) return false;
        return true;
    }

    private async Task<CacheManifest> BuildManifestAsync(string indexId, string virtualRoot, CancellationToken ct)
    {
        var discovery = await _discoverer.DiscoverAllAsync(
            new DiscoveryRequest { RootPath = virtualRoot }, ct).ConfigureAwait(false);

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in discovery.Files)
        {
            files[f.RelativePath] = f.Sha256;
        }
        return new CacheManifest
        {
            IndexId = indexId,
            Files = files,
            AdapterVersions = new Dictionary<string, string>(_adapterVersions, StringComparer.OrdinalIgnoreCase),
            Embedding = _embeddingProfile,
        };
    }

    private static CachePaths GetVirtualPaths(string virtualRoot)
    {
        // Build virtual paths for the cache directory inside the project root.
        // Uses forward slashes consistent with the VFS convention.
        var normalizedRoot = virtualRoot.TrimEnd('/', '\\');
        var dir = $"{normalizedRoot}/{CacheDirectoryName}";
        return new CachePaths(dir, $"{dir}/{TreeFileName}", $"{dir}/{ManifestFileName}");
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
    }

    private readonly record struct CachePaths(string Directory, string Tree, string Manifest);
}
