using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

/// <summary>
/// Shared fixture helpers for the contractual tool tests. Builds a small,
/// deterministic <see cref="InMemoryRaggableStore"/> graph so each tool can be
/// exercised against a stable input without spinning up the full indexing
/// pipeline. The goal is contract coverage (typed input/output, nominal +
/// error paths, stable output format) rather than pipeline coverage.
/// </summary>
internal static class TestGraph
{
    public const string Root = "/src";

    public static RaggableNode Monorepo(string virtualRoot = Root) => new()
    {
        Id = $"monorepo::{virtualRoot}",
        Kind = UniversalNodeKind.Monorepo,
        Name = virtualRoot,
        Fqn = virtualRoot,
        VirtualFilePath = virtualRoot,
        Range = new NodeRange(0, 0, 0, 0, 0),
        Level = NodeLevel.L0_Monorepo,
        Language = "multi",
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
    };

    public static RaggableNode Package(string fqn, string virtualFilePath, string? parentId = null) => new()
    {
        Id = $"{fqn}#package",
        Kind = UniversalNodeKind.Package,
        Name = fqn.Split("::")[^1],
        Fqn = fqn,
        VirtualFilePath = virtualFilePath,
        Range = new NodeRange(0, 0, 1, 1, 0),
        Level = NodeLevel.L1_Package,
        Language = "typescript",
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
        ParentId = parentId,
    };

    public static RaggableNode Module(string virtualFilePath, string language = "typescript", string? parentId = null) => new()
    {
        Id = $"{virtualFilePath}#module@0",
        Kind = UniversalNodeKind.Module,
        Name = virtualFilePath,
        Fqn = virtualFilePath,
        VirtualFilePath = virtualFilePath,
        Range = new NodeRange(0, 0, 1, 20, 0),
        Level = NodeLevel.L2_Module,
        Language = language,
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
        ParentId = parentId,
    };

    public static RaggableNode Symbol(
        string fqn,
        string virtualFilePath,
        UniversalNodeKind kind = UniversalNodeKind.Function,
        string language = "typescript",
        string? parentId = null,
        int startLine = 1,
        int endLine = 5,
        string signature = "",
        string doc = "",
        string source = "")
        => new()
        {
            Id = $"{fqn}#symbol",
            Kind = kind,
            Name = fqn.Split("::")[^1],
            Fqn = fqn,
            VirtualFilePath = virtualFilePath,
            Range = new NodeRange(0, 0, startLine, endLine, 0),
            Level = NodeLevel.L3_Symbol,
            Language = language,
            SourceSnippet = source,
            Sha256 = string.Empty,
            ParentId = parentId,
            Signature = signature,
            DocComment = doc,
        };

    public static RaggableEdge Edge(RaggableNode from, RaggableNode to, EdgeKind kind, string? id = null)
        => new(id ?? $"{from.Id}->{to.Id}:{kind}", from.Id, to.Id, kind);

    public static void Link(RaggableNode parent, params RaggableNode[] children)
    {
        foreach (var child in children) parent.ChildrenIdsMutable.Add(child.Id);
    }

    public static void RecordCall(RaggableNode caller, RaggableNode callee)
    {
        caller.CallIdsMutable.Add(callee.Id);
        callee.CalledByIdsMutable.Add(caller.Id);
    }

    public static InMemoryRaggableStore Store(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges)
        => new(nodes, edges, new FakeFileSystemService());

    public static StatementNode Statement(
        string id,
        string parentSymbolId,
        StatementKind kind,
        int startLine,
        string expression = "expr",
        string? condition = null)
        => new()
        {
            Id = id,
            ParentSymbolId = parentSymbolId,
            Kind = kind,
            StartLine = startLine,
            EndLine = startLine,
            Expression = expression,
            Condition = condition,
        };

    public static ImmutableArray<T> Arr<T>(params T[] items) => [.. items];
}
