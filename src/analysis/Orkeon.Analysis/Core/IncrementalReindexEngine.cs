using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        ArgumentNullException.ThrowIfNull(discoverer);
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
        var started = Stopwatch.GetTimestamp();
        var root = virtualRoot.TrimEnd('/');
        var changedVirtual = NormalizeChangedPaths(changedFilePaths, root);

        var (keptNodes, removedNodeIds) = PartitionNodes(currentTree, changedVirtual);
        var removedIds = new HashSet<string>(removedNodeIds, StringComparer.Ordinal);
        var packageNodes = keptNodes.Where(n => n.Level == NodeLevel.L1_Package).ToList();
        var monorepo = keptNodes.FirstOrDefault(n => n.Level == NodeLevel.L0_Monorepo)
            ?? TreeAssembly.BuildMonorepoNode(root);
        foreach (var parent in keptNodes.Where(n => n.Level <= NodeLevel.L1_Package))
        {
            parent.ChildrenIdsMutable.RemoveAll(removedIds.Contains);
        }

        var unchangedNodes = keptNodes.Where(n => n.Level > NodeLevel.L1_Package).ToList();
        PurgeLinks(unchangedNodes, removedIds);
        var reusedFileCount = unchangedNodes.Select(n => n.VirtualFilePath).Distinct(StringComparer.Ordinal).Count();

        var graphBuildersByLanguage = new Dictionary<string, DependencyGraphBuilder>(StringComparer.OrdinalIgnoreCase);
        var changedByLanguage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var newNodes = new List<RaggableNode>();
        var filesParsed = 0;

        foreach (var path in changedVirtual)
        {
            ct.ThrowIfCancellationRequested();
            var language = FileSystemDiscoverer.IndexableLanguage(root, path, opts.Exclude, opts.Languages);
            if (language is null) continue;
            var isMarkdown = language == RaggableTreeBuilder.MarkdownLanguage;
            if (!isMarkdown && !_adaptersByLanguage.ContainsKey(language)) continue;

            var source = await _fileSystem.TryReadAllTextAsync(path, ct).ConfigureAwait(false);
            if (source is null) continue;

            filesParsed++;
            if (isMarkdown)
            {
                newNodes.AddRange(RaggableTreeBuilder.MarkdownMapper.Map(path, source));
                continue;
            }

            var adapter = _adaptersByLanguage[language];
            var fileNodes = new UniversalSemanticMapper(adapter, _pool).ExtractNodes(path, source).ToList();
            newNodes.AddRange(fileNodes);
            TreeAssembly.LinkToPackage(fileNodes, packageNodes, monorepo);
            GetOrCreateBuilder(graphBuildersByLanguage, adapter).RegisterFile(path, source, fileNodes);
            if (!changedByLanguage.TryGetValue(adapter.LanguageName, out var paths))
            {
                paths = [];
                changedByLanguage[adapter.LanguageName] = paths;
            }
            paths.Add(path);
        }

        var newEdges = new List<RaggableEdge>();
        foreach (var (language, gb) in graphBuildersByLanguage)
        {
            var languageNodes = unchangedNodes.Where(n => string.Equals(n.Language, language, StringComparison.OrdinalIgnoreCase));
            newEdges.AddRange(await gb.BuildForFilesAsync(changedByLanguage[language], languageNodes, _resolver, ct).ConfigureAwait(false));
        }

        foreach (var fp in _fingerprinters)
        {
            fp.Apply(newNodes, newEdges);
        }

        var allNodes = new List<RaggableNode>(keptNodes);
        allNodes.AddRange(newNodes);
        var nodesById = allNodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var newIds = new HashSet<string>(newNodes.Select(n => n.Id), StringComparer.Ordinal);

        // Edges held by the other files survive when both ends still exist; the changed files' own
        // outgoing edges are the ones just rebuilt.
        var keptEdges = currentTree.Edges
            .Where(e => nodesById.ContainsKey(e.FromId) && nodesById.ContainsKey(e.ToId) && !newIds.Contains(e.FromId))
            .ToList();
        foreach (var edge in keptEdges.Where(e => newIds.Contains(e.ToId)))
        {
            DependencyGraphBuilder.LinkNodes(edge.Kind, nodesById[edge.FromId], nodesById[edge.ToId]);
        }

        var allEdges = new List<RaggableEdge>(keptEdges);
        allEdges.AddRange(newEdges);

        var index = allNodes
            .Where(n => !string.IsNullOrEmpty(n.Fqn))
            .GroupBy(n => n.Fqn)
            .ToDictionary(g => g.Key, g => g.First());

        await EnrichNewNodesAsync(newNodes, index, opts, ct).ConfigureAwait(false);

        var tree = new RaggableTree(allNodes, allEdges, index);

        await SyncVectorStoreAsync(newNodes, removedNodeIds, index, ct).ConfigureAwait(false);

        return new IncrementalReindexResult(
            tree,
            ComputeIndexId(unchangedNodes, newNodes),
            filesParsed,
            reusedFileCount,
            newNodes.Count,
            removedNodeIds.Count)
        {
            AddedEdgeCount = newEdges.Count,
            RemovedEdgeCount = currentTree.Edges.Count - keptEdges.Count,
            FilesParsed = filesParsed,
            Elapsed = Stopwatch.GetElapsedTime(started),
            TotalNodeCount = allNodes.Count,
            TotalEdgeCount = allEdges.Count,
        };
    }

    private static string ComputeIndexId(List<RaggableNode> unchangedNodes, List<RaggableNode> newNodes)
    {
        var sb = new StringBuilder();
        foreach (var node in unchangedNodes.Concat(newNodes).OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            sb.Append(node.Id).Append(':').Append(node.Sha256).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
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

    private static (List<RaggableNode> Kept, List<string> RemovedIds) PartitionNodes(
        RaggableTree currentTree,
        HashSet<string> changedVirtual)
    {
        var kept = new List<RaggableNode>();
        var removed = new List<string>();
        foreach (var node in currentTree.Nodes)
        {
            if (node.Level > NodeLevel.L1_Package && changedVirtual.Contains(node.VirtualFilePath)) removed.Add(node.Id);
            else kept.Add(node);
        }
        return (kept, removed);
    }

    private static void PurgeLinks(List<RaggableNode> nodes, HashSet<string> removedIds)
    {
        foreach (var node in nodes)
        {
            node.ImportIdsMutable.RemoveAll(removedIds.Contains);
            node.ImportedByIdsMutable.RemoveAll(removedIds.Contains);
            node.CallIdsMutable.RemoveAll(removedIds.Contains);
            node.CalledByIdsMutable.RemoveAll(removedIds.Contains);
            node.ExtendsIdsMutable.RemoveAll(removedIds.Contains);
            node.ImplementsIdsMutable.RemoveAll(removedIds.Contains);
        }
    }
}

public sealed record IncrementalReindexResult(
    RaggableTree Tree,
    string IndexId,
    int ChangedFileCount,
    int ReusedFileCount,
    int AddedNodeCount,
    int RemovedNodeCount)
{
    public int AddedEdgeCount { get; init; }
    public int RemovedEdgeCount { get; init; }
    public int FilesParsed { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int TotalNodeCount { get; init; }
    public int TotalEdgeCount { get; init; }
}
