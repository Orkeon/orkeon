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

        var settings = new CrewSettingsYamlConfig
        {
            Name = blueprint.Crew?.Name,
            Goal = blueprint.Crew?.Goal,
            Process = blueprint.Crew?.Process ?? "sequential",
            Verbose = blueprint.Crew?.Verbose,
            Memory = blueprint.Crew?.Memory,
            ManagerAgent = blueprint.Manager,
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
}
