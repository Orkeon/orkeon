using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class ImpactAnalysisTool : ToolBase<ImpactAnalysisRequest, ImpactAnalysisResponse>
{
    private const int TopCallersLimit = 10;
    private readonly IRaggableStore _store;

    public ImpactAnalysisTool(IRaggableStore store, ILogger<ImpactAnalysisTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "impact_analysis";
    public override string Description => "Who is affected if we change this symbol? Direct + transitive callers, grouped by package.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<ImpactAnalysisResponse> ExecuteTypedAsync(ImpactAnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<ImpactAnalysisResponse> ExecuteTypedCoreAsync()
        {
            var target = await _store.GetAsync(request.Target, cancellationToken).ConfigureAwait(false);
            if (target is null) throw await FqnSuggestions.BuildAsync(_store, request.Target, cancellationToken).ConfigureAwait(false);

            var direct = await _store.GetEdgesAsync(target.Fqn, EdgeKind.Calls, request.Direction, cancellationToken).ConfigureAwait(false);
            var directFqns = direct.Select(e => request.Direction == Direction.Backward ? e.FromId : e.ToId)
                                   .Distinct(StringComparer.Ordinal)
                                   .ToImmutableArray();

            var expand = await _store.ExpandAsync(
                new ExpandQuery
                {
                    Seeds = [target.Fqn],
                    EdgeKinds = [EdgeKind.Calls],
                    Direction = request.Direction,
                    MaxDepth = request.MaxDepth,
                    MaxNodes = request.MaxNodes,
                },
                cancellationToken).ConfigureAwait(false);

            var transitive = expand.Nodes
                .Where(n => n.Id != target.Id && n.Fqn != target.Fqn)
                .Select(n => n.Fqn)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();

            var byPackage = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in expand.Nodes)
            {
                var packageId = ExtractPackageFqn(node);
                if (string.IsNullOrEmpty(packageId)) continue;
                byPackage[packageId] = byPackage.GetValueOrDefault(packageId) + 1;
            }

            var topCallers = await ResolveTopCallersAsync(target, cancellationToken).ConfigureAwait(false);

            return new ImpactAnalysisResponse
            {
                Target = target.Fqn,
                DirectImpact = directFqns,
                TransitiveImpact = transitive,
                ByPackage = byPackage.ToImmutableDictionary(StringComparer.Ordinal),
                TopCallers = topCallers,
                Truncated = expand.Truncated,
            };
        }
    }

    private async Task<ImmutableArray<CompactNode>> ResolveTopCallersAsync(RaggableNode target, CancellationToken ct)
    {
        if (target.CalledByIds.Count == 0) return [];
        var ids = target.CalledByIds.Take(TopCallersLimit);
        var nodes = await _store.GetManyAsync(ids, ct).ConfigureAwait(false);
        return [.. nodes.Select(n => new CompactNode
        {
            Fqn = n.Fqn,
            Kind = n.EffectiveKind,
            SummaryShort = n.SemanticSummary,
            Signature = n.Signature,
        })];
    }

    private static string ExtractPackageFqn(RaggableNode node)
    {
        if (string.IsNullOrEmpty(node.Fqn)) return string.Empty;
        var parts = node.Fqn.Split("::", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return string.Empty;
        return string.Join("::", parts, 0, Math.Min(2, parts.Length));
    }
}
