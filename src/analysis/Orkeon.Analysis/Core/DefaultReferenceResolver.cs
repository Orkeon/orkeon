using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public sealed class DefaultReferenceResolver : IReferenceResolver
{
    public Task<ImmutableArray<RaggableEdge>> ResolveAsync(
        IReadOnlyList<UnresolvedRef> refs,
        IReadOnlyDictionary<string, RaggableNode> symbolsByFqn,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(refs);
        ArgumentNullException.ThrowIfNull(symbolsByFqn);

        var byShortName = BuildShortNameIndex(symbolsByFqn);
        var edges = ImmutableArray.CreateBuilder<RaggableEdge>();
        var seen = new HashSet<string>();

        foreach (var r in refs)
        {
            ct.ThrowIfCancellationRequested();
            if (!symbolsByFqn.TryGetValue(r.SourceFqn, out var source)) continue;
            if (string.IsNullOrWhiteSpace(r.RawTargetName)) continue;

            var target = ResolveTarget(source, r.RawTargetName, byShortName);
            var edgeKind = EdgeFromReference(r.Kind);
            if (edgeKind == EdgeKind.None) continue;

            var targetId = target?.Id ?? $"ext::unresolved::{r.RawTargetName}";
            var edgeId = $"{source.Id}→{targetId}:{edgeKind}";
            if (!seen.Add(edgeId)) continue;
            if (source.Id == targetId) continue;

            edges.Add(new RaggableEdge(edgeId, source.Id, targetId, edgeKind, r.CallSite));
        }

        return Task.FromResult(edges.ToImmutable());
    }

    private static RaggableNode? ResolveTarget(
        RaggableNode source,
        string name,
        Dictionary<string, List<RaggableNode>> byShortName)
    {
        if (!byShortName.TryGetValue(name, out var candidates) || candidates.Count == 0) return null;

        var sameFile = candidates.FirstOrDefault(c => c.VirtualFilePath == source.VirtualFilePath);
        if (sameFile is not null) return sameFile;

        return candidates[0];
    }

    private static EdgeKind EdgeFromReference(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Call => EdgeKind.Calls,
        ReferenceKind.Instantiate => EdgeKind.Calls,
        ReferenceKind.Import => EdgeKind.Imports,
        ReferenceKind.TypeRef => EdgeKind.Extends,
        _ => EdgeKind.None,
    };

    private static Dictionary<string, List<RaggableNode>> BuildShortNameIndex(
        IReadOnlyDictionary<string, RaggableNode> symbolsByFqn)
    {
        var index = new Dictionary<string, List<RaggableNode>>(StringComparer.Ordinal);
        foreach (var node in symbolsByFqn.Values)
        {
            if (node.Level != NodeLevel.L3_Symbol) continue;
            if (!index.TryGetValue(node.Name, out var bucket))
            {
                bucket = [];
                index[node.Name] = bucket;
            }
            bucket.Add(node);
        }
        return index;
    }
}
