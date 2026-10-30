using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.Cache;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class IndexCodebaseTool : ToolBase<IndexCodebaseRequest, IndexCodebaseResponse>
{
    private readonly InMemoryRaggableStore _store;
    private readonly Func<RaggableTreeBuilder> _builderFactory;
    private readonly CachingTreeBuilder? _cachingBuilder;

    /// <summary>Without <paramref name="cachingBuilder"/>, the index cache between sessions, every call builds from scratch.</summary>
    public IndexCodebaseTool(
        InMemoryRaggableStore store,
        Func<RaggableTreeBuilder> builderFactory,
        CachingTreeBuilder? cachingBuilder = null,
        ILogger<IndexCodebaseTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
        _cachingBuilder = cachingBuilder;
    }

    public override string Name => "index_codebase";
    public override string Description => "Scan the codebase and build the full RaggableTree index, reusing the one saved by an earlier session when the host wires the cache.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<IndexCodebaseResponse> ExecuteTypedAsync(IndexCodebaseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<IndexCodebaseResponse> ExecuteTypedCoreAsync()
        {
        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            return new IndexCodebaseResponse
            {
                IndexId = string.Empty,
                NodeCount = 0,
                EdgeCount = 0,
                StatementCount = 0,
                FileCount = 0,
                Elapsed = TimeSpan.Zero,
                Errors = ["root_path is required. Example: index_codebase(root_path=\"/src\")"],
            };
        }

        if (request.MaxParallelism < 0)
        {
            return new IndexCodebaseResponse
            {
                IndexId = string.Empty,
                NodeCount = 0,
                EdgeCount = 0,
                StatementCount = 0,
                FileCount = 0,
                Elapsed = TimeSpan.Zero,
                Errors = [$"max_parallelism must be 0 (one per processor) or a positive number, not {request.MaxParallelism}."],
            };
        }

        var stopwatch = Stopwatch.StartNew();
        BuildResult buildResult;
        var cache = "disabled";
        var reindexed = 0;
        if (_cachingBuilder is null)
        {
            buildResult = await _builderFactory().BuildAsync(request.RootPath, request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var cached = await _cachingBuilder.BuildAsync(request.RootPath, request, cancellationToken).ConfigureAwait(false);
            buildResult = cached.Build;
            cache = cached.Outcome switch
            {
                IndexCacheOutcome.Bypassed => "bypassed",
                IndexCacheOutcome.Hit => "hit",
                IndexCacheOutcome.Refreshed => "refreshed",
                _ => "miss",
            };
            reindexed = cached.ReindexedFileCount;
        }

        _store.Replace(buildResult.Tree.Nodes, buildResult.Tree.Edges);
        stopwatch.Stop();

        var errors = buildResult.Tree.Nodes
            .Where(n => n.ParseStatus == ParseStatus.Failed)
            .Select(n => n.VirtualFilePath)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

        var statementCount = 0;
        foreach (var node in buildResult.Tree.Nodes)
        {
            statementCount += node.Statements.Count;
        }

        return new IndexCodebaseResponse
        {
            IndexId = buildResult.IndexId,
            NodeCount = buildResult.Tree.Nodes.Count,
            EdgeCount = buildResult.Tree.Edges.Count,
            StatementCount = statementCount,
            FileCount = buildResult.FileCount,
            Elapsed = stopwatch.Elapsed,
            Errors = errors,
            EmbeddingStats = buildResult.EmbeddingStats,
            Cache = cache,
            ReindexedFileCount = reindexed,
            FilterStats = new IndexFilterStats
            {
                TotalEnumerated = buildResult.FilterStats.TotalEnumerated,
                ExcludedByExcludeSet = buildResult.FilterStats.ExcludedByExcludeSet,
                ExcludedByGitignore = buildResult.FilterStats.ExcludedByGitignore,
                ExcludedByLanguage = buildResult.FilterStats.ExcludedByLanguage,
                ExcludedBySuffix = buildResult.FilterStats.ExcludedBySuffix,
                ExcludedByPolicy = buildResult.FilterStats.ExcludedByPolicy,
                ExcludedByPolicyPaths = buildResult.FilterStats.ExcludedByPolicyPaths,
                VfsFileEntriesYielded = buildResult.FilterStats.VfsFileEntriesYielded,
                PhysicalEntriesProbe = buildResult.FilterStats.PhysicalEntriesProbe,
                PhysicalRoot = buildResult.FilterStats.PhysicalRoot,
            },
        };
        }
    }
}
