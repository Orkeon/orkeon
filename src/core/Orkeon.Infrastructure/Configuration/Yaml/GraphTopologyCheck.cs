using Orkeon.Domain.Configuration;
using Orkeon.Domain.Graph;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// The shape faults of a crew's <c>graph:</c> block, from <see cref="GraphTopologyValidator"/> (ADR-018,
/// point 3): the YAML mapper calls it at load and the exporter before it writes, so the two give one text.
/// </summary>
internal static class GraphTopologyCheck
{
    private const string StartSentinel = "START";

    /// <summary>Every fault of the graph made of <paramref name="nodeIds"/> and <paramref name="edges"/>, starting at <paramref name="start"/> or at the first node.</summary>
    internal static IReadOnlyList<string> Faults(string? start, IReadOnlyList<string> nodeIds, IReadOnlyList<GraphEdgeConfig> edges)
    {
        var first = start ?? (nodeIds.Count > 0 ? nodeIds[0] : string.Empty);
        var described = new List<GraphEdgeDescription> { new(StartSentinel, [first], IsFixed: true) };
        foreach (var from in edges.GroupBy(e => e.From, StringComparer.Ordinal))
        {
            var single = from.Count() == 1 && from.First().When is null;
            described.Add(new GraphEdgeDescription(from.Key, [.. from.Select(e => e.To).Distinct(StringComparer.Ordinal)], single));
        }

        return GraphTopologyValidator.Validate(new GraphTopology(StartSentinel, GraphConfig.End, nodeIds, described));
    }
}
