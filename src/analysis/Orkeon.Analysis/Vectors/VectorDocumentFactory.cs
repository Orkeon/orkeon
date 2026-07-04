using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Vectors;

public static class VectorDocumentFactory
{
    public static IReadOnlyList<VectorDocument> FromTree(RaggableTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var outDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edge in tree.Edges)
        {
            outDegree[edge.FromId] = outDegree.GetValueOrDefault(edge.FromId) + 1;
            inDegree[edge.ToId] = inDegree.GetValueOrDefault(edge.ToId) + 1;
        }

        var documents = new List<VectorDocument>();
        foreach (var node in tree.Nodes)
        {
            if (node.Embedding is null || node.Embedding.Value.IsEmpty) continue;

            var text = string.IsNullOrEmpty(node.EmbeddingText) ? node.SourceSnippet : node.EmbeddingText;
            documents.Add(new VectorDocument(
                node.Id,
                text,
                node.Embedding.Value,
                BuildMetadata(node, inDegree, outDegree)));
        }
        return documents;
    }

    private static VectorMetadata BuildMetadata(
        RaggableNode node,
        IReadOnlyDictionary<string, int> inDegree,
        IReadOnlyDictionary<string, int> outDegree)
    {
        return new VectorMetadata
        {
            Kind = node.Kind.ToString(),
            Language = node.Language,
            VirtualFilePath = node.VirtualFilePath,
            Fqn = node.Fqn,
            Tags = [.. node.Tags.Keys],
            Decorators = [.. node.Decorators],
            HasParent = !string.IsNullOrEmpty(node.ParentId),
            ChildCount = node.ChildrenIds.Count,
            InDegree = inDegree.GetValueOrDefault(node.Id),
            OutDegree = outDegree.GetValueOrDefault(node.Id),
        };
    }
}
