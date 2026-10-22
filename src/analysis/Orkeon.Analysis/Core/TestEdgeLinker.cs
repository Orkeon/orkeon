using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Marks test nodes with the <c>test</c> tag and links each C# test method to the symbols it calls that live in
/// a project its own project references, with <see cref="EdgeKind.Tests"/> edges. Reads no file.
/// </summary>
internal static partial class TestEdgeLinker
{
    private const string TestTag = "test";

    public static bool IsTestCallName(string name) => name is "test" or "it";

    public static void MarkTest(RaggableNode node) => node.TagsMutable[TestTag] = "true";

    public static List<RaggableEdge> Link(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.Kind == UniversalNodeKind.Method && node.Decorators.Any(IsTestAttribute)) MarkTest(node);
        }

        var referenced = edges
            .Where(e => e.Kind == EdgeKind.ProjectDepends)
            .ToLookup(e => e.FromId, e => e.ToId, StringComparer.Ordinal);
        var linked = new List<RaggableEdge>();
        foreach (var call in edges.Where(e => e.Kind == EdgeKind.Calls))
        {
            if (!byId.TryGetValue(call.FromId, out var test) || test.Kind != UniversalNodeKind.Method
                || !test.Tags.ContainsKey(TestTag) || !byId.TryGetValue(call.ToId, out var target)) continue;
            var testProject = ProjectOf(test, byId);
            var targetProject = ProjectOf(target, byId);
            if (testProject is null || targetProject is null || !referenced[testProject].Contains(targetProject)) continue;
            linked.Add(new RaggableEdge($"tests::{test.Id}->{target.Id}", test.Id, target.Id, EdgeKind.Tests));
        }

        return linked;
    }

    private static string? ProjectOf(RaggableNode node, Dictionary<string, RaggableNode> byId)
    {
        var current = node;
        while (current.Kind != UniversalNodeKind.Package)
        {
            if (current.ParentId is null || !byId.TryGetValue(current.ParentId, out var parent)) return null;
            current = parent;
        }

        return current.Id;
    }

    private static bool IsTestAttribute(string decorator) => TestAttributePattern().IsMatch(decorator);

    [GeneratedRegex(@"[\[,\s](?:\w+\.)*(?:Fact|Theory)(?:Attribute)?\s*(?:[\](,]|$)")]
    private static partial Regex TestAttributePattern();
}
