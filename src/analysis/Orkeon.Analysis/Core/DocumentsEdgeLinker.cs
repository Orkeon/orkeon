using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Links each Markdown <see cref="UniversalNodeKind.Section"/> to the symbols it names (heading equal to the local name,
/// <c>code:</c> and <c>link:</c> tags set by the Markdown mapper) with <see cref="EdgeKind.Documents"/> edges. Reads no file.
/// </summary>
internal static class DocumentsEdgeLinker
{
    public const string CodeTag = "code:";
    public const string LinkTag = "link:";

    private const string MarkdownLanguage = "markdown";

    public static List<RaggableEdge> Link(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges)
    {
        var byLocalName = nodes
            .Where(n => n.Level > NodeLevel.L2_Module && n.Fqn.Length > 0 && n.Language != MarkdownLanguage)
            .ToLookup(n => LocalName(n.Fqn), StringComparer.Ordinal);
        var seen = edges.Where(e => e.Kind == EdgeKind.Documents).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var linked = new List<RaggableEdge>();
        foreach (var section in nodes.Where(n => n.Kind == UniversalNodeKind.Section))
        {
            var names = section.Tags.Keys
                .Where(k => k.StartsWith(CodeTag, StringComparison.Ordinal) || k.StartsWith(LinkTag, StringComparison.Ordinal))
                .Select(k => k[(k.IndexOf(':', StringComparison.Ordinal) + 1)..])
                .Append(section.Name);
            foreach (var target in names.Distinct(StringComparer.Ordinal).SelectMany(name => byLocalName[name]))
            {
                var id = $"documents::{section.Id}->{target.Id}";
                if (seen.Add(id)) linked.Add(new RaggableEdge(id, section.Id, target.Id, EdgeKind.Documents));
            }
        }

        return linked;
    }

    private static string LocalName(string fqn)
    {
        var scope = fqn.LastIndexOf("::", StringComparison.Ordinal);
        var tail = scope < 0 ? fqn : fqn[(scope + 2)..];
        return tail[(tail.LastIndexOf('.') + 1)..];
    }
}
