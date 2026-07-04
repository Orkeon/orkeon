using System.Globalization;
using System.Text;
using Orkeon.Domain.Flows;

namespace Orkeon.Infrastructure.Flows.Visualization;

/// <summary>
/// Converts flow definitions into graph representations and exports them to various formats.
/// </summary>
public static class FlowGraphSerializer
{
    /// <summary>
    /// Converts an <see cref="IFlowDefinition"/> to a <see cref="FlowGraph"/>.
    /// </summary>
    /// <param name="definition">The flow definition to serialize.</param>
    /// <returns>A graph representation of the flow.</returns>
    public static FlowGraph Serialize(IFlowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var nodes = definition.Steps.Select(step => new GraphNode(
            Id: step.Id,
            Name: step.Name,
            Type: step.Type ?? "unknown"
        )).ToList();

        var edges = new List<GraphEdge>();

        // Build edges from dependencies
        foreach (var step in definition.Steps)
        {
            foreach (var dep in step.Dependencies)
            {
                edges.Add(new GraphEdge(SourceId: dep, TargetId: step.Id));
            }
        }

        return new FlowGraph(definition.Name, nodes.AsReadOnly(), edges.AsReadOnly());
    }

    /// <summary>
    /// Exports a <see cref="FlowGraph"/> to Mermaid diagram syntax.
    /// </summary>
    /// <param name="graph">The graph to export.</param>
    /// <returns>A string containing the Mermaid diagram definition.</returns>
    public static string ExportToMermaid(FlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        foreach (var node in graph.Nodes)
        {
#pragma warning disable CA1308 // lowercase is the required wire/storage form, not a comparison normalization
            var shape = node.Type.ToLowerInvariant() switch
            {
                "conditional" => $"    {node.Id}{{{{{node.Name}}}}}",
                "crew" => $"    {node.Id}[[\"{node.Name}\"]]",
                _ => $"    {node.Id}[\"{node.Name}\"]"
            };
#pragma warning restore CA1308
            sb.AppendLine(shape);
        }

        foreach (var edge in graph.Edges)
        {
            if (edge.Label != null)
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {edge.SourceId} -->|{edge.Label}| {edge.TargetId}");
            else
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {edge.SourceId} --> {edge.TargetId}");
        }

        return sb.ToString().TrimEnd();
    }
}
