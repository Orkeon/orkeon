using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class CodebaseMapTool : ToolBase<CodebaseMapRequest, CodebaseMapResponse>
{
    private readonly IRaggableStore _store;
    private readonly IndexFreshnessService? _freshness;

    public CodebaseMapTool(
        IRaggableStore store,
        ILogger<CodebaseMapTool>? logger = null,
        IndexFreshnessService? freshness = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _freshness = freshness;
    }

    public override string Name => "codebase_map";
    public override string Description => "What is the shape of this codebase? Lists the entries of one level (L1_Package by default, L2_Module, L3_Symbol) with their kind and path, under root_fqn when given, at most max_entries (200), truncated when more exist; include_metrics adds counts and complexity. The FQN of an entry feeds package_summary and symbol_detail.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<CodebaseMapResponse> ExecuteTypedAsync(CodebaseMapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<CodebaseMapResponse> ExecuteTypedCoreAsync()
        {
            // Lazy freshness (PLAN B3): a map drawn over stale files misleads silently.
            if (_freshness is not null)
                await _freshness.EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
            // One entry past the page: truncated says that more entries exist, never that the page is full (#283).
            var maxEntries = Math.Max(1, request.MaxEntries);
            // The root goes into the query (a package or a folder by path, a namespace by its files, a type by FQN):
            // the page and truncated then describe the root's subset, not the first page of the whole level.
            var query = new NodeQuery
            {
                Level = request.Level,
                RootFqn = string.IsNullOrEmpty(request.RootFqn) ? null : request.RootFqn,
                Take = maxEntries + 1,
            };
            var nodes = await _store.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            var truncated = nodes.Count > maxEntries;
            if (truncated) nodes = [.. nodes.Take(maxEntries)];

            var entries = nodes.Select(n => MapEntry(n, request.IncludeMetrics)).ToImmutableArray();
            var totals = BuildTotals(nodes);
            return new CodebaseMapResponse
            {
                Level = request.Level,
                Entries = entries,
                Totals = totals,
                Truncated = truncated,
            };
        }
    }

    private static MapEntry MapEntry(RaggableNode node, bool includeMetrics)
    {
        int? cyclomatic = null;
        int? loc = null;
        if (includeMetrics)
        {
            cyclomatic = node.Body?.CyclomaticComplexity;
            var range = node.Range;
            if (range.EndLine > 0 && range.StartLine > 0) loc = range.EndLine - range.StartLine + 1;
        }
        return new MapEntry
        {
            Fqn = node.Fqn,
            Name = node.Name,
            Kind = node.EffectiveKind,
            Language = node.Language,
            ChildrenCount = node.ChildrenIds.Count > 0 ? node.ChildrenIds.Count : null,
            Cyclomatic = cyclomatic,
            LoC = loc,
        };
    }

    private static CodebaseTotals BuildTotals(IReadOnlyList<RaggableNode> nodes)
    {
        var packageCount = 0;
        var moduleCount = 0;
        var symbolCount = 0;
        foreach (var n in nodes)
        {
            switch (n.Level)
            {
                case NodeLevel.L1_Package: packageCount++; break;
                case NodeLevel.L2_Module: moduleCount++; break;
                case NodeLevel.L3_Symbol: symbolCount++; break;
            }
        }
        return new CodebaseTotals
        {
            NodeCount = nodes.Count,
            PackageCount = packageCount,
            ModuleCount = moduleCount,
            SymbolCount = symbolCount,
        };
    }
}
