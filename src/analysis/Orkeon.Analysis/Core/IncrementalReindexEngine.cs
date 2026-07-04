using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

public sealed class IncrementalReindexEngine
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

    public IncrementalReindexEngine(ILanguageAdapter adapter, IFileSystemService fileSystem)
        : this(
            [adapter ?? throw new ArgumentNullException(nameof(adapter))],
            fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)))
    {
    }

    public IncrementalReindexEngine(IEnumerable<ILanguageAdapter> adapters, IFileSystemService fileSystem)
        : this(adapters,
               new FileSystemDiscoverer(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem))),
               fileSystem,
               new TreeSitterParserPool(),
               new DefaultReferenceResolver(),
               new EmbeddingTextComposer())
    {
    }

    public IncrementalReindexEngine(
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

        var map = new Dictionary<string, ILanguageAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            map[adapter.LanguageName] = adapter;
        }
        if (map.Count == 0) throw new ArgumentException("At least one adapter must be provided.", nameof(adapters));
        _adaptersByLanguage = map;
    }

    public Task<IncrementalReindexResult> ReindexAsync(
        RaggableTree currentTree,
        string virtualRoot,
        IReadOnlyList<string> changedFilePaths,
        IndexCodebaseRequest opts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(currentTree);
        ArgumentException.ThrowIfNullOrEmpty(virtualRoot);
        ArgumentNullException.ThrowIfNull(changedFilePaths);
        ArgumentNullException.ThrowIfNull(opts);
        return ReindexCoreAsync(currentTree, virtualRoot, changedFilePaths, opts, ct);
    }

    private async Task<IncrementalReindexResult> ReindexCoreAsync(
        RaggableTree currentTree,
        string virtualRoot,
        IReadOnlyList<string> changedFilePaths,
        IndexCodebaseRequest opts,
        CancellationToken ct)
    {
        // Phase 1 — Discover via VFS. RootPath is a virtual path, FileSystemDiscoverer resolves it.
        var discovery = await _discoverer.DiscoverAllAsync(new DiscoveryRequest
        {
            RootPath = virtualRoot,
            Languages = opts.Languages,
            Exclude = opts.Exclude,
        }, ct).ConfigureAwait(false);

        var rootVirtual = discovery.RootPath;
        var changedVirtual = NormalizeChangedPaths(changedFilePaths, rootVirtual);

        var currentFiles = new HashSet<string>(
            discovery.Files.Select(f => f.VirtualPath),
            StringComparer.Ordinal);

        var (reusedNodes, removedNodeIds) = PartitionNodes(currentTree, changedVirtual, currentFiles);
        ClearEdgeState(reusedNodes);

        var reusedByPath = GroupReusedByPath(reusedNodes);
        var reusedModulePaths = new HashSet<string>(
            reusedNodes.Where(n => n.Level == NodeLevel.L2_Module).Select(n => n.VirtualFilePath),
            StringComparer.Ordinal);

        var allNodes = new List<RaggableNode>();
        var monorepo = TreeAssembly.BuildMonorepoNode(rootVirtual);
        allNodes.Add(monorepo);
        var packageNodes = TreeAssembly.BuildPackageNodes(monorepo, discovery.Packages, rootVirtual);
        allNodes.AddRange(packageNodes);

        var graphBuildersByLanguage = new Dictionary<string, DependencyGraphBuilder>(StringComparer.OrdinalIgnoreCase);
        var newNodes = new List<RaggableNode>();
        var changedFileCount = 0;
        var reusedFileCount = 0;

        foreach (var file in discovery.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!_adaptersByLanguage.TryGetValue(file.Language, out var adapter)) continue;

            var source = await _fileSystem.TryReadAllTextAsync(file.VirtualPath, ct).ConfigureAwait(false);
            if (source is null) continue;

            var reuse = reusedModulePaths.Contains(file.VirtualPath) && !changedVirtual.Contains(file.VirtualPath);
            List<RaggableNode> fileNodes;
            if (reuse)
            {
                fileNodes = reusedByPath[file.VirtualPath];
                reusedFileCount++;
            }
            else
            {
                var mapper = new UniversalSemanticMapper(adapter, _pool);
                fileNodes = [.. mapper.ExtractNodes(file.VirtualPath, source)];
                newNodes.AddRange(fileNodes);
                changedFileCount++;
            }

            TreeAssembly.LinkToPackage(fileNodes, packageNodes, monorepo);
            allNodes.AddRange(fileNodes);

            GetOrCreateBuilder(graphBuildersByLanguage, adapter)
                .RegisterFile(file.VirtualPath, source, fileNodes);
        }

        var allEdges = new List<RaggableEdge>();
        foreach (var gb in graphBuildersByLanguage.Values)
        {
            var edges = await gb.BuildDependenciesAsync(_resolver, ct).ConfigureAwait(false);
            allEdges.AddRange(edges);
        }

        foreach (var fp in _fingerprinters)
        {
            fp.Apply(allNodes, allEdges);
        }

        var index = allNodes
            .Where(n => !string.IsNullOrEmpty(n.Fqn))
            .GroupBy(n => n.Fqn)
            .ToDictionary(g => g.Key, g => g.First());

        await EnrichNewNodesAsync(newNodes, index, opts, ct).ConfigureAwait(false);

        var tree = new RaggableTree(allNodes, allEdges, index);

        await SyncVectorStoreAsync(newNodes, removedNodeIds, index, ct).ConfigureAwait(false);

        var indexId = TreeAssembly.ComputeIndexId(discovery.Files);
        return new IncrementalReindexResult(
            tree,
            indexId,
            changedFileCount,
            reusedFileCount,
            newNodes.Count,
            removedNodeIds.Count);
    }

    private async System.Threading.Tasks.Task EnrichNewNodesAsync(
        List<RaggableNode> newNodes,
        Dictionary<string, RaggableNode> index,
        IndexCodebaseRequest opts,
        CancellationToken ct)
    {
        if (newNodes.Count == 0) return;

        if (opts.EnrichWithLlm)
        {
            await _summarizer.SummarizeAsync(newNodes, index, ct).ConfigureAwait(false);
        }

        _composer.ApplyToAll(newNodes, index);

        if (_embedder is not null)
        {
            await _embedder.EmbedAsync(newNodes, opts.EmbeddingModel, ct).ConfigureAwait(false);
        }
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

    private async System.Threading.Tasks.Task SyncVectorStoreAsync(
        List<RaggableNode> newNodes,
        List<string> removedNodeIds,
        Dictionary<string, RaggableNode> index,
        CancellationToken ct)
    {
        if (_vectorStore is null) return;

        if (removedNodeIds.Count > 0)
        {
            await _vectorStore.DeleteAsync(removedNodeIds, ct).ConfigureAwait(false);
        }
        if (newNodes.Count > 0)
        {
            var newDocs = VectorDocumentFactory.FromTree(new RaggableTree(newNodes, [], index));
            if (newDocs.Count > 0)
            {
                await _vectorStore.IndexAsync(newDocs, ct).ConfigureAwait(false);
            }
        }
    }

    private static HashSet<string> NormalizeChangedPaths(IReadOnlyList<string> paths, string rootVirtual)
    {
        var root = rootVirtual.TrimEnd('/');
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path)) continue;
            var normalized = path.Replace('\\', '/');
            if (normalized.StartsWith('/'))
            {
                set.Add(normalized.TrimEnd('/'));
            }
            else
            {
                set.Add($"{root}/{normalized.TrimStart('/').TrimEnd('/')}");
            }
        }
        return set;
    }

    private static (List<RaggableNode> Reused, List<string> RemovedIds) PartitionNodes(
        RaggableTree currentTree,
        HashSet<string> changedVirtual,
        HashSet<string> currentFiles)
    {
        var reused = new List<RaggableNode>();
        var removed = new List<string>();
        foreach (var node in currentTree.Nodes)
        {
            if (node.Level == NodeLevel.L0_Monorepo || node.Level == NodeLevel.L1_Package) continue;
            var filePath = node.VirtualFilePath;
            var drop = changedVirtual.Contains(filePath) || !currentFiles.Contains(filePath);
            if (drop) removed.Add(node.Id);
            else reused.Add(node);
        }
        return (reused, removed);
    }

    private static void ClearEdgeState(List<RaggableNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.ImportIdsMutable.Clear();
            node.ImportedByIdsMutable.Clear();
            node.CallIdsMutable.Clear();
            node.CalledByIdsMutable.Clear();
            node.ExtendsIdsMutable.Clear();
            node.ImplementsIdsMutable.Clear();
        }
    }

    private static Dictionary<string, List<RaggableNode>> GroupReusedByPath(List<RaggableNode> reused)
    {
        var map = new Dictionary<string, List<RaggableNode>>(StringComparer.Ordinal);
        foreach (var node in reused)
        {
            if (!map.TryGetValue(node.VirtualFilePath, out var list))
            {
                list = [];
                map[node.VirtualFilePath] = list;
            }
            list.Add(node);
        }
        return map;
    }
}

public sealed record IncrementalReindexResult(
    RaggableTree Tree,
    string IndexId,
    int ChangedFileCount,
    int ReusedFileCount,
    int AddedNodeCount,
    int RemovedNodeCount);
