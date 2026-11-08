using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

public sealed partial class YamlCrewMapper
{
    /// <summary>
    /// The <c>graph:</c> block as a <see cref="GraphConfig"/> (ADR-018, points 5 to 7). Every fault of the
    /// block is collected and refused together at load, each naming the node or the task it is about: a
    /// <c>graph:</c> outside <c>process: graph</c>, a task a node cannot run, a <c>when</c> on a task that is
    /// no decision, a decision with no "otherwise", two "otherwise" edges, several edges on an ordinary task,
    /// a task-level <c>when</c> or <c>dependencies</c> beside nodes, and the shape faults of
    /// <see cref="Orkeon.Domain.Graph.GraphTopologyValidator"/>.
    /// </summary>
    private static GraphConfig? MapGraph(
        string crewName, ProcessType process, string? processText, GraphYamlConfig? yaml, Dictionary<string, TaskYamlConfig>? tasks)
    {
        if (yaml is null)
            return null;

        if (process != ProcessType.Graph)
        {
            throw new InvalidOperationException(
                $"Crew '{crewName}' declares graph:, which only process: graph reads, and its process is '{processText}'. " +
                "Remove graph, or set process: graph.");
        }

        var taskYaml = tasks ?? [];
        var faults = new List<string>();
        var nodes = MapGraphNodes(yaml, taskYaml, faults, out var nodeTask);
        var edges = MapGraphEdges(yaml, taskYaml, nodeTask, faults);
        var hasTopology = yaml.Nodes is not null || edges.Count > 0;

        if (hasTopology)
        {
            CheckTaskLevelRouting(taskYaml, faults);
            faults.AddRange(GraphTopologyCheck.Faults(yaml.Start, [.. nodeTask.Keys], edges));
        }
        else if (yaml.Start is not null)
        {
            faults.Add($"start '{yaml.Start}' needs nodes or edges: with neither, the crew runs one node per task.");
        }

        if (faults.Count > 0)
            throw new InvalidOperationException($"Crew '{crewName}' graph: " + string.Join(" ", faults));

        return new GraphConfig
        {
            Start = yaml.Start,
            Nodes = nodes,
            Edges = edges,
            CircuitBreakerPreset = yaml.CircuitBreakerPreset ?? "strict",
            MaxTransitions = yaml.MaxTransitions,
            MaxStateVisits = yaml.MaxVisits,
            MaxTotalDurationSeconds = yaml.MaxTotalDurationSeconds,
        };
    }

    /// <summary>The nodes written, or null for the implicit graph; <paramref name="nodeTask"/> holds every node id with its task, in order.</summary>
    private static List<GraphNodeConfig>? MapGraphNodes(
        GraphYamlConfig yaml, Dictionary<string, TaskYamlConfig> tasks, List<string> faults, out Dictionary<string, string> nodeTask)
    {
        nodeTask = new Dictionary<string, string>(StringComparer.Ordinal);
        if (yaml.Nodes is null)
        {
            foreach (var key in tasks.Keys)
                nodeTask[key] = key;
            return null;
        }

        var written = new List<GraphNodeConfig>();
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in yaml.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Task))
            {
                faults.Add($"A node needs an id and a task, got id '{node.Id}' and task '{node.Task}'.");
                continue;
            }

            written.Add(new GraphNodeConfig(node.Id, node.Task));
            if (!nodeTask.TryAdd(node.Id, node.Task))
            {
                faults.Add($"Node '{node.Id}' is declared twice.");
                continue;
            }

            if (!tasks.ContainsKey(node.Task))
                faults.Add($"Node '{node.Id}' names task '{node.Task}', which the crew does not have (tasks: {string.Join(", ", tasks.Keys)}).");
            else if (!named.TryAdd(node.Task, node.Id))
                faults.Add($"Node '{node.Id}' names task '{node.Task}', which node '{named[node.Task]}' already runs: a task is run by one node at most.");
        }

        foreach (var unnamed in tasks.Keys.Where(k => !named.ContainsKey(k)))
            faults.Add($"Task '{unnamed}' is run by no node: with nodes, every task of the crew is named by one.");

        return written;
    }

    private static List<GraphEdgeConfig> MapGraphEdges(
        GraphYamlConfig yaml, Dictionary<string, TaskYamlConfig> tasks, Dictionary<string, string> nodeTask, List<string> faults)
    {
        var edges = new List<GraphEdgeConfig>();
        foreach (var edge in yaml.Edges ?? [])
        {
            if (string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
            {
                faults.Add($"An edge needs a from and a to, got from '{edge.From}' and to '{edge.To}'.");
                continue;
            }

            TaskCondition? when = null;
            if (edge.When is not null)
            {
                var taskKey = nodeTask.GetValueOrDefault(edge.From);
                var task = taskKey is not null && tasks.TryGetValue(taskKey, out var found) ? found : null;
                when = MapEdgeCondition(edge, taskKey, task, faults);
            }

            edges.Add(new GraphEdgeConfig(edge.From, edge.To, when));
        }

        foreach (var from in edges.GroupBy(e => e.From, StringComparer.Ordinal).Where(g => nodeTask.ContainsKey(g.Key)))
        {
            var isDecision = tasks.TryGetValue(nodeTask[from.Key], out var task) && task.Decision is not null;
            var count = from.Count();
            var otherwise = from.Count(e => e.When is null);
            if (!isDecision && count > 1)
                faults.Add($"Node '{from.Key}' runs task '{nodeTask[from.Key]}', which is not a decision task, and has {count} edges: an ordinary task has exactly one.");
            else if (otherwise > 1)
                faults.Add($"Node '{from.Key}' has {otherwise} edges without when: only one edge is the otherwise.");
            else if (isDecision && otherwise == 0)
                faults.Add($"Node '{from.Key}' runs decision task '{nodeTask[from.Key]}' and has no edge without when: add the otherwise edge, taken when no when holds.");
        }

        return edges;
    }

    private static TaskCondition? MapEdgeCondition(
        GraphEdgeYamlConfig edge, string? taskKey, TaskYamlConfig? task, List<string> faults)
    {
        var when = edge.When!;
        var text = string.Join(", ", when.Select(c => $"{c.Key}: {Render(c.Value)}"));
        if (when.Count == 0)
        {
            faults.Add($"Node '{edge.From}' edge to '{edge.To}' has an empty when: name the decision answers, or remove the key.");
            return null;
        }

        if (task?.Decision is null)
        {
            if (taskKey is not null)
                faults.Add($"Node '{edge.From}' edge to '{edge.To}' has a when ({text}), but task '{taskKey}' is not a decision task: only a decision task routes on its answers.");
            return null;
        }

        try
        {
            var decisionTask = WhenClauseParser.DecisionTaskOf(when);
            if (decisionTask != taskKey)
                throw new ArgumentException($"an edge reads the decision task it leaves ('{taskKey}'), not '{decisionTask}'.");
            var condition = WhenClauseParser.Parse(when, MapDecision(taskKey!, task.Decision));
            if (condition.IsOtherwise)
                throw new ArgumentException("otherwise is not written on an edge: an edge without when is the otherwise.");
            return condition;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidOperationException)
        {
            faults.Add($"Node '{edge.From}' edge to '{edge.To}' when ({text}): {ex.Message}");
            return null;
        }
    }

    /// <summary>With nodes, the edges order the tasks: a task-level <c>when</c> or <c>dependencies</c> would be a second router.</summary>
    private static void CheckTaskLevelRouting(Dictionary<string, TaskYamlConfig> tasks, List<string> faults)
    {
        foreach (var (key, task) in tasks)
        {
            if (task.When is not null)
                faults.Add($"Task '{key}' declares when:, which a crew with graph nodes refuses: the edges route, a task-level when would be a second router.");
            if (task.Dependencies is { Count: > 0 })
                faults.Add($"Task '{key}' declares dependencies:, which a crew with graph nodes refuses: the edges order the tasks.");
        }
    }
}
