using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>What a compilation produced: the DTOs the renderer writes, the configuration the validator judges.</summary>
internal sealed record ForgeCompilation
{
    /// <summary>Crew-level settings, per-entity <c>config.yaml</c> shape.</summary>
    public required CrewSettingsYamlConfig Settings { get; init; }

    /// <summary>Agents by key — one <c>agents/&lt;key&gt;.yaml</c> each.</summary>
    public required Dictionary<string, AgentYamlConfig> Agents { get; init; }

    /// <summary>Tasks by key — one <c>tasks/&lt;key&gt;.yaml</c> each.</summary>
    public required Dictionary<string, TaskYamlConfig> Tasks { get; init; }

    /// <summary>The domain configuration, ready for <c>CrewDefinitionValidator</c> and <c>ICrewFactory</c>.</summary>
    public required CrewConfiguration Configuration { get; init; }
}

/// <summary>
/// Compiles a blueprint into the very DTOs the YAML loader reads and the very configuration
/// the shared validator judges (SPEC-ORKEON-FORGE §8). One source of truth: what is written
/// to disk and what is validated derive from the same objects, so they cannot drift.
/// </summary>
internal static class ForgeBlueprintCompiler
{
    /// <summary>The deliverable source the forge writes when the delivering agent cannot write the file itself.</summary>
    internal const string FinalMessageSource = "final_message";

    /// <summary>The deliverable source the forge writes when every agent that may run the task writes the file itself.</summary>
    internal const string ToolCallSource = "tool_call";

    /// <summary>
    /// The agents that may run <paramref name="task"/> at run time (#392, D21): the agent it names in a
    /// sequential, parallel or graph crew; every agent but the manager in a hierarchical or consensual
    /// crew (the manager coordinates and runs no task); every agent in an autonomous crew. Mirrors
    /// <see cref="DeliverableToolCheck"/>'s reading of a loaded crew.
    /// </summary>
    internal static IReadOnlyList<ForgeBlueprintAgent> CandidatesOf(ForgeBlueprint blueprint, ForgeBlueprintTask task)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(task);

        var agents = blueprint.Agents ?? [];
        var process = string.IsNullOrWhiteSpace(blueprint.Crew?.Process) ? "sequential" : blueprint.Crew.Process.Trim();
        var known = ProcessType.All.FirstOrDefault(p => string.Equals(p.Value, process, StringComparison.OrdinalIgnoreCase));
        var bindsDeclaredAgent = known is null
            || known == ProcessType.Sequential || known == ProcessType.Parallel || known == ProcessType.Graph;
        if (bindsDeclaredAgent)
        {
            var declared = agents.Where(agent => string.Equals(agent.Key, task.Agent, StringComparison.Ordinal)).ToList();
            return declared.Count > 0 ? declared : agents;
        }

        return known!.AcceptsManagerAgent && !string.IsNullOrWhiteSpace(blueprint.Manager)
            ? agents.Where(agent => !string.Equals(agent.Key, blueprint.Manager, StringComparison.Ordinal)).ToList()
            : agents;
    }

    /// <summary>
    /// Whether <paramref name="agent"/>, running <paramref name="task"/>, holds a built-in tool that writes
    /// <paramref name="path"/> (<see cref="DeliverableToolCheck.CanWrite(IEnumerable{string}, string)"/>): its own
    /// tools and the ones the task grants, as the engine counts them. For a binary format only the tool that
    /// writes the format counts, <c>file_write</c>, which writes text, does not. The forge reads a path and
    /// compares tool names through the engine's helpers, so it and the load check never disagree (#392).
    /// </summary>
    internal static bool CanWrite(ForgeBlueprintAgent agent, ForgeBlueprintTask task, string path)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        return DeliverableToolCheck.CanWrite((agent.Tools ?? []).Concat(task.Tools ?? []), path);
    }

    /// <summary>
    /// The <c>deliverable.source</c> the forge writes for <paramref name="task"/> (#392): <c>tool_call</c> when
    /// every agent that may run it (<see cref="CandidatesOf"/>) writes the file itself, else
    /// <c>final_message</c>: the framework saves the agent's final answer to the path, and no tool is granted
    /// for it (least privilege). A binary deliverable is always <c>tool_call</c>: the answer's text cannot be
    /// a .docx, and <see cref="ForgeBlueprint.Validate"/> refuses the plan whose agent lacks the tool that
    /// writes the format (D22). The source follows from the plan, so a reopened team gets it back.
    /// </summary>
    internal static string EmittedDeliverableSource(ForgeBlueprint blueprint, ForgeBlueprintTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Deliverable is not { } path || DeliverableToolCheck.IsBinary(path))
            return ToolCallSource;

        var candidates = CandidatesOf(blueprint, task);
        return candidates.Count > 0 && candidates.All(agent => CanWrite(agent, task, path)) ? ToolCallSource : FinalMessageSource;
    }

    /// <summary>
    /// Compiles <paramref name="blueprint"/> (structurally valid — run
    /// <see cref="ForgeBlueprint.Validate"/> first: the mapper refuses a reference that names
    /// nothing by an exception) into the loader's DTO form and the domain configuration. The verdict is a separate step (<see cref="Validate"/>), so a
    /// render and its validation always look at the same compilation.
    /// </summary>
    public static ForgeCompilation Compile(ForgeBlueprint blueprint, IReadOnlyList<ForgeFolder>? folders = null)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        // The roots the crew reads and writes (VFS-90): the crew says what it expects, so a
        // run without the launcher's --mount refuses instead of silently writing nowhere. The
        // confirmed folder list is that set; a plan compiled without one — an
        // edit checked on its own — names what it addresses by itself.
        var expectedRoots = folders is { Count: > 0 }
            ? folders.Select(f => f.Path!).ToList()
            : ForgePromoter.DerivedMounts(blueprint).Select(m => m.VirtualRoot).ToList();

        var settings = new CrewSettingsYamlConfig
        {
            Name = blueprint.Crew?.Name,
            Goal = blueprint.Crew?.Goal,
            Process = blueprint.Crew?.Process ?? "sequential",
            Verbose = blueprint.Crew?.Verbose,
            Memory = blueprint.Crew?.Memory,
            ManagerAgent = blueprint.Manager,
            Mounts = expectedRoots.Count > 0 ? new Collection<string>(expectedRoots) : null,
        };

        var agents = new Dictionary<string, AgentYamlConfig>(StringComparer.Ordinal);
        foreach (var agent in blueprint.Agents ?? [])
        {
            agents[agent.Key!] = new AgentYamlConfig
            {
                Role = agent.Role,
                Goal = agent.Goal,
                Backstory = agent.Backstory,
                Tools = agent.Tools is { Count: > 0 } tools ? new Collection<string>([.. tools]) : null,
                AllowDelegation = agent.AllowDelegation,
                MaxIter = agent.EffectiveMaxIterations,
            };
        }

        var tasks = new Dictionary<string, TaskYamlConfig>(StringComparer.Ordinal);
        foreach (var task in blueprint.Tasks ?? [])
        {
            tasks[task.Key!] = new TaskYamlConfig
            {
                Description = task.Description,
                ExpectedOutput = task.ExpectedOutput,
                Agent = task.Agent,
                Dependencies = task.Dependencies is { Count: > 0 } deps ? new Collection<string>([.. deps]) : null,
                Tools = task.Tools is { Count: > 0 } taskTools ? new Collection<string>([.. taskTools]) : null,
                Deliverable = string.IsNullOrWhiteSpace(task.Deliverable)
                    ? null
                    : new DeliverableYamlConfig { Path = task.Deliverable, Source = EmittedDeliverableSource(blueprint, task) is FinalMessageSource ? FinalMessageSource : null },
            };
        }

        var mapper = new YamlCrewMapper(NullLogger.Instance);
        var configuration = mapper.BuildConfiguration(
            new CrewMappingSettings
            {
                Name = settings.Name,
                Goal = settings.Goal,
                Process = settings.Process,
                Verbose = settings.Verbose,
                Memory = settings.Memory,
                ManagerAgent = settings.ManagerAgent,
                Mounts = settings.Mounts,
            },
            agents,
            tasks);

        return new ForgeCompilation
        {
            Settings = settings,
            Agents = agents,
            Tasks = tasks,
            Configuration = configuration,
        };
    }

    /// <summary>
    /// The full validation verdict on a compiled blueprint: deliverables outside the confirmed
    /// folders, unknown tools, then the shared rules. The by-key references — the manager, each
    /// task's agent and dependencies — are checked before compilation
    /// (<see cref="ForgeBlueprint.Validate"/>): <c>YamlCrewMapper</c> refuses one that names
    /// nothing, and a manager in a mode that has none, by an exception (GAP-33). No errors = fit
    /// to run; warnings never block.
    /// </summary>
    public static ForgeValidationVerdict Validate(
        ForgeCompilation compilation,
        IReadOnlyCollection<string> knownTools,
        IReadOnlyList<ForgeFolder>? folders = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(knownTools);

        var errors = new List<string>();

        // Every deliverable lands in a confirmed output folder: the trial mounts
        // those and nothing else, and so does the adopted team.
        if (folders is { Count: > 0 })
            ValidateDeliverables(compilation, folders, errors);
        var known = knownTools as ISet<string> ?? new HashSet<string>(knownTools, StringComparer.Ordinal);

        foreach (var (key, agent) in compilation.Agents)
        {
            foreach (var tool in agent.Tools ?? [])
            {
                if (!known.Contains(tool))
                    errors.Add($"{ForgeErrorCodes.ToolUnknown}: agent '{key}' names tool '{tool}', which is not in the catalogue.");
            }
        }

        foreach (var (key, task) in compilation.Tasks)
        {
            foreach (var tool in task.Tools ?? [])
            {
                if (!known.Contains(tool))
                    errors.Add($"{ForgeErrorCodes.ToolUnknown}: task '{key}' names tool '{tool}', which is not in the catalogue.");
            }
        }

        var shared = CrewDefinitionValidator.Validate(compilation.Configuration);
        errors.AddRange(shared.Errors);

        return new ForgeValidationVerdict { Errors = errors, Warnings = shared.Warnings };
    }

    /// <summary>A deliverable outside every confirmed output folder, named with the folders it may use.</summary>
    private static void ValidateDeliverables(
        ForgeCompilation compilation, IReadOnlyList<ForgeFolder> folders, List<string> errors)
    {
        var outputs = string.Join(", ", folders.Where(f => !f.IsInput).Select(f => f.Path));
        foreach (var (key, task) in compilation.Tasks)
        {
            if (task.Deliverable?.Path is { Length: > 0 } deliverable && ForgeFolders.OutputOf(deliverable, folders) is null)
            {
                errors.Add(
                    $"Task '{key}' delivers '{deliverable}', which is not under a confirmed output folder. "
                    + $"Deliver under one of: {outputs}.");
            }
        }
    }
}

/// <summary>The outcome of validating a compiled blueprint.</summary>
internal sealed record ForgeValidationVerdict
{
    /// <summary>What blocks the cycle; the repair prompt carries these verbatim.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>What the user should see without being blocked.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>The public error codes of the forge cycle (SPEC-ORKEON-FORGE §6).</summary>
internal static class ForgeErrorCodes
{
    /// <summary>
    /// The settings declare a virtual root twice and the forge has no selector: no entry of it is
    /// mounted. A warning event, never an error (CH-12).
    /// </summary>
    public const string MountAmbiguous = "FORGE-MOUNT-AMBIGUOUS";

    /// <summary>The interview could not produce a schema-valid brief.</summary>
    public const string BriefIncomplete = "FORGE-BRIEF-INCOMPLETE";

    /// <summary>
    /// The folder list a client confirmed breaks the rules: a malformed or reserved
    /// root, a root twice, no output, a directory that is not there. Recoverable: the folders are
    /// proposed again.
    /// </summary>
    public const string FoldersInvalid = "FORGE-FOLDERS-INVALID";

    /// <summary>The assistant could not produce a schema-valid blueprint.</summary>
    public const string BlueprintInvalid = "FORGE-BLUEPRINT-INVALID";

    /// <summary>A blueprint names a tool absent from the injected catalogue.</summary>
    public const string ToolUnknown = "FORGE-TOOL-UNKNOWN";

    /// <summary>The rendered crew failed validation beyond the repair attempts.</summary>
    public const string ValidationFailed = "FORGE-VALIDATION-FAILED";

    /// <summary>A session file is missing or unreadable where the cycle needs it.</summary>
    public const string SessionCorrupt = "FORGE-SESSION-CORRUPT";

    /// <summary>The promotion could not write its folder; the session stays Ready, retryable.</summary>
    public const string PromoteFailed = "FORGE-PROMOTE-FAILED";

    /// <summary>
    /// A <c>warning</c> of <c>forge promote</c>: the team's <c>orkeon</c> command is
    /// longer than the 8 191 characters a <c>cmd</c> command holds once expanded, so its
    /// <c>run.cmd</c> launches nothing and says why when it is run; <c>run.sh</c> is complete. The
    /// promotion succeeds: Linux and macOS lose nothing.
    /// </summary>
    public const string LauncherTooLong = "FORGE-LAUNCHER-TOO-LONG";

    /// <summary>A command was asked of a session that is not where that command applies.</summary>
    public const string InvalidState = "FORGE-INVALID-STATE";

    /// <summary>
    /// The promotion is written, but the session folder could not take the team folder's name —
    /// a handle held open on Windows, an antivirus. Carried by a <c>warning</c>,
    /// never an <c>error</c>: the team is linked to its session by the id either way.
    /// </summary>
    public const string SessionNotRenamed = "FORGE-SESSION-NOT-RENAMED";

    /// <summary>
    /// <c>forge reopen</c> found no crew it can read back into a plan under the team folder
    /// (no folder, no YAML crew, a script crew, or files that do not describe a valid plan).
    /// The schedule verbs say it too of a folder that is not there, or holds no launcher.
    /// </summary>
    public const string TeamUnreadable = "FORGE-TEAM-UNREADABLE";

    /// <summary>
    /// <c>forge schedule</c> was asked to install the schedule of a folder that declares none: its
    /// <c>forge.json</c> records no <c>--schedule</c> of a promotion.
    /// </summary>
    public const string ScheduleNone = "FORGE-SCHEDULE-NONE";

    /// <summary>
    /// The operating system refused a schedule verb — a policy, no user session bus, no crontab
    /// binary. Carried by an <c>error</c> whose <c>command</c> is what a person can run instead.
    /// </summary>
    public const string ScheduleRefused = "FORGE-SCHEDULE-REFUSED";

    /// <summary>
    /// <c>forge schedule</c> found the name its folder takes already registered for another team
    /// folder that claims it: never replaced.
    /// </summary>
    public const string ScheduleNameTaken = "FORGE-SCHEDULE-NAME-TAKEN";

    /// <summary>
    /// A <c>warning</c>: the registration a folder's record names belongs to the folder it was
    /// copied from, and is left in place.
    /// </summary>
    public const string ScheduleNotOwned = "FORGE-SCHEDULE-NOT-OWNED";

    /// <summary>
    /// A <c>warning</c>: the registration a folder's record names was made on another operating
    /// system family, which this machine cannot act on.
    /// </summary>
    public const string ScheduleOtherSystem = "FORGE-SCHEDULE-OTHER-SYSTEM";

    /// <summary>
    /// A <c>warning</c>: the schedule is removed from the OS, but files that described it could not
    /// all be deleted.
    /// </summary>
    public const string ScheduleFilesKept = "FORGE-SCHEDULE-FILES-KEPT";

    /// <summary>
    /// A <c>warning</c> of <c>forge promote</c>: a re-adoption dropped the schedule, and this
    /// folder's registration still runs the team until <c>forge unschedule</c> removes it — a
    /// promotion never touches the OS. <c>forge rename</c> refuses with it too: the
    /// registration would be left running a folder that no longer exists.
    /// </summary>
    public const string ScheduleStillInstalled = "FORGE-SCHEDULE-STILL-INSTALLED";

    /// <summary>
    /// <c>forge rename</c>: the folder the new name gives the team is already
    /// there — another team, a folder, a file. Nothing moved; the message says what is there.
    /// </summary>
    public const string RenameTaken = "FORGE-RENAME-TAKEN";

    /// <summary>
    /// <c>forge rename</c>: the disk refused a step — a folder that would not
    /// move, a file that would not be written. Everything done before it was put back, and the
    /// message says so — or names what could not be.
    /// </summary>
    public const string RenameFailed = "FORGE-RENAME-FAILED";

    /// <summary>
    /// A <c>warning</c> of <c>forge rename</c>: the team's sidecar could not be read, so
    /// the name it records was left as it was; the rename itself stands.
    /// </summary>
    public const string RenameSidecarUnreadable = "FORGE-RENAME-SIDECAR-UNREADABLE";
}
