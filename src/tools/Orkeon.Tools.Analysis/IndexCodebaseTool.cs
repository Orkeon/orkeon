using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Core;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class IndexCodebaseTool : ToolBase<IndexCodebaseRequest, IndexCodebaseResponse>
{
    private readonly InMemoryRaggableStore _store;
    private readonly Func<RaggableTreeBuilder> _builderFactory;

    public IndexCodebaseTool(
        InMemoryRaggableStore store,
        Func<RaggableTreeBuilder> builderFactory,
        ILogger<IndexCodebaseTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
    }

    public override string Name => "index_codebase";
    public override string Description => "Scan the codebase and build the full RaggableTree index.";

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

        var stopwatch = Stopwatch.StartNew();
        var builder = _builderFactory();
        var buildResult = await builder.BuildAsync(request.RootPath, request, cancellationToken).ConfigureAwait(false);

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
            FilterStats = new IndexFilterStats
            {
                TotalEnumerated = buildResult.FilterStats.TotalEnumerated,
                ExcludedByExcludeSet = buildResult.FilterStats.ExcludedByExcludeSet,
                ExcludedByGitignore = buildResult.FilterStats.ExcludedByGitignore,
                ExcludedByLanguage = buildResult.FilterStats.ExcludedByLanguage,
                ExcludedBySuffix = buildResult.FilterStats.ExcludedBySuffix,
                VfsFileEntriesYielded = buildResult.FilterStats.VfsFileEntriesYielded,
                PhysicalEntriesProbe = buildResult.FilterStats.PhysicalEntriesProbe,
                PhysicalRoot = buildResult.FilterStats.PhysicalRoot,
            },
        };
        }
    }
}
