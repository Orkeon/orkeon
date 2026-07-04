using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

public sealed class EmbeddingTextComposer : IEmbeddingTextComposer
{
    private const int DefaultMaxLength = 2000;
    private const int MaxRelations = 5;

    private readonly int _maxLength;

    public EmbeddingTextComposer(int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        _maxLength = maxLength;
    }

    public string Compose(RaggableNode node, IReadOnlyDictionary<string, RaggableNode> allNodes)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(allNodes);

        var sections = new List<string>();

        // 1. Kind + FQN
        sections.Add($"{node.EffectiveKind} {node.Fqn}".Trim());

        // 2. Signature
        if (!string.IsNullOrWhiteSpace(node.Signature)) sections.Add(node.Signature.Trim());

        // 3. DocComment (first sentence)
        if (!string.IsNullOrWhiteSpace(node.DocComment))
        {
            sections.Add(FirstSentence(node.DocComment));
        }

        // 4. Parent context
        if (node.ParentId is not null && allNodes.TryGetValue(node.ParentId, out var parent))
        {
            sections.Add($"member of {parent.EffectiveKind} {parent.Name}");
        }

        // 5. Key relations (imports + extends, max MaxRelations)
        var relations = BuildRelations(node, allNodes);
        if (relations.Count > 0) sections.Add(string.Join(", ", relations));

        // 6. Decorators
        if (node.Decorators.Count > 0)
        {
            sections.Add(string.Join(", ", node.Decorators));
        }

        // 7. Semantic summary
        if (!string.IsNullOrWhiteSpace(node.SemanticSummary)) sections.Add(node.SemanticSummary.Trim());

        var text = string.Join("\n", sections.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (text.Length > _maxLength) text = text[.._maxLength];
        return text;
    }

    public void ApplyToAll(
        IEnumerable<RaggableNode> nodes,
        IReadOnlyDictionary<string, RaggableNode> nodeIndex)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(nodeIndex);

        foreach (var node in nodes)
        {
            node.EmbeddingText = Compose(node, nodeIndex);
        }
    }

    private static List<string> BuildRelations(
        RaggableNode node,
        IReadOnlyDictionary<string, RaggableNode> index)
    {
        var relations = new List<string>();
        AppendRelationFqns(relations, node.ImportIds, "imports", index);
        AppendRelationFqns(relations, node.ExtendsIds, "extends", index);
        AppendRelationFqns(relations, node.ImplementsIds, "implements", index);
        return relations.Take(MaxRelations).ToList();
    }

    private static void AppendRelationFqns(
        List<string> sink,
        IReadOnlyList<string> ids,
        string label,
        IReadOnlyDictionary<string, RaggableNode> index)
    {
        foreach (var id in ids)
        {
            if (sink.Count >= MaxRelations) break;
            var fqn = index.TryGetValue(id, out var target) ? target.Fqn : id;
            sink.Add($"{label} {fqn}");
        }
    }

    private static string FirstSentence(string text)
    {
        var cleaned = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '*' || ch == '/') continue;
            cleaned.Append(ch);
        }
        var s = cleaned.ToString().Trim();
        var terminators = new[] { ". ", ".\n", "!\n", "?\n", "\n\n" };
        var earliest = s.Length;
        foreach (var t in terminators)
        {
            var idx = s.IndexOf(t, StringComparison.Ordinal);
            if (idx >= 0 && idx < earliest) earliest = idx + 1;
        }
        return s[..Math.Min(earliest, s.Length)].Trim();
    }
}
