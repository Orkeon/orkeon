using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Serialization;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Cache;

/// <summary>What the index cache did for one build.</summary>
public enum IndexCacheOutcome
{
    /// <summary><c>use_cache</c> was false: built from scratch, then saved.</summary>
    Bypassed,
    /// <summary>Nothing reusable under the key: built from scratch, then saved.</summary>
    Miss,
    /// <summary>Reloaded as saved: no file changed.</summary>
    Hit,
    /// <summary>Reloaded, then the changed files parsed again, then saved.</summary>
    Refreshed,
}

/// <summary>A build and what the cache did for it.</summary>
public sealed record CachedBuildResult(BuildResult Build, IndexCacheOutcome Outcome, int ReindexedFileCount);

/// <summary>
/// Builds the index of a root through <see cref="IRaggableTreeCache"/>. The files are discovered with the request's
/// options and compared with the saved entry: none changed reloads it, a few changed reindexes them with
/// <see cref="IncrementalReindexEngine"/>, anything else - another key, a package marker changed, a root alias, more
/// than half of the files changed - builds from scratch. Every build or refresh is saved for the next session.
/// </summary>
public sealed partial class CachingTreeBuilder
{
    // Bumped when what a cached tree holds changes in a way the serializer version does not tell.
    private const char FormatVersion = '1';

    private readonly IRaggableTreeCache _cache;
    private readonly IFileSystemDiscoverer _discoverer;
    private readonly IFileSystemService _fs;
    private readonly Func<RaggableTreeBuilder> _builderFactory;
    private readonly Func<IncrementalReindexEngine> _engineFactory;
    private readonly string _environment;
    private readonly IVectorStoreProvider? _vectorStore;
    private readonly ILogger _logger;

    /// <summary>
    /// The environment is what else shapes a tree than the request: the adapters, fingerprinters, summarizer and
    /// embedder the host wires (<see cref="DescribeEnvironment"/>). A change of it makes every saved entry a miss.
    /// </summary>
    public CachingTreeBuilder(
        IRaggableTreeCache cache,
        IFileSystemDiscoverer discoverer,
        IFileSystemService fileSystem,
        Func<RaggableTreeBuilder> builderFactory,
        Func<IncrementalReindexEngine> engineFactory,
        string environment,
        IVectorStoreProvider? vectorStore = null,
        ILogger<CachingTreeBuilder>? logger = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _discoverer = discoverer ?? throw new ArgumentNullException(nameof(discoverer));
        _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _vectorStore = vectorStore;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>The environment string of a host: the type of every component that shapes a tree, and the embedding size.</summary>
    public static string DescribeEnvironment(
        IEnumerable<ILanguageAdapter> adapters,
        IEnumerable<IFrameworkFingerprinter> fingerprinters,
        INodeSummarizer? summarizer,
        IEmbeddingProvider? embedder)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(fingerprinters);
        var parts = new List<string>();
        parts.AddRange(adapters.Select(a => $"adapter:{a.LanguageName}:{a.GetType().FullName}").Order(StringComparer.Ordinal));
        parts.AddRange(fingerprinters.Select(f => $"fingerprinter:{f.GetType().FullName}").Order(StringComparer.Ordinal));
        parts.Add($"summarizer:{summarizer?.GetType().FullName}");
        parts.Add(embedder is null ? "embedder:" : $"embedder:{embedder.GetType().FullName}:{embedder.Dimensions}");
        return string.Join('\n', parts);
    }

    public Task<CachedBuildResult> BuildAsync(string virtualRoot, IndexCodebaseRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(virtualRoot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaxParallelism);
        return BuildCoreAsync(virtualRoot, request, ct);
    }

    private async Task<CachedBuildResult> BuildCoreAsync(string virtualRoot, IndexCodebaseRequest request, CancellationToken ct)
    {
        var discovery = await _discoverer.DiscoverAllAsync(
            RaggableTreeBuilder.DiscoveryRequestOf(virtualRoot, request), ct).ConfigureAwait(false);
        var key = CacheKeyOf(request);
        var files = discovery.Files.ToDictionary(f => f.RelativePath, f => f.Sha256, StringComparer.Ordinal);
        var markers = await MarkerHashesAsync(discovery, ct).ConfigureAwait(false);
        var indexId = TreeAssembly.ComputeIndexId(discovery.Files);

        var saved = request.UseCache ? await _cache.LoadAsync(virtualRoot, key, ct).ConfigureAwait(false) : null;
        if (saved is not null && SameEntries(saved.Markers, markers))
        {
            var changed = ChangedPaths(saved.Files, files);
            if (changed.Count == 0)
            {
                await MirrorAsync(saved.Tree, ct).ConfigureAwait(false);
                return new CachedBuildResult(Result(saved.Tree, indexId, discovery, null), IndexCacheOutcome.Hit, 0);
            }
            if (string.IsNullOrEmpty(request.RootAlias) && changed.Count * 2 <= Math.Max(files.Count, 1))
            {
                var root = discovery.RootPath.TrimEnd('/');
                var refreshed = await _engineFactory().ReindexAsync(
                    saved.Tree, root, [.. changed.Select(p => $"{root}/{p}")], request, ct).ConfigureAwait(false);
                await MirrorAsync(refreshed.Tree, ct).ConfigureAwait(false);
                await SaveAsync(virtualRoot, new RaggableTreeCacheEntry(refreshed.Tree, indexId, key, files, markers), ct).ConfigureAwait(false);
                return new CachedBuildResult(Result(refreshed.Tree, indexId, discovery, null), IndexCacheOutcome.Refreshed, changed.Count);
            }
        }

        var build = await _builderFactory().BuildAsync(discovery, request, ct).ConfigureAwait(false);
        await SaveAsync(virtualRoot, new RaggableTreeCacheEntry(build.Tree, build.IndexId, key, files, markers), ct).ConfigureAwait(false);
        return new CachedBuildResult(build, request.UseCache ? IndexCacheOutcome.Miss : IndexCacheOutcome.Bypassed, 0);
    }

    // Everything of the request that shapes the tree; MaxParallelism and UseCache do not.
    private string CacheKeyOf(IndexCodebaseRequest request)
    {
        var assembly = typeof(CachingTreeBuilder).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        var text = new StringBuilder()
            .Append("format:").Append(FormatVersion).Append(':').Append(RaggableTreeSerializer.SupportedVersion).Append('\n')
            .Append("analysis:").Append(version).Append('\n')
            .Append(_environment).Append('\n')
            .Append("languages:").AppendJoin(',', request.Languages.Order(StringComparer.OrdinalIgnoreCase)).Append('\n')
            .Append("exclude:").AppendJoin(',', request.Exclude.Order(StringComparer.Ordinal)).Append('\n')
            .Append("gitignore:").Append(request.RespectGitignore).Append('\n')
            .Append("alias:").Append(request.RootAlias).Append('\n')
            .Append("enrich:").Append(request.EnrichWithLlm).Append('\n')
            .Append("embedding_model:").Append(request.EmbeddingModel);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // The project graph reads the package markers (a csproj): a changed marker rebuilds from scratch.
    private async Task<Dictionary<string, string>> MarkerHashesAsync(DiscoveryResult discovery, CancellationToken ct)
    {
        var root = discovery.RootPath.TrimEnd('/');
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var package in discovery.Packages)
        {
            var text = await _fs.TryReadAllTextAsync($"{root}/{package.MarkerPath}", ct).ConfigureAwait(false);
            hashes[package.MarkerPath] = text is null ? "" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
        return hashes;
    }

    private static List<string> ChangedPaths(IReadOnlyDictionary<string, string> saved, Dictionary<string, string> current)
    {
        var changed = current
            .Where(kv => !saved.TryGetValue(kv.Key, out var sha) || !string.Equals(sha, kv.Value, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();
        changed.AddRange(saved.Keys.Where(p => !current.ContainsKey(p)));
        changed.Sort(StringComparer.Ordinal);
        return changed;
    }

    private static bool SameEntries(IReadOnlyDictionary<string, string> saved, Dictionary<string, string> current) =>
        saved.Count == current.Count
        && current.All(kv => saved.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

    // A host that mirrors embeddings into a vector store gets the reloaded tree too; upserting is idempotent.
    private async Task MirrorAsync(RaggableTree tree, CancellationToken ct)
    {
        if (_vectorStore is null) return;
        var documents = VectorDocumentFactory.FromTree(tree);
        if (documents.Count > 0) await _vectorStore.IndexAsync(documents, ct).ConfigureAwait(false);
    }

    private async Task SaveAsync(string virtualRoot, RaggableTreeCacheEntry entry, CancellationToken ct)
    {
        if (!await _cache.SaveAsync(virtualRoot, entry, ct).ConfigureAwait(false))
            LogNotSaved(virtualRoot);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The index of {Root} could not be saved to its cache; the next call builds it again")]
    private partial void LogNotSaved(string root);

    private static BuildResult Result(RaggableTree tree, string indexId, DiscoveryResult discovery, EmbeddingStats? stats) =>
        new(tree, indexId, discovery.Files.Length, stats, discovery.FilterStats);
}
