using System.Text;
using Orkeon.Analysis.Abstractions.DTOs.Tools;

namespace Orkeon.Tools.Analysis.Internal;

internal static class MermaidGenerator
{
    public const int MaxNodesInDiagram = 50;

    public static string ToFlowchart(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        var rendered = nodes.Take(MaxNodesInDiagram).ToList();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rendered.Count; i++)
        {
            var node = rendered[i];
            var id = $"n{i}";
            ids[node.Fqn] = id;
            sb.Append("  ").Append(id).Append('[').Append('"').Append(node.Kind).Append(": ").Append(EscapeLabel(node.Name)).Append('"').AppendLine("]");
            if (node.IsSeed) sb.Append("  ").Append("style ").Append(id).AppendLine(" fill:#fef08a,stroke:#f59e0b");
        }
        foreach (var edge in edges)
        {
            if (!ids.TryGetValue(edge.From, out var fromId)) continue;
            if (!ids.TryGetValue(edge.To, out var toId)) continue;
            sb.Append("  ").Append(fromId).Append(" -->|\"").Append(edge.Kind).Append("\"| ").AppendLine(toId);
        }
        return sb.ToString();
    }

    public static string ToDot(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph G {");
        var rendered = nodes.Take(MaxNodesInDiagram).ToList();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rendered.Count; i++)
        {
            var node = rendered[i];
            var id = $"n{i}";
            ids[node.Fqn] = id;
            sb.Append("  ").Append(id).Append(" [label=\"").Append(EscapeLabel(node.Name)).Append('"');
            if (node.IsSeed) sb.Append(",style=filled,fillcolor=yellow");
            sb.AppendLine("];");
        }
        foreach (var edge in edges)
        {
            if (!ids.TryGetValue(edge.From, out var fromId)) continue;
            if (!ids.TryGetValue(edge.To, out var toId)) continue;
            sb.Append("  ").Append(fromId).Append(" -> ").Append(toId).Append(" [label=\"").Append(edge.Kind).AppendLine("\"];");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeLabel(string input)
    {
        return input.Replace("\"", "'", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    }
}
