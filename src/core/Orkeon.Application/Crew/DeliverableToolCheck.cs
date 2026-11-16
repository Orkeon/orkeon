using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew;

/// <summary>
/// A task whose deliverable the agent must write itself (<see cref="DeliverableSource.ToolCall"/>, the
/// default) given to an agent that holds no <c>file_write</c> tool (#392).
/// </summary>
/// <param name="Task">The task's name: its key, else the start of its description.</param>
/// <param name="Agent">The role of the agent the task is assigned to.</param>
/// <param name="Path">The virtual path the deliverable declares.</param>
public sealed record DeliverableToolFinding(string Task, string Agent, string Path)
{
    /// <summary>The sentence a host reports: the task, the agent, the path and the two fixes.</summary>
    public string Message =>
        $"Task '{Task}' declares the deliverable '{Path}' (source tool_call), but its agent '{Agent}' has no "
        + $"{DeliverableToolCheck.FileWriteToolName} tool: the file will not be written. Give the agent "
        + $"{DeliverableToolCheck.FileWriteToolName}, or set deliverable.source: final_message.";
}

/// <summary>
/// The load-time check shared by the crew factory (a warning in the log of every run) and
/// <c>orkeon run --validate</c> (a <c>WARNING:</c> line): a task with a <c>tool_call</c> deliverable whose
/// agent cannot call <c>file_write</c> ends without its file, and nothing else says so before the run
/// (#392). A warning, not a refusal: a crew may write the file through another tool (an MCP server's, a
/// script tool), which the check cannot tell apart from a missing one.
/// </summary>
/// <remarks>
/// The check reads the tools the runtime offers the agent for the task: the agent's own, resolved by name
/// through the tool registry (built-ins, script tools and MCP tools alike, all registered before the crew
/// loads), plus the task's own <c>tools:</c>. It skips what it cannot know statically, so it never warns
/// wrongly: a task with no assigned agent (a hierarchical, consensual or autonomous crew whose manager
/// picks one at run time), and an agent on a provider that runs its own tools
/// (<see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmProviderCapabilities.RunsOwnTools"/>), whose
/// tool belt the provider holds, not Orkeon.
/// </remarks>
public static class DeliverableToolCheck
{
    /// <summary>The name of the tool a <c>tool_call</c> deliverable is written with.</summary>
    public const string FileWriteToolName = "file_write";

    /// <summary>
    /// The tasks of a built crew whose <c>tool_call</c> deliverable their agent cannot write, in task order.
    /// </summary>
    /// <param name="tasks">The crew's tasks.</param>
    /// <param name="agents">The crew's agents, the ones the tasks are assigned to among them.</param>
    /// <returns>One finding per such task; empty when none.</returns>
    public static IReadOnlyList<DeliverableToolFinding> Check(IEnumerable<CrewTask> tasks, IEnumerable<DomainAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(agents);

        var agentsById = new Dictionary<Orkeon.Domain.Common.AgentId, DomainAgent>();
        foreach (var agent in agents)
            agentsById[agent.Id] = agent;

        var findings = new List<DeliverableToolFinding>();
        foreach (var task in tasks)
        {
            if (task.Deliverable is not { Source: DeliverableSource.ToolCall, Path: { Length: > 0 } path }
                || string.IsNullOrWhiteSpace(path)
                || task.AssignedAgent is not { } agentId
                || !agentsById.TryGetValue(agentId, out var assigned)
                || assigned.Llm is { Capabilities.RunsOwnTools: true })
            {
                continue;
            }

            if (HasFileWrite(assigned.Tools) || HasFileWrite(task.Tools))
                continue;

            findings.Add(new DeliverableToolFinding(NameOf(task), assigned.Role.Value, path));
        }

        return findings;
    }

    private static bool HasFileWrite(IEnumerable<Orkeon.Domain.Tools.IBaseTool> tools)
        => tools.Any(tool => string.Equals(tool.Name, FileWriteToolName, StringComparison.OrdinalIgnoreCase));

    private static string NameOf(CrewTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.Key))
            return task.Key;

        var description = task.Description.Value;
        return description.Length <= 60 ? description : string.Concat(description.AsSpan(0, 57), "...");
    }
}
