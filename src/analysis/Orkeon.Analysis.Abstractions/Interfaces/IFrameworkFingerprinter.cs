using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface IFrameworkFingerprinter
{
    string Name { get; }
    void Apply(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges);
}
