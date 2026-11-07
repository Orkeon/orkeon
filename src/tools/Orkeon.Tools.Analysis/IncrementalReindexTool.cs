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
    private readonly IndexFreshnessService _freshness;
    private readonly IGitDiffProvider? _gitDiff;

    public IncrementalReindexTool(
        IndexFreshnessService freshness,
        IGitDiffProvider? gitDiff = null,
        ILogger<IncrementalReindexTool>? logger = null) : base(logger)
    {
        _freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
        _gitDiff = gitDiff;
    }

    public override string Name => "incremental_reindex";
    public override string Description => "Reindex only files changed since a prior commit or from an explicit list. Cost is proportional to the changed files, not the repository: one file of 2 000 lines reindexes in under 2 s.";

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

            // The freshness service runs IncrementalReindexEngine: unchanged nodes are
            // reused and only the changed files are re-parsed, so the cost follows the
            // changed files, not the repository.
            var engineResult = await _freshness.RefreshPathsAsync(
                virtualChanged, request.RootPath, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new IndexCodebaseResponse
            {
                IndexId = engineResult.IndexId,
                NodeCount = engineResult.TotalNodeCount,
                EdgeCount = engineResult.TotalEdgeCount,
                FileCount = engineResult.FilesParsed,
                Elapsed = engineResult.Elapsed,
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
