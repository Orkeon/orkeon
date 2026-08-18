using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Composes the text that gets embedded for a graph node (signature, summary, surrounding context).
/// </summary>
public interface IEmbeddingTextComposer
{
    string Compose(RaggableNode node, IReadOnlyDictionary<string, RaggableNode> allNodes);
    void ApplyToAll(IEnumerable<RaggableNode> nodes, IReadOnlyDictionary<string, RaggableNode> nodeIndex);
}
