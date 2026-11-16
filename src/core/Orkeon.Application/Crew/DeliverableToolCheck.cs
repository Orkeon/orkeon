using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew;

/// <summary>
/// A task whose deliverable the agent must write itself (<see cref="DeliverableSource.ToolCall"/>, the
/// default) while none of the agents that may run it holds a tool that writes that file
/// (<see cref="DeliverableFindingKind.NoWriter"/>), or a decision task that declares a deliverable, which
/// nothing writes (<see cref="DeliverableFindingKind.DecisionTask"/>) (#392).
/// </summary>
/// <param name="Task">The task's name: its key, else the start of its description.</param>
/// <param name="Agents">
/// The roles of the agents that may run the task, in crew order: the one it is assigned to when the
/// process binds it, else every agent the runtime may pick.
/// </param>
/// <param name="Path">The virtual path the deliverable declares.</param>
public sealed record DeliverableToolFinding(string Task, IReadOnlyList<string> Agents, string Path)
{
    /// <summary>What the finding is about.</summary>
    public DeliverableFindingKind Kind { get; init; } = DeliverableFindingKind.NoWriter;

    /// <summary>
    /// The sentence a host reports. For a missing writer: the task, the agents, the path and the fixes
    /// (the tool of the file's format to give; for a text file, <c>deliverable.source: final_message</c>
    /// as well). For a decision task: that the deliverable is never written.
    /// </summary>
    public string Message
    {
        get
        {
            if (Kind == DeliverableFindingKind.DecisionTask)
            {
                return $"Task '{Task}' is a decision task: its deliverable '{Path}' is never written, a decision runs no "
                    + "agent and writes no file. Remove deliverable:, or move it to a task that runs an agent.";
            }

            var writers = DeliverableToolCheck.WritersOf(Path);
            var named = writers.Count > 0
                ? string.Join(", ", writers)
                : $"no built-in tool writes '{DeliverableToolCheck.ExtensionOf(Path)}'";
            var tool = DeliverableToolCheck.PreferredWriterOf(Path);
            var binary = DeliverableToolCheck.IsBinary(Path);
            var single = Agents.Count == 1;
            var give = (tool, single) switch
            {
                (not null, true) => $"Give the agent {tool}",
                (not null, false) => $"Give {tool} to the agent meant to write it",
                (null, true) => "Give the agent a tool that writes it (an MCP server's, a plugin's)",
                (null, false) => "Give a tool that writes it (an MCP server's, a plugin's) to the agent meant to write it",
            };
            // A binary file is never the final answer's text: final_message is no fix for it.
            var other = binary
                ? "or deliver a text format (.md, .html, .csv) instead."
                : "or set deliverable.source: final_message.";
            var who = single
                ? $"its agent '{Agents[0]}' has no tool that writes it"
                : $"none of the agents that may run it ({string.Join(", ", Agents.Select(static role => $"'{role}'"))}) has a tool that writes it";
            return $"Task '{Task}' declares the deliverable '{Path}' (source tool_call), but {who} ({named}): "
                + $"the file may not be written. {give}, {other}";
        }
    }
}

/// <summary>What a <see cref="DeliverableToolFinding"/> is about.</summary>
public enum DeliverableFindingKind
{
    /// <summary>No agent that may run the task holds a tool that writes its deliverable.</summary>
    NoWriter,

    /// <summary>A decision task declares a deliverable, which nothing ever writes.</summary>
    DecisionTask,
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
/// (<see cref="LlmProviderCapabilities.RunsOwnTools"/>): the check cannot see into either. A binary file
/// (<see cref="IsBinary"/>) is written only by the tool of its format: <c>file_write</c> writes text, and does
/// not count for it.
/// </para>
/// <para>
/// A decision task runs no agent: a deliverable it declares, whatever its source, is reported as never
/// written (<see cref="DeliverableFindingKind.DecisionTask"/>), and its agents are not judged.
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
    // they write (empty: any text file), in the order a prompt prefers them: the tool of a format before
    // file_write. Left out: the editors (edit_file, edit_batch, structural_rewrite change a file that
    // exists), the downloaders (drive_download, email_save_attachment pick and sanitize the file name
    // themselves, and never overwrite) and shell_command, whose contract names no path.
    private static readonly (string Tool, string[] Extensions)[] s_writers =
    [
        ("docx_writer", [".docx"]),
        ("xlsx_writer", [".xlsx"]),
        ("image_generation", [".png", ".jpg", ".jpeg", ".webp"]),
        ("firefox", [".png"]),
        (FileWriteToolName, []),
    ];

    // The formats a final answer's text, or file_write's, cannot be: office documents, archives, images,
    // audio, video, databases, fonts and executables. The one list the prompt, the load check and the
    // forge read (#392); every extension a writer above writes is in it. A format that is plain text
    // (.rtf, .svg, .csv, .html) is not: file_write writes it.
    private static readonly HashSet<string> s_binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".dotx", ".dotm", ".doc", ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls",
        ".pptx", ".pptm", ".potx", ".ppsx", ".ppt", ".odt", ".ods", ".odp", ".odg", ".pdf", ".epub",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".ico", ".heic", ".avif", ".psd",
        ".zip", ".gz", ".tgz", ".tar", ".7z", ".rar", ".bz2", ".xz",
        ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".mp4", ".webm", ".mov", ".avi", ".mkv",
        ".sqlite", ".sqlite3", ".db", ".parquet", ".avro", ".orc", ".feather", ".npy", ".npz", ".pkl",
        ".woff", ".woff2", ".ttf", ".otf", ".exe", ".dll", ".so", ".dylib", ".bin", ".jar", ".wasm",
    };

    /// <summary>
    /// The built-in tools that write a file at a path the agent names: <c>docx_writer</c> (<c>.docx</c>),
    /// <c>xlsx_writer</c> (<c>.xlsx</c>), <c>image_generation</c> (an image), <c>firefox</c> (a <c>.png</c>
    /// screenshot) and <c>file_write</c> (any text file).
    /// </summary>
    public static IReadOnlyList<string> FileWriterToolNames { get; } = [.. s_writers.Select(static writer => writer.Tool)];

    /// <summary>
    /// Whether <paramref name="path"/> names a binary format (<c>.docx</c>, <c>.xlsx</c>, <c>.pdf</c>, an image,
    /// an archive...): one the final answer's text, or <c>file_write</c>'s, cannot be. Read from the extension,
    /// whatever its case; a name that starts with its only dot (<c>/output/.docx</c>) has none.
    /// </summary>
    /// <param name="path">The deliverable's virtual path.</param>
    public static bool IsBinary(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return s_binaryExtensions.Contains(ExtensionOf(path));
    }

    /// <summary>
    /// The built-in tools that can write the file at <paramref name="path"/>, the tool of its format first;
    /// <c>file_write</c> last, and never for a binary path (<see cref="IsBinary"/>). Empty when no built-in tool
    /// writes that binary format.
    /// </summary>
    /// <param name="path">The deliverable's virtual path.</param>
    public static IReadOnlyList<string> WritersOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = ExtensionOf(path);
        var binary = IsBinary(path);
        return [.. s_writers
            .Where(writer => writer.Extensions.Length == 0
                ? !binary
                : writer.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .Select(static writer => writer.Tool)];
    }

    /// <summary>
    /// The tool to give an agent that must write <paramref name="path"/>: the one made for its format when
    /// there is one (<c>docx_writer</c> for a <c>.docx</c>), else <c>file_write</c> for a text file; null for a
    /// binary format no built-in tool writes (<c>.pdf</c>).
    /// </summary>
    /// <param name="path">The deliverable's virtual path.</param>
    public static string? PreferredWriterOf(string path)
    {
        var writers = WritersOf(path);
        return writers.Count > 0 ? writers[0] : null;
    }

    /// <summary>
    /// The writer of <paramref name="path"/> an agent holding <paramref name="toolNames"/> is told to use: the
    /// first of <see cref="WritersOf"/> it holds, by its built-in name; null when it holds none.
    /// </summary>
    /// <param name="toolNames">The names of the tools the agent holds for the task.</param>
    /// <param name="path">The deliverable's virtual path.</param>
    public static string? WriterHeldFor(IEnumerable<string> toolNames, string path)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        var held = toolNames.ToList();
        return WritersOf(path).FirstOrDefault(writer => held.Contains(writer, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Whether one of <paramref name="toolNames"/> is a built-in tool that writes <paramref name="path"/>.</summary>
    /// <param name="toolNames">The names of the tools an agent holds for the task.</param>
    /// <param name="path">The deliverable's virtual path.</param>
    public static bool CanWrite(IEnumerable<string> toolNames, string path) => WriterHeldFor(toolNames, path) is not null;

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
            if (task.Deliverable is not { Path: { Length: > 0 } path } deliverable || string.IsNullOrWhiteSpace(path))
                continue;

            // A decision runs no agent and writes no file: its deliverable is never written, whatever its source,
            // and the agents' tools are beside the point (#392).
            if (task.IsDecision)
            {
                findings.Add(new DeliverableToolFinding(NameOf(task), [], path) { Kind = DeliverableFindingKind.DecisionTask });
                continue;
            }

            if (deliverable.Source != DeliverableSource.ToolCall)
                continue;

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

    /// <summary>The extension of the last segment of <paramref name="path"/>, dot included; empty when it has none.</summary>
    internal static string ExtensionOf(string path)
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
