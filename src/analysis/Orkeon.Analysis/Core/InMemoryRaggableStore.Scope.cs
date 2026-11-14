using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// The file-system scope of the agent whose tool call reads the store (#349): while
/// <see cref="AgentRunScope.EnforcedFileSystem"/> is set, every answer of the store leaves out the nodes
/// whose file the scope does not let the agent read, and whatever names them (an edge, a path, a cycle,
/// a hit, a ranking entry). One rule, <see cref="IsVisible"/>, decides for every query.
/// </summary>
public sealed partial class InMemoryRaggableStore
{
    /// <summary>
    /// Whether <paramref name="scope"/> lets the agent see <paramref name="node"/>: a node without a virtual
    /// path (an external symbol, an unresolved end, a page outline's <c>p3</c>) holds no file; a node whose
    /// file the scope grants <see cref="FileAccessRights.Read"/> on is visible; a monorepo or package node,
    /// a directory, is visible on the way to an entry of the scope (its name, never a file's content).
    /// </summary>
    /// <param name="node">The node, or <see langword="null"/> for an end the store does not hold.</param>
    /// <param name="scope">The scope in force.</param>
    internal static bool IsVisible(RaggableNode? node, AgentFileSystemScope scope)
    {
        if (node is null) return true;
        var path = node.VirtualFilePath;
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/')) return true;
        if (scope.Allows(path, FileAccessRights.Read)) return true;
        if (node.Level is not (NodeLevel.L0_Monorepo or NodeLevel.L1_Package)) return false;

        var directory = path.TrimEnd('/');
        return scope.Entries.Any(e => directory.Length == 0
            || e.Path.Equals(directory, StringComparison.Ordinal)
            || e.Path.StartsWith(directory + "/", StringComparison.Ordinal));
    }

    // Whether the scope of the tool call in progress hides the node; never outside a scoped tool call.
    private static bool Hidden(RaggableNode? node)
        => AgentRunScope.EnforcedFileSystem is { } scope && !IsVisible(node, scope);

    private bool HiddenKey(string key) => Hidden(Resolve(key));

    private bool EdgeHidden(RaggableEdge edge) => HiddenKey(edge.FromId) || HiddenKey(edge.ToId);

    private static IReadOnlyList<RaggableNode> Visible(IReadOnlyList<RaggableNode> nodes)
        => AgentRunScope.EnforcedFileSystem is null ? nodes : [.. nodes.Where(n => !Hidden(n))];

    private IReadOnlyList<RaggableEdge> Visible(IReadOnlyList<RaggableEdge> edges)
        => AgentRunScope.EnforcedFileSystem is null ? edges : [.. edges.Where(e => !EdgeHidden(e))];

    private SubGraph Visible(SubGraph graph)
    {
        if (AgentRunScope.EnforcedFileSystem is null) return graph;
        return graph with
        {
            Nodes = [.. graph.Nodes.Where(n => !Hidden(n))],
            Edges = [.. graph.Edges.Where(e => !EdgeHidden(e))],
        };
    }

    private bool AnyHidden(ImmutableArray<string> fqns)
        => AgentRunScope.EnforcedFileSystem is not null && fqns.Any(HiddenKey);

    private IReadOnlyList<CallPath> Visible(IReadOnlyList<CallPath> paths)
        => AgentRunScope.EnforcedFileSystem is null ? paths : [.. paths.Where(p => !AnyHidden(p.Fqns))];

    private IReadOnlyList<Cycle> Visible(IReadOnlyList<Cycle> cycles)
        => AgentRunScope.EnforcedFileSystem is null ? cycles : [.. cycles.Where(c => !AnyHidden(c.Fqns))];

    private IReadOnlyList<ComplexityEntry> Visible(IReadOnlyList<ComplexityEntry> entries)
        => AgentRunScope.EnforcedFileSystem is null ? entries : [.. entries.Where(e => !HiddenKey(e.Fqn))];

    private IReadOnlyList<CentralityEntry> Visible(IReadOnlyList<CentralityEntry> entries)
        => AgentRunScope.EnforcedFileSystem is null ? entries : [.. entries.Where(e => !HiddenKey(e.Fqn))];
}
