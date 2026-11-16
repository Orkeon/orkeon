using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew;

/// <summary>
/// A task whose deliverable the agent must write itself (<see cref="DeliverableSource.ToolCall"/>, the
/// default) while none of the agents that may run it holds a tool that writes that file (#392).
/// </summary>
/// <param name="Task">The task's name: its key, else the start of its description.</param>
/// <param name="Agents">
/// The roles of the agents that may run the task, in crew order: the one it is assigned to when the
/// process binds it, else every agent the runtime may pick.
/// </param>
/// <param name="Path">The virtual path the deliverable declares.</param>
public sealed record DeliverableToolFinding(string Task, IReadOnlyList<string> Agents, string Path)
{
    /// <summary>The sentence a host reports: the task, the agents, the path and the two fixes.</summary>
    public string Message
    {
        get
        {
            var writers = string.Join(", ", DeliverableToolCheck.WritersOf(Path));
            var tool = DeliverableToolCheck.PreferredWriterOf(Path);
            return Agents.Count == 1
                ? $"Task '{Task}' declares the deliverable '{Path}' (source tool_call), but its agent '{Agents[0]}' has no "
                    + $"tool that writes it ({writers}): the file may not be written. Give the agent {tool}, or set "
                    + "deliverable.source: final_message."
                : $"Task '{Task}' declares the deliverable '{Path}' (source tool_call), but none of the agents that may "
                    + $"run it ({string.Join(", ", Agents.Select(static role => $"'{role}'"))}) has a tool that writes it "
                    + $"({writers}): the file may not be written. Give {tool} to the agent meant to write it, or set "
                    + "deliverable.source: final_message.";
        }
    }
}

/// <summary>
/// The load-time check shared by the crew factory (a warning in the log of every run) and
/// <c>orkeon run --validate</c> (a <c>WARNING:</c> line): a task with a <c>tool_call</c> deliverable that
/// no agent able to run it can write ends without its file, and nothing else says so before the run
/// (#392). A warning, not a refusal: the check knows what the tools Orkeon ships write, and nothing of
/// the others.
/// </summary>
/// <remarks>
/// <para>
/// An agent can write the deliverable when its tools for the task (its own, resolved through the tool
/// registry, plus the task's <c>tools:</c>) hold a built-in tool that writes a file of that kind at a given
/// path (<see cref="FileWriterToolNames"/>), or any tool Orkeon does not ship (an MCP server's, a script's,
/// a plugin's, a host's own), which may write it, or when its provider runs its own tools
/// (<see cref="LlmProviderCapabilities.RunsOwnTools"/>): the check cannot see into either.
/// </para>
/// <para>
/// The agents that may run a task follow the process. Sequential, parallel and graph crews run a task on
/// the agent it declares; a task that declares none (or one the crew does not carry) goes to the crew's
/// agents in turn (round-robin, or the semantic selector), so every agent may run it. A hierarchical or
/// consensual crew runs every task on the workers (every agent but the manager agent) the manager picks or
/// the vote involves, and an autonomous crew on any of its agents, whatever the task declares. The check
/// warns only when none of them can write the file.
/// </para>
/// </remarks>
public static class DeliverableToolCheck
{
    /// <summary>The name of the tool a <c>tool_call</c> deliverable is written with.</summary>
    public const string FileWriteToolName = "file_write";

    // The built-in tools that create or overwrite a file at a path the agent names, with the extensions
    // they write (empty: any). Left out: the editors (edit_file, edit_batch, structural_rewrite change a
    // file that exists), the downloaders (drive_download, email_save_attachment pick and sanitize the
    // file name themselves, and never overwrite) and shell_command, whose contract names no path.
    private static readonly (string Tool, string[] Extensions)[] s_writers =
    [
        (FileWriteToolName, []),
        ("docx_writer", [".docx"]),
        ("xlsx_writer", [".xlsx"]),
        ("image_generation", [".png", ".jpg", ".jpeg", ".webp"]),
        ("firefox", [".png"]),
    ];

    /// <summary>
    /// The built-in tools that write a file at a path the agent names: <c>file_write</c> (any file),
    /// <c>docx_writer</c> (<c>.docx</c>), <c>xlsx_writer</c> (<c>.xlsx</c>), <c>image_generation</c> (an image)
    /// and <c>firefox</c> (a <c>.png</c> screenshot).
    /// </summary>
    public static IReadOnlyList<string> FileWriterToolNames { get; } = [.. s_writers.Select(static writer => writer.Tool)];

    /// <summary>The built-in tools that can write the file at <paramref name="path"/>, <c>file_write</c> first.</summary>
    /// <param name="path">The deliverable's virtual path.</param>
    public static IReadOnlyList<string> WritersOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = ExtensionOf(path);
        return [.. s_writers
            .Where(writer => writer.Extensions.Length == 0 || writer.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .Select(static writer => writer.Tool)];
    }

    /// <summary>
    /// The tool to give an agent that must write <paramref name="path"/>: the one made for its format when
    /// there is one (<c>docx_writer</c> for a <c>.docx</c>), else <c>file_write</c>.
    /// </summary>
    /// <param name="path">The deliverable's virtual path.</param>
    public static string PreferredWriterOf(string path)
    {
        var writers = WritersOf(path);
        return writers.FirstOrDefault(static tool => tool != FileWriteToolName) ?? FileWriteToolName;
    }

    /// <summary>Whether one of <paramref name="toolNames"/> is a built-in tool that writes <paramref name="path"/>.</summary>
    /// <param name="toolNames">The names of the tools an agent holds for the task.</param>
    /// <param name="path">The deliverable's virtual path.</param>
    public static bool CanWrite(IEnumerable<string> toolNames, string path)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        var writers = WritersOf(path);
        return toolNames.Any(name => writers.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The tasks of a built crew whose <c>tool_call</c> deliverable no agent that may run them can write, in
    /// task order.
    /// </summary>
    /// <param name="tasks">The crew's tasks.</param>
    /// <param name="agents">The crew's agents, the ones the tasks are assigned to among them.</param>
    /// <param name="processType">The crew's process, which decides the agents that may run a task.</param>
    /// <param name="managerAgentId">The crew's manager agent, which runs no task, when it has one.</param>
    /// <returns>One finding per such task; empty when none.</returns>
    public static IReadOnlyList<DeliverableToolFinding> Check(
        IEnumerable<CrewTask> tasks, IEnumerable<DomainAgent> agents, ProcessType processType, AgentId? managerAgentId)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(processType);

        var crew = agents.ToList();
        var findings = new List<DeliverableToolFinding>();
        foreach (var task in tasks)
        {
            if (task.Deliverable is not { Source: DeliverableSource.ToolCall, Path: { Length: > 0 } path }
                || string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var candidates = CandidatesOf(task, crew, processType, managerAgentId);
            if (candidates.Count == 0 || candidates.Any(agent => CanWrite(agent, task, path)))
                continue;

            findings.Add(new DeliverableToolFinding(NameOf(task), [.. candidates.Select(static agent => agent.Role.Value)], path));
        }

        return findings;
    }

    /// <summary>The agents that may run <paramref name="task"/>, in crew order.</summary>
    private static List<DomainAgent> CandidatesOf(
        CrewTask task, List<DomainAgent> crew, ProcessType processType, AgentId? managerAgentId)
    {
        var bindsDeclaredAgent = processType == ProcessType.Sequential
            || processType == ProcessType.Parallel
            || processType == ProcessType.Graph;
        if (bindsDeclaredAgent)
        {
            if (task.AssignedAgent is { } assigned && crew.FirstOrDefault(agent => agent.Id == assigned) is { } declared)
                return [declared];
            return crew;
        }

        // The manager agent of a hierarchical or consensual crew coordinates; it runs no task.
        return processType.AcceptsManagerAgent && managerAgentId is { } manager
            ? crew.Where(agent => agent.Id != manager).ToList()
            : crew;
    }

    private static bool CanWrite(DomainAgent agent, CrewTask task, string path)
    {
        if (agent.Llm is { Capabilities.RunsOwnTools: true })
            return true;

        var tools = agent.Tools.Concat(task.Tools).ToList();
        return tools.Any(IsOpaque) || CanWrite(tools.Select(static tool => tool.Name), path);
    }

    /// <summary>
    /// A tool whose writes the check cannot know: one Orkeon does not ship (a plugin's, a host's own), an MCP
    /// server's (<c>McpToolAdapter</c>), a script's (<c>Orkeon.Scripting</c>) or a Microsoft Agent Framework
    /// agent called as a tool. A wrapper (an observer, a meter) is judged by the tool it wraps.
    /// </summary>
    private static bool IsOpaque(IBaseTool tool)
    {
        while (tool is IToolWrapper wrapper)
            tool = wrapper.Inner;

        var type = tool.GetType();
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        if (!assembly.StartsWith("Orkeon.", StringComparison.Ordinal)
            || assembly is "Orkeon.Scripting" or "Orkeon.Interop.AgentFramework")
        {
            return true;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Orkeon.Infrastructure.MCP.McpToolAdapter")
                return true;
        }

        return false;
    }

    private static string ExtensionOf(string path)
    {
        var slash = path.LastIndexOf('/');
        var dot = path.LastIndexOf('.');
        return dot > slash + 1 ? path[dot..] : string.Empty;
    }

    /// <summary>
    /// The name a task is reported under: its key, else the start of its description. The load check
    /// and the run-time logs share it, so the operator reads the same name at load and at run time.
    /// </summary>
    internal static string NameOf(CrewTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.Key))
            return task.Key;

        var description = task.Description.Value;
        return description.Length <= 60 ? description : string.Concat(description.AsSpan(0, 57), "...");
    }
}
