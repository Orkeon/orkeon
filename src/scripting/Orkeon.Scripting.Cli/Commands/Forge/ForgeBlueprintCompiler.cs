using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
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
    /// <summary>
    /// Compiles <paramref name="blueprint"/> (structurally valid — run
    /// <see cref="ForgeBlueprint.Validate"/> first) into the loader's DTO form and the
    /// domain configuration. The verdict is a separate step (<see cref="Validate"/>), so a
    /// render and its validation always look at the same compilation.
    /// </summary>
    public static ForgeCompilation Compile(ForgeBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        // The roots the blueprint reads and writes (VFS-90): the crew says what it expects, so
        // a run without the launcher's --mount refuses instead of silently writing nowhere.
        var expectedRoots = ForgePromoter.DeliverableMounts(blueprint).Select(m => m.VirtualRoot).ToList();

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
                MaxIter = agent.MaxIterations,
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
                Deliverable = string.IsNullOrWhiteSpace(task.Deliverable)
                    ? null
                    : new DeliverableYamlConfig { Path = task.Deliverable },
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
    /// The full validation verdict on a compiled blueprint: unknown tools, key references,
    /// then the shared rules. The by-key reference checks live here of necessity, not
    /// convenience — <c>YamlCrewMapper</c> maps an unresolvable <c>agent:</c> or dependency
    /// name to null/nothing, so by the time <c>CrewDefinitionValidator</c> looks, the error
    /// has been erased; only the pre-mapping side can still see it. No errors = fit to run;
    /// warnings never block.
    /// </summary>
    public static ForgeValidationVerdict Validate(
        ForgeCompilation compilation,
        IReadOnlyCollection<string> knownTools)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(knownTools);

        var errors = new List<string>();
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
            if (task.Agent is { } agent && !compilation.Agents.ContainsKey(agent))
                errors.Add($"Task '{key}' names agent '{agent}', which does not exist.");

            foreach (var dependency in task.Dependencies ?? [])
            {
                if (!compilation.Tasks.ContainsKey(dependency))
                    errors.Add($"Task '{key}' depends on '{dependency}', which does not exist.");
            }
        }

        if (compilation.Settings.ManagerAgent is { } manager && !compilation.Agents.ContainsKey(manager))
            errors.Add($"'manager' names agent '{manager}', which does not exist.");

        var shared = CrewDefinitionValidator.Validate(compilation.Configuration);
        errors.AddRange(shared.Errors);

        return new ForgeValidationVerdict { Errors = errors, Warnings = shared.Warnings };
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
    /// <summary>The interview could not produce a schema-valid brief.</summary>
    public const string BriefIncomplete = "FORGE-BRIEF-INCOMPLETE";

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

    /// <summary>A command was asked of a session that is not where that command applies.</summary>
    public const string InvalidState = "FORGE-INVALID-STATE";

    /// <summary>
    /// The promotion is written, but the session folder could not take the team folder's name —
    /// a handle held open on Windows, an antivirus (STUDIO-26, D-05). Carried by a <c>warning</c>,
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
    /// <c>forge.json</c> records no <c>--schedule</c> of a promotion (STUDIO-27).
    /// </summary>
    public const string ScheduleNone = "FORGE-SCHEDULE-NONE";

    /// <summary>
    /// The operating system refused a schedule verb — a policy, no user session bus, no crontab
    /// binary. Carried by an <c>error</c> whose <c>command</c> is what a person can run instead
    /// (STUDIO-27, D-04).
    /// </summary>
    public const string ScheduleRefused = "FORGE-SCHEDULE-REFUSED";

    /// <summary>
    /// <c>forge schedule</c> found the name its folder takes already registered for another team
    /// folder that claims it: never replaced (STUDIO-27).
    /// </summary>
    public const string ScheduleNameTaken = "FORGE-SCHEDULE-NAME-TAKEN";

    /// <summary>
    /// A <c>warning</c>: the registration a folder's record names belongs to the folder it was
    /// copied from, and is left in place (STUDIO-27).
    /// </summary>
    public const string ScheduleNotOwned = "FORGE-SCHEDULE-NOT-OWNED";

    /// <summary>
    /// A <c>warning</c>: the registration a folder's record names was made on another operating
    /// system family, which this machine cannot act on (STUDIO-27).
    /// </summary>
    public const string ScheduleOtherSystem = "FORGE-SCHEDULE-OTHER-SYSTEM";

    /// <summary>
    /// A <c>warning</c>: the schedule is removed from the OS, but files that described it could not
    /// all be deleted (STUDIO-27).
    /// </summary>
    public const string ScheduleFilesKept = "FORGE-SCHEDULE-FILES-KEPT";

    /// <summary>
    /// A <c>warning</c> of <c>forge promote</c>: a re-adoption dropped the schedule, and this
    /// folder's registration still runs the team until <c>forge unschedule</c> removes it — a
    /// promotion never touches the OS (STUDIO-27).
    /// </summary>
    public const string ScheduleStillInstalled = "FORGE-SCHEDULE-STILL-INSTALLED";
}
