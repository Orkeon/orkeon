using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Detects the frameworks and technologies a codebase uses (fingerprint phase).
/// </summary>
public interface IFrameworkFingerprinter
{
    string Name { get; }
    void Apply(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges);
}
