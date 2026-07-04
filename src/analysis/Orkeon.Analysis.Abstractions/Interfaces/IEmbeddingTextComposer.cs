using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface IEmbeddingTextComposer
{
    string Compose(RaggableNode node, IReadOnlyDictionary<string, RaggableNode> allNodes);
    void ApplyToAll(IEnumerable<RaggableNode> nodes, IReadOnlyDictionary<string, RaggableNode> nodeIndex);
}
