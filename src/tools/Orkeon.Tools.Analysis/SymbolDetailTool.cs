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
    public override string Description =>
        "What is this symbol? Returns the signature, doc comment and body metrics of one symbol by FQN (a hit of codebase_search), "
        + "and on expand its members (at most max_children, 50), callers, callees, statements and inheritance: what it extends and "
        + "implements, and what derives from or implements it (derived_by, implemented_by), as FQNs. For the source text, "
        + "node_read or symbol_source; for who is affected by changing it, impact_analysis.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    // A small model fills every optional field and sends 0: refuse it rather than return an empty result.
    protected override string? ValidateTypedRequest(SymbolDetailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxChildren < 1)
            return $"max_children must be at least 1 (got {request.MaxChildren}); omit it for the default of 50";
        if (request.MaxChildren > 200)
            return $"max_children must be at most 200 (got {request.MaxChildren}); omit it for the default of 50";
        return null;
    }

    protected override Task<SymbolDetailResponse> ExecuteTypedAsync(SymbolDetailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SymbolDetailResponse> ExecuteTypedCoreAsync()
        {
            var node = await _store.GetAsync(request.Fqn, cancellationToken).ConfigureAwait(false);
            if (node is null) throw await FqnSuggestions.BuildAsync(_store, request.Fqn, cancellationToken).ConfigureAwait(false);

            var expand = request.Expand;
            var maxChildren = request.MaxChildren;

            var (members, truncated) = await LoadMembersAsync(node, expand, maxChildren, cancellationToken).ConfigureAwait(false);
            var inheritance = await LoadInheritanceAsync(node, expand, cancellationToken).ConfigureAwait(false);

            var callers = expand.HasFlag(ExpandModes.Callers)
                ? await ResolveCompactAsync(node.CalledByIds, cancellationToken).ConfigureAwait(false)
                : ImmutableArray<CompactNode>.Empty;

            var callees = expand.HasFlag(ExpandModes.Callees)
                ? await ResolveCompactAsync(node.CallIds, cancellationToken).ConfigureAwait(false)
                : ImmutableArray<CompactNode>.Empty;

            var statements = await LoadStatementsAsync(node, expand, cancellationToken).ConfigureAwait(false);

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
                Extends = inheritance.Extends,
                Implements = inheritance.Implements,
                DerivedBy = inheritance.DerivedBy,
                ImplementedBy = inheritance.ImplementedBy,
                TopCallers = callers,
                TopCallees = callees,
                Statements = statements,
                Truncated = truncated,
            };
        }
    }

    private async Task<(ImmutableArray<CompactNode> Members, bool Truncated)> LoadMembersAsync(
        RaggableNode node, ExpandModes expand, int maxChildren, CancellationToken cancellationToken)
    {
        if (!expand.HasFlag(ExpandModes.Members)) return ([], false);

        var children = await _store.GetChildrenAsync(node.Id, cancellationToken).ConfigureAwait(false);
        var truncated = children.Count > maxChildren;
        if (truncated) children = [.. children.Take(maxChildren)];
        return ([.. children.Select(ToCompact)], truncated);
    }

    /// <summary>
    /// Both directions of the inheritance edges, as FQNs: an edge holds node ids, which every list resolves to the
    /// FQN of the node (an id no node holds, an external target, stays as it is).
    /// </summary>
    private async Task<Inheritance> LoadInheritanceAsync(
        RaggableNode node, ExpandModes expand, CancellationToken cancellationToken)
    {
        if (!expand.HasFlag(ExpandModes.Inheritance)) return new Inheritance([], [], [], []);

        // The id, not the FQN: two partial declarations share an FQN, never an id.
        var key = string.IsNullOrEmpty(node.Id) ? node.Fqn : node.Id;
        var outgoing = await _store.GetEdgesAsync(key, EdgeKind.Extends | EdgeKind.Implements, Direction.Forward, cancellationToken).ConfigureAwait(false);
        var incoming = await _store.GetEdgesAsync(key, EdgeKind.Extends | EdgeKind.Implements, Direction.Backward, cancellationToken).ConfigureAwait(false);

        return new Inheritance(
            await ToFqnsAsync(outgoing.Where(e => e.Kind == EdgeKind.Extends).Select(e => e.ToId), cancellationToken).ConfigureAwait(false),
            await ToFqnsAsync(outgoing.Where(e => e.Kind == EdgeKind.Implements).Select(e => e.ToId), cancellationToken).ConfigureAwait(false),
            await ToFqnsAsync(incoming.Where(e => e.Kind == EdgeKind.Extends).Select(e => e.FromId), cancellationToken).ConfigureAwait(false),
            await ToFqnsAsync(incoming.Where(e => e.Kind == EdgeKind.Implements).Select(e => e.FromId), cancellationToken).ConfigureAwait(false));
    }

    private async Task<ImmutableArray<string>> ToFqnsAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0) return [];

        var nodes = await _store.GetManyAsync(distinct, cancellationToken).ConfigureAwait(false);
        var fqnByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            if (string.IsNullOrEmpty(n.Fqn)) continue;
            fqnByKey.TryAdd(n.Id, n.Fqn);
            fqnByKey.TryAdd(n.Fqn, n.Fqn);
        }

        return [.. distinct.Select(id => fqnByKey.GetValueOrDefault(id, id)).Distinct(StringComparer.Ordinal)];
    }

    private sealed record Inheritance(
        ImmutableArray<string> Extends,
        ImmutableArray<string> Implements,
        ImmutableArray<string> DerivedBy,
        ImmutableArray<string> ImplementedBy);

    private async Task<ImmutableArray<StatementNode>> LoadStatementsAsync(
        RaggableNode node, ExpandModes expand, CancellationToken cancellationToken)
    {
        if (!expand.HasFlag(ExpandModes.Statements)) return [];

        var stmts = await _store.GetStatementsAsync(node.Id, cancellationToken).ConfigureAwait(false);
        return [.. stmts];
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
