using System.Diagnostics;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Orchestrates the indexing pipeline that turns a codebase into a RaggableTree.
/// </summary>
public sealed class RaggableTreeBuilder
{
    private readonly Dictionary<string, ILanguageAdapter> _adaptersByLanguage;
    private readonly IFileSystemDiscoverer _discoverer;
    private readonly IFileSystemService _fileSystem;
    private readonly TreeSitterParserPool _pool;
    private readonly IReferenceResolver _resolver;
    private readonly IEmbeddingTextComposer _composer;
    private readonly IReadOnlyList<IFrameworkFingerprinter> _fingerprinters;
    private readonly INodeSummarizer _summarizer;
    private readonly IEmbeddingProvider? _embedder;
    private readonly IVectorStoreProvider? _vectorStore;
    private readonly IProgress<IndexBuildProgress>? _buildProgress;

    public RaggableTreeBuilder(ILanguageAdapter adapter, IFileSystemService fileSystem)
        : this(
            [adapter ?? throw new ArgumentNullException(nameof(adapter))],
            fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)))
    {
    }

    public RaggableTreeBuilder(IEnumerable<ILanguageAdapter> adapters, IFileSystemService fileSystem)
        : this(adapters,
               new FileSystemDiscoverer(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem))),
               fileSystem,
               new TreeSitterParserPool(),
               new DefaultReferenceResolver(),
               new EmbeddingTextComposer())
    {
    }

    public RaggableTreeBuilder(
        IEnumerable<ILanguageAdapter> adapters,
        IFileSystemDiscoverer discoverer,
        IFileSystemService fileSystem,
        TreeSitterParserPool pool,
        IReferenceResolver resolver,
        IEmbeddingTextComposer composer,
        RaggableEnrichmentServices? enrichment = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        enrichment ??= RaggableEnrichmentServices.None;
        _discoverer = discoverer ?? throw new ArgumentNullException(nameof(discoverer));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _fingerprinters = enrichment.Fingerprinters?.ToList() ?? [];
        _summarizer = enrichment.Summarizer ?? NullNodeSummarizer.Instance;
        _embedder = enrichment.Embedder;
        _vectorStore = enrichment.VectorStore;
        _buildProgress = enrichment.BuildProgress;

        var map = new Dictionary<string, ILanguageAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            map[adapter.LanguageName] = adapter;
        }
        if (map.Count == 0) throw new ArgumentException("At least one adapter must be provided.", nameof(adapters));
        _adaptersByLanguage = map;
    }

    public Task<BuildResult> BuildAsync(
        string virtualRoot,
        IndexCodebaseRequest opts,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(virtualRoot);
        ArgumentNullException.ThrowIfNull(opts);
        return BuildCoreAsync(virtualRoot, opts, ct);
    }

    private async Task<BuildResult> BuildCoreAsync(
        string virtualRoot,
        IndexCodebaseRequest opts,
        CancellationToken ct)
    {
        // Phase 1 — Discover (RootPath is a virtual path, resolved by FileSystemDiscoverer via VFS)
        _buildProgress?.Report(new IndexBuildProgress(IndexBuildPhase.Discovery, 0, 0));
        var discoveryRequest = new DiscoveryRequest
        {
            RootPath = virtualRoot,
            Languages = opts.Languages,
            Exclude = opts.Exclude,
            RespectGitignore = opts.RespectGitignore,
        };
        var discovery = await _discoverer.DiscoverAllAsync(discoveryRequest, ct).ConfigureAwait(false);

        var allNodes = new List<RaggableNode>();
        var monorepo = TreeAssembly.BuildMonorepoNode(discovery.RootPath);
        allNodes.Add(monorepo);
        var packageNodes = TreeAssembly.BuildPackageNodes(monorepo, discovery.Packages, discovery.RootPath);
        allNodes.AddRange(packageNodes);

        // Phase 2/3 — Parse + Extract per file
        var graphBuildersByLanguage = new Dictionary<string, DependencyGraphBuilder>(StringComparer.OrdinalIgnoreCase);
        var fileIndex = 0;
        foreach (var file in discovery.Files)
        {
            ct.ThrowIfCancellationRequested();
            _buildProgress?.Report(new IndexBuildProgress(IndexBuildPhase.Parse, ++fileIndex, discovery.Files.Length));
            if (!_adaptersByLanguage.TryGetValue(file.Language, out var adapter)) continue;

            var source = await _fileSystem.TryReadAllTextAsync(file.VirtualPath, ct).ConfigureAwait(false);
            if (source is null) continue;

            var mapper = new UniversalSemanticMapper(adapter, _pool);
            var fileNodes = mapper.ExtractNodes(file.VirtualPath, source);
            TreeAssembly.LinkToPackage(fileNodes, packageNodes, monorepo);
            allNodes.AddRange(fileNodes);

            GetOrCreateBuilder(graphBuildersByLanguage, adapter)
                .RegisterFile(file.VirtualPath, source, fileNodes);
        }

        // Phase 3b — Resolve dependencies per language
        _buildProgress?.Report(new IndexBuildProgress(IndexBuildPhase.Resolve, 0, 0));
        var allEdges = new List<RaggableEdge>();
        foreach (var gb in graphBuildersByLanguage.Values)
        {
            var edges = await gb.BuildDependenciesAsync(_resolver, ct).ConfigureAwait(false);
            allEdges.AddRange(edges);
        }

        // Phase 3c — Fingerprinters
        foreach (var fp in _fingerprinters)
        {
            fp.Apply(allNodes, allEdges);
        }

        // Phase 3d — Apply RootAlias (optional): rewrite virtual-root prefix in every FQN.
        // Makes FQN independent of the mount alias and cleaner to display.
        if (!string.IsNullOrEmpty(opts.RootAlias))
        {
            ApplyRootAlias(allNodes, monorepo.VirtualFilePath, opts.RootAlias);
        }

        // Phase 4 — LLM enrichment (optional)
        var index = allNodes
            .Where(n => !string.IsNullOrEmpty(n.Fqn))
            .GroupBy(n => n.Fqn)
            .ToDictionary(g => g.Key, g => g.First());

        if (opts.EnrichWithLlm)
        {
            _buildProgress?.Report(new IndexBuildProgress(IndexBuildPhase.Enrich, 0, 0));
            await _summarizer.SummarizeAsync(allNodes, index, ct).ConfigureAwait(false);
        }

        // Phase 5a — Compose embedding text
        _composer.ApplyToAll(allNodes, index);

        // Phase 5b — Embed (optional)
        if (_embedder is not null)
            _buildProgress?.Report(new IndexBuildProgress(IndexBuildPhase.Embed, 0, 0));
        var embeddingStats = await EmbedAndCollectStatsAsync(allNodes, opts, ct).ConfigureAwait(false);

        var tree = new RaggableTree(allNodes, allEdges, index);

        // Phase 6 — Persist (optional)
        if (_vectorStore is not null)
        {
            _buildProgress?.Report(new IndexBuildProgress(IndexBuildPhase.Persist, 0, 0));
            var documents = VectorDocumentFactory.FromTree(tree);
            if (documents.Count > 0)
            {
                await _vectorStore.IndexAsync(documents, ct).ConfigureAwait(false);
            }
        }

        var indexId = TreeAssembly.ComputeIndexId(discovery.Files);
        return new BuildResult(tree, indexId, discovery.Files.Length, embeddingStats, discovery.FilterStats);
    }

    private DependencyGraphBuilder GetOrCreateBuilder(
        Dictionary<string, DependencyGraphBuilder> builders, ILanguageAdapter adapter)
    {
        if (!builders.TryGetValue(adapter.LanguageName, out var gb))
        {
            gb = new DependencyGraphBuilder(adapter, _pool);
            builders[adapter.LanguageName] = gb;
        }
        return gb;
    }

    private async System.Threading.Tasks.Task<EmbeddingStats?> EmbedAndCollectStatsAsync(
        List<RaggableNode> allNodes, IndexCodebaseRequest opts, CancellationToken ct)
    {
        if (_embedder is null) return null;

        var embedSw = Stopwatch.StartNew();
        await _embedder.EmbedAsync(allNodes, opts.EmbeddingModel, ct).ConfigureAwait(false);
        embedSw.Stop();

        var nodesEmbedded = allNodes.Count(n => n.Embedding is not null);
        var eligibleNodes = allNodes.Count(n => !string.IsNullOrEmpty(n.EmbeddingText));
        var nodesSkipped = eligibleNodes - nodesEmbedded;

        // Provider name is derived from the implementation type name. Exposing
        // Provider/Model directly on IEmbeddingProvider is intentionally deferred:
        // the current heuristic is sufficient for stats reporting and avoids widening
        // the abstraction for cosmetic metadata.
        var providerName = _embedder.GetType().Name.Replace("EmbeddingProvider", "", StringComparison.Ordinal);
        return new EmbeddingStats
        {
            Provider = string.IsNullOrEmpty(providerName) ? "Unknown" : providerName,
            Model = opts.EmbeddingModel ?? "",
            Dimensions = _embedder.Dimensions,
            NodesEmbedded = nodesEmbedded,
            NodesSkipped = nodesSkipped < 0 ? 0 : nodesSkipped,
            Elapsed = embedSw.Elapsed,
        };
    }

    private static void ApplyRootAlias(List<RaggableNode> nodes, string virtualRoot, string alias)
    {
        var prefix = virtualRoot.TrimEnd('/');
        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.Fqn)) continue;
            if (node.Fqn.Equals(prefix, StringComparison.Ordinal))
            {
                node.Fqn = alias;
            }
            else if (node.Fqn.StartsWith(prefix, StringComparison.Ordinal))
            {
                node.Fqn = string.Concat(alias, node.Fqn.AsSpan(prefix.Length));
            }
        }
    }

}

/// <summary>Outcome of building a <see cref="RaggableTree"/> from a codebase.</summary>
public sealed record BuildResult(
    RaggableTree Tree,
    string IndexId,
    int FileCount,
    EmbeddingStats? EmbeddingStats,
    DiscoveryFilterStats FilterStats);
