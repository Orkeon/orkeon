using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class IncrementalReindexTool : ToolBase<IncrementalReindexRequest, IndexCodebaseResponse>
{
    private readonly InMemoryRaggableStore _store;
    private readonly Func<RaggableTreeBuilder> _builderFactory;
    private readonly IGitDiffProvider? _gitDiff;

    public IncrementalReindexTool(
        InMemoryRaggableStore store,
        Func<RaggableTreeBuilder> builderFactory,
        IGitDiffProvider? gitDiff = null,
        ILogger<IncrementalReindexTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
        _gitDiff = gitDiff;
    }

    public override string Name => "incremental_reindex";
    public override string Description => "Reindex only files changed since a prior commit or from an explicit list.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<IndexCodebaseResponse> ExecuteTypedAsync(IncrementalReindexRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<IndexCodebaseResponse> ExecuteTypedCoreAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            IReadOnlyList<string> changedFiles = request.ChangedFiles.IsDefaultOrEmpty
                ? await ResolveChangedFilesAsync(request, cancellationToken).ConfigureAwait(false)
                : request.ChangedFiles;

            if (changedFiles.Count == 0)
            {
                stopwatch.Stop();
                return new IndexCodebaseResponse
                {
                    IndexId = "unchanged",
                    NodeCount = 0,
                    EdgeCount = 0,
                    Elapsed = stopwatch.Elapsed,
                };
            }

            var rootPrefix = request.RootPath.TrimEnd('/');
            var virtualChanged = changedFiles
                .Select(f =>
                {
                    var normalized = f.Replace('\\', '/');
                    return normalized.StartsWith('/')
                        ? normalized.TrimEnd('/')
                        : $"{rootPrefix}/{normalized.TrimStart('/').TrimEnd('/')}";
                })
                .ToList();

            _store.RemoveFilesAndDescendants(virtualChanged);

            var builder = _builderFactory();
            var indexRequest = new IndexCodebaseRequest
            {
                RootPath = request.RootPath,
                Languages = request.Languages,
                EnrichWithLlm = request.EnrichWithLlm,
            };
            var buildResult = await builder.BuildAsync(request.RootPath, indexRequest, cancellationToken).ConfigureAwait(false);

            var changedSet = new HashSet<string>(virtualChanged, StringComparer.Ordinal);
            var freshNodes = buildResult.Tree.Nodes.Where(n => changedSet.Contains(n.VirtualFilePath)).ToList();
            _store.AddNodes(freshNodes);

            var survivorIds = new HashSet<string>(freshNodes.Select(n => n.Id), StringComparer.Ordinal);
            var freshEdges = buildResult.Tree.Edges
                .Where(e => survivorIds.Contains(e.FromId) || survivorIds.Contains(e.ToId))
                .ToList();
            _store.AddEdges(freshEdges);

            stopwatch.Stop();
            return new IndexCodebaseResponse
            {
                IndexId = buildResult.IndexId,
                NodeCount = freshNodes.Count,
                EdgeCount = freshEdges.Count,
                FileCount = virtualChanged.Count,
                Elapsed = stopwatch.Elapsed,
                Errors = ImmutableArray<string>.Empty,
            };
        }
    }

    private async Task<IReadOnlyList<string>> ResolveChangedFilesAsync(IncrementalReindexRequest request, CancellationToken ct)
    {
        if (_gitDiff is null || string.IsNullOrEmpty(request.FromCommit) || string.IsNullOrEmpty(request.ToCommit))
        {
            return [];
        }
        return await _gitDiff.GetChangedFilesAsync(request.RootPath, request.FromCommit, request.ToCommit, ct).ConfigureAwait(false);
    }
}
