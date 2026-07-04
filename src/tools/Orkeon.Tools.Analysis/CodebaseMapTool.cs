using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class CodebaseMapTool : ToolBase<CodebaseMapRequest, CodebaseMapResponse>
{
    private readonly IRaggableStore _store;

    public CodebaseMapTool(IRaggableStore store, ILogger<CodebaseMapTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "codebase_map";
    public override string Description => "Structural map of the codebase at a given zoom level (L0-L3).";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<CodebaseMapResponse> ExecuteTypedAsync(CodebaseMapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<CodebaseMapResponse> ExecuteTypedCoreAsync()
        {
            var query = new NodeQuery { Level = request.Level, Take = Math.Max(1, request.MaxEntries) };
            var nodes = await _store.QueryAsync(query, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(request.RootFqn))
            {
                var root = await _store.GetAsync(request.RootFqn, cancellationToken).ConfigureAwait(false);
                if (root is not null)
                {
                    // Scope by VirtualFilePath prefix rather than FQN prefix: L1 package FQN ends in
                    // "::name" while L2/L3 FQN embed the virtual path, so Fqn.StartsWith never matched
                    // across levels. Virtual paths always use '/' so the comparison is platform-stable.
                    var rootPath = root.VirtualFilePath;
                    if (string.IsNullOrEmpty(rootPath))
                    {
                        nodes = [.. nodes.Where(n => n.Fqn.StartsWith(root.Fqn, StringComparison.Ordinal))];
                    }
                    else
                    {
                        var prefix = rootPath.TrimEnd('/');
                        nodes = [.. nodes.Where(n =>
                            !string.IsNullOrEmpty(n.VirtualFilePath) &&
                            (n.VirtualFilePath.Equals(prefix, StringComparison.Ordinal)
                             || n.VirtualFilePath.StartsWith(prefix + "/", StringComparison.Ordinal)))];
                    }
                }
            }

            var entries = nodes.Select(n => MapEntry(n, request.IncludeMetrics)).ToImmutableArray();
            var totals = BuildTotals(nodes);
            var truncated = nodes.Count >= request.MaxEntries;
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
