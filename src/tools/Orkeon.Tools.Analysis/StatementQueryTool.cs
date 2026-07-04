using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class StatementQueryTool : ToolBase<StatementQueryRequest, StatementQueryResponse>
{
    private readonly IRaggableStore _store;

    public StatementQueryTool(IRaggableStore store, ILogger<StatementQueryTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "statement_query";
    public override string Description => "Query L4 statements by kind, parent FQN, or semantic similarity.";

    protected override Task<StatementQueryResponse> ExecuteTypedAsync(StatementQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<StatementQueryResponse> ExecuteTypedCoreAsync()
        {
            var parents = request.ParentFqns.IsDefaultOrEmpty
                ? await CollectAllParentsAsync(cancellationToken).ConfigureAwait(false)
                : request.ParentFqns.ToList();

            var kinds = request.Kinds.IsDefaultOrEmpty ? null : new HashSet<StatementKind>(request.Kinds);
            var topK = Math.Max(1, request.TopK);

            var hits = await CollectHitsAsync(parents, kinds, topK, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(request.SemanticQuery))
            {
                hits = await RankBySemanticScoreAsync(hits, request.SemanticQuery, topK, cancellationToken).ConfigureAwait(false);
            }

            return new StatementQueryResponse
            {
                Hits = [.. hits.Take(topK)],
                Truncated = hits.Count >= topK,
            };
        }
    }

    private async Task<List<StatementHit>> CollectHitsAsync(
        List<string> parents,
        HashSet<StatementKind>? kinds,
        int topK,
        CancellationToken cancellationToken)
    {
        var hits = new List<StatementHit>();

        foreach (var parentFqn in parents)
        {
            if (hits.Count >= topK) break;
            var parent = await _store.GetAsync(parentFqn, cancellationToken).ConfigureAwait(false);
            if (parent is null) continue;
            var stmts = await _store.GetStatementsAsync(parent.Id, cancellationToken).ConfigureAwait(false);
            if (AppendStatementHits(hits, parent, stmts, kinds, topK)) break;
        }

        return hits;
    }

    private static bool AppendStatementHits(
        List<StatementHit> hits,
        RaggableNode parent,
        IEnumerable<StatementNode> stmts,
        HashSet<StatementKind>? kinds,
        int topK)
    {
        foreach (var stmt in stmts)
        {
            if (kinds is not null && !kinds.Contains(stmt.Kind)) continue;
            hits.Add(new StatementHit
            {
                ParentFqn = parent.Fqn,
                StatementId = stmt.Id,
                Kind = stmt.Kind,
                StartLine = stmt.StartLine,
                EndLine = stmt.EndLine,
                Expression = stmt.Expression,
                Condition = stmt.Condition,
            });
            if (hits.Count >= topK) return true;
        }

        return false;
    }

    private async Task<List<StatementHit>> RankBySemanticScoreAsync(
        List<StatementHit> hits,
        string semanticQuery,
        int topK,
        CancellationToken cancellationToken)
    {
        var semantic = await _store.SemanticSearchAsync(new SemanticQuery { Text = semanticQuery, TopK = topK }, cancellationToken).ConfigureAwait(false);
        var semanticByFqn = semantic.ToDictionary(h => h.Fqn, h => h.Score, StringComparer.Ordinal);
        return [.. hits
            .Select(h => semanticByFqn.TryGetValue(h.ParentFqn, out var score) ? h with { Score = score } : h)
            .OrderByDescending(h => h.Score ?? 0)];
    }

    private async Task<List<string>> CollectAllParentsAsync(CancellationToken ct)
    {
        var all = await _store.QueryAsync(new NodeQuery { Level = NodeLevel.L3_Symbol, Take = 1000 }, ct).ConfigureAwait(false);
        return all.Where(n => n.Statements.Count > 0).Select(n => n.Fqn).ToList();
    }
}
