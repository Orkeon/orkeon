using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class SymbolDetailTool : ToolBase<SymbolDetailRequest, SymbolDetailResponse>
{
    private const int TopLinksLimit = 10;
    private readonly IRaggableStore _store;

    public SymbolDetailTool(IRaggableStore store, ILogger<SymbolDetailTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "symbol_detail";
    public override string Description => "Expanded detail for an L3 symbol: signature, doc, body metrics, members, callers, callees, statements.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<SymbolDetailResponse> ExecuteTypedAsync(SymbolDetailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SymbolDetailResponse> ExecuteTypedCoreAsync()
        {
        var node = await _store.GetAsync(request.Fqn, cancellationToken).ConfigureAwait(false);
        if (node is null) throw await FqnSuggestions.BuildAsync(_store, request.Fqn, cancellationToken).ConfigureAwait(false);

        var expand = request.Expand;
        var truncated = false;
        var maxChildren = Math.Clamp(request.MaxChildren, 1, 200);

        ImmutableArray<CompactNode> members = [];
        if (expand.HasFlag(ExpandModes.Members))
        {
            var children = await _store.GetChildrenAsync(node.Id, cancellationToken).ConfigureAwait(false);
            if (children.Count > maxChildren) { truncated = true; children = [.. children.Take(maxChildren)]; }
            members = [.. children.Select(ToCompact)];
        }

        ImmutableArray<string> extends = [];
        ImmutableArray<string> implements = [];
        if (expand.HasFlag(ExpandModes.Inheritance))
        {
            var parents = await _store.GetEdgesAsync(node.Fqn, EdgeKind.Extends, Direction.Forward, cancellationToken).ConfigureAwait(false);
            extends = [.. parents.Select(e => e.ToId).Distinct(StringComparer.Ordinal)];
            var impls = await _store.GetEdgesAsync(node.Fqn, EdgeKind.Implements, Direction.Forward, cancellationToken).ConfigureAwait(false);
            implements = [.. impls.Select(e => e.ToId).Distinct(StringComparer.Ordinal)];
        }

        var callers = expand.HasFlag(ExpandModes.Callers)
            ? await ResolveCompactAsync(node.CalledByIds, cancellationToken).ConfigureAwait(false)
            : ImmutableArray<CompactNode>.Empty;

        var callees = expand.HasFlag(ExpandModes.Callees)
            ? await ResolveCompactAsync(node.CallIds, cancellationToken).ConfigureAwait(false)
            : ImmutableArray<CompactNode>.Empty;

        ImmutableArray<StatementNode> statements = [];
        if (expand.HasFlag(ExpandModes.Statements))
        {
            var stmts = await _store.GetStatementsAsync(node.Id, cancellationToken).ConfigureAwait(false);
            statements = [.. stmts];
        }

        return new SymbolDetailResponse
        {
            Fqn = node.Fqn,
            Name = node.Name,
            Kind = node.EffectiveKind,
            Level = node.Level,
            Signature = request.IncludeSignature ? node.Signature : null,
            DocComment = request.IncludeDoc ? node.DocComment : null,
            SummaryShort = node.SemanticSummary,
            Body = request.IncludeBodyMetrics ? node.Body : null,
            Members = members,
            Extends = extends,
            Implements = implements,
            TopCallers = callers,
            TopCallees = callees,
            Statements = statements,
            Truncated = truncated,
        };
        }
    }

    private async Task<ImmutableArray<CompactNode>> ResolveCompactAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var take = ids.Take(TopLinksLimit).ToList();
        var nodes = await _store.GetManyAsync(take, ct).ConfigureAwait(false);
        return [.. nodes.Select(ToCompact)];
    }

    private static CompactNode ToCompact(RaggableNode node) => new()
    {
        Fqn = node.Fqn,
        Kind = node.EffectiveKind,
        SummaryShort = node.SemanticSummary,
        Signature = node.Signature,
    };
}
