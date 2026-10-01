using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The one place that decides which tools an agent holds while it runs a given task (GAP-07).
/// Every consumer — the three agent loops, the streaming path, the system prompt's tool section
/// and the guardrails' tool rules — reads the belt from here, so what the model is told it can
/// call and what it can actually call never diverge.
/// </summary>
/// <remarks>
/// The belt is the agent's tools, then the task's own <c>tools:</c> (they <b>add to</b> the
/// agent's for this task only, never replace them), then <c>human_input</c> when the task asks for
/// human input and the host registered that tool. A name appears once: the first holder wins,
/// compared case-insensitively. The agent's and the task's own instances are used as they are:
/// a crew loaded from YAML already holds the registry's instances, and an agent built in code
/// with its own instance of a tool the host also registers keeps its own (GAP-11) — the host's
/// registered tools only supply <c>human_input</c>.
/// </remarks>
public static class TaskToolbelt
{
    /// <summary>The name of the tool injected when a task declares <c>humanInput: true</c>.</summary>
    public const string HumanInputToolName = "human_input";

    /// <summary>Composes the tools <paramref name="agent"/> holds while it runs <paramref name="task"/>.</summary>
    /// <param name="agent">The executing agent.</param>
    /// <param name="task">The task being run.</param>
    /// <param name="registeredTools">The host's registered tools, or null when none are known.</param>
    /// <returns>The effective belt, without duplicate names, agent tools first.</returns>
    public static IReadOnlyList<IBaseTool> Compose(
        DomainAgent agent, CrewTask task, IEnumerable<IBaseTool>? registeredTools = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);

        var belt = new List<IBaseTool>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in agent.Tools.Concat(task.Tools))
        {
            if (names.Add(tool.Name))
                belt.Add(tool);
        }

        if (task.HumanInput
            && registeredTools?.FirstOrDefault(t => string.Equals(t.Name, HumanInputToolName, StringComparison.OrdinalIgnoreCase)) is { } humanInput
            && names.Add(humanInput.Name))
        {
            belt.Add(humanInput);
        }

        return belt;
    }
}
