using Orkeon.Application.Interfaces;
using Orkeon.Application.Memory;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Validates a mapped <see cref="CrewConfiguration"/> (basic fields, agents, tasks, the manager
/// and the modes that have one) and detects circular task dependencies. Extracted from
/// <see cref="YamlCrewDefinitionLoader"/> (R4.2 god-file decomposition) so the validation
/// and cycle-detection logic is testable in isolation.
/// </summary>
public static class CrewDefinitionValidator
{
    /// <summary>Runs the full validation pipeline over a crew configuration.</summary>
    public static CrewDefinitionValidationResult Validate(CrewConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        ValidateCrewBasicFields(config, errors);
        ValidateAgents(config, errors);
        ValidateTasks(config, errors);
        ValidateHierarchicalProcess(config, errors);
        ValidateManagerMode(config, errors);
        ValidateConsensualManager(config, errors);
        ValidateAsyncExecution(config, errors);
        ValidateMounts(config, errors);

        // The validator raises no warning of its own: a loader's go through its logger.
        return new CrewDefinitionValidationResult(
            errors.Count == 0,
            errors.AsReadOnly(),
            []);
    }

    /// <summary>
    /// The <c>mounts:</c> block (VFS-90): the format is the value object's, so what is left to
    /// check is that the block selects coherently — one root once, and never two different
    /// entries for one root.
    /// </summary>
    private static void ValidateMounts(CrewConfiguration config, List<string> errors)
    {
        if (config.Mounts is null)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in config.Mounts)
        {
            if (!seen.Add(reference.ToString()))
                errors.Add($"Crew mounts: '{reference}' is listed twice.");
        }

        foreach (var group in config.Mounts.Where(m => m.Id is not null)
                     .GroupBy(m => m.VirtualRoot, StringComparer.Ordinal))
        {
            var ids = group.Select(m => m.Id!.ToString()).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count > 1)
                errors.Add($"Crew mounts: two ids select '{group.Key}' ({string.Join(", ", ids)}); keep one.");
        }
    }

    private static void ValidateCrewBasicFields(CrewConfiguration config, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            errors.Add("Crew name is required.");

        if (string.IsNullOrWhiteSpace(config.Goal))
            errors.Add("Crew goal is required.");

        // A crew's maxRpm bounds its model requests per minute (GAP-38): zero or less would let none
        // through — it used to be replaced by 100 without a word. Left out, the crew has no limit.
        if (config.MaxRpm is <= 0)
        {
            errors.Add(
                $"Crew '{config.Name}' maxRpm: {config.MaxRpm} — the model requests the crew may make per minute must be 1 or " +
                "more. Leave maxRpm: out for no limit of its own.");
        }

        // memory: decides whether the crew remembers, memoryProvider: where (GAP-30): a provider
        // named for a crew without memory would hold nothing.
        if (!config.Memory && !string.IsNullOrWhiteSpace(config.MemoryProvider))
        {
            errors.Add(
                $"memoryProvider: '{config.MemoryProvider}' needs memory: true — the provider holds what the crew " +
                "remembers, and without memory: true (the default is false) the crew remembers nothing. " +
                "Add memory: true, or remove memoryProvider:.");
        }

        // A type no provider answers to fails the load, like an unknown tool (GAP-40): it used to run
        // the crew's memory on the volatile provider, with a warning at its first store.
        if (!string.IsNullOrWhiteSpace(config.MemoryProvider) && MemoryProviderTypes.Canonical(config.MemoryProvider) is null)
            errors.Add(MemoryProviderTypes.UnknownMessage("memoryProvider:", config.MemoryProvider.Trim()));
    }

    private static void ValidateAgents(CrewConfiguration config, List<string> errors)
    {
        if (config.Agents == null || config.Agents.Count == 0)
        {
            errors.Add("At least one agent is required.");
            return;
        }

        // An agent goes by its key — the one its author wrote —, else its identifier (GAP-39).
        foreach (var agent in config.Agents)
        {
            if (string.IsNullOrWhiteSpace(agent.Role))
                errors.Add($"Agent '{CrewEntryNames.Of(agent)}' must have a role.");
            if (string.IsNullOrWhiteSpace(agent.Goal))
                errors.Add($"Agent '{CrewEntryNames.Of(agent)}' must have a goal.");
            ValidateAgentLimits(agent, errors);
        }
    }

    /// <summary>
    /// An agent's <c>maxRpm</c> and <c>maxIter</c> (GAP-38): zero or less used to be replaced — by 10
    /// and 15 — without a word. Both are refused, naming the agent; left out, <c>maxRpm</c> is no limit
    /// of its own and <c>maxIter</c> the default.
    /// </summary>
    private static void ValidateAgentLimits(AgentConfiguration agent, List<string> errors)
    {
        if (agent.MaxRpm is <= 0)
        {
            errors.Add(
                $"Agent '{CrewEntryNames.Of(agent)}' maxRpm: {agent.MaxRpm} — the model requests the agent may make per minute must be 1 or " +
                "more. Leave maxRpm: out for no limit of its own.");
        }

        if (agent.MaxIterations <= 0)
        {
            errors.Add(
                $"Agent '{CrewEntryNames.Of(agent)}' maxIter: {agent.MaxIterations} — the turns the agent may take on a task must be 1 or " +
                $"more. Leave maxIter: out for the default ({Orkeon.Domain.Constants.Agent.AgentDefaults.MaxIterations}).");
        }
    }

    private static void ValidateTasks(CrewConfiguration config, List<string> errors)
    {
        if (config.Tasks == null || config.Tasks.Count == 0)
        {
            errors.Add("At least one task is required.");
            return;
        }

        var agentIds = config.Agents?.Select(a => a.Id).ToHashSet() ?? [];
        var taskIds = config.Tasks.Select(t => t.Id).ToHashSet();

        foreach (var task in config.Tasks)
        {
            ValidateSingleTask(task, agentIds, taskIds, errors);
        }

        if (HasCircularDependencies(config.Tasks))
            errors.Add("Circular task dependencies detected.");
    }

    private static void ValidateSingleTask(
        TaskConfiguration task, HashSet<AgentId> agentIds, HashSet<TaskId> taskIds, List<string> errors)
    {
        // A task goes by its key, else its identifier (GAP-39); a reference it cannot resolve names no
        // entry, so it keeps its identifier.
        var name = CrewEntryNames.Of(task);

        if (string.IsNullOrWhiteSpace(task.Description))
            errors.Add($"Task '{name}' must have a description.");

        if (string.IsNullOrWhiteSpace(task.ExpectedOutput))
            errors.Add($"Task '{name}' must have an expected output.");

        if (task.AssignedAgentId != null && !agentIds.Contains(task.AssignedAgentId))
            errors.Add($"Task '{name}' references unknown agent '{task.AssignedAgentId}'.");

        foreach (var dep in task.Dependencies)
        {
            if (!taskIds.Contains(dep))
                errors.Add($"Task '{name}' references unknown dependency '{dep}'.");
        }
    }

    /// <summary>
    /// A hierarchical crew read from a configuration names its manager, one of its agents: the
    /// manager assigns and reviews on that agent's <c>llm:</c> block (GAP-19). Only C# can give a
    /// manager a provider of its own instead (<c>CrewBuilder.WithManagerLlm</c>); this used to warn
    /// that the first agent would manage, and the crew builder then refused the crew.
    /// </summary>
    private static void ValidateHierarchicalProcess(CrewConfiguration config, List<string> errors)
    {
        if (config.Process != ProcessType.Hierarchical)
            return;

        if (config.ManagerAgentId == null)
        {
            errors.Add("Hierarchical process requires a manager agent: name one of the crew's agents as its manager "
                + "(managerAgent: in YAML, .manager(agent) in .ork.ts). It assigns and reviews on that agent's llm: block.");
            return;
        }

        var agentIds = config.Agents?.Select(a => a.Id).ToHashSet() ?? [];
        if (!agentIds.Contains(config.ManagerAgentId))
            errors.Add($"Manager agent '{config.ManagerAgentId}' is not defined in agents.");
    }

    /// <summary>
    /// A manager agent means something in Hierarchical and Consensual only (GAP-33): elsewhere it would
    /// run tasks like any other agent. The YAML and <c>.ork.ts</c> loaders refuse it first, naming the
    /// key as the author wrote it; this covers a configuration built in code, naming the agent by its
    /// role.
    /// </summary>
    private static void ValidateManagerMode(CrewConfiguration config, List<string> errors)
    {
        if (config.ManagerAgentId is null || config.Process.AcceptsManagerAgent)
            return;

        var manager = config.Agents?.FirstOrDefault(a => a.Id == config.ManagerAgentId);
        var named = manager is null ? $"'{config.ManagerAgentId}'" : $"'{manager.Role}'";
        errors.Add(
            $"Manager agent {named} is set, but the {config.Process.Value} process has no manager: the agent would only be " +
            "one more worker. Remove it, or use the Hierarchical process (the manager assigns each task and reviews its " +
            "output) or the Consensual one (it arbitrates the vote under the ManagerDecision fallback).");
    }

    /// <summary>
    /// A consensual crew's manager (the arbiter of the <c>ManagerDecision</c> fallback, GAP-04)
    /// must be one of its agents.
    /// </summary>
    private static void ValidateConsensualManager(CrewConfiguration config, List<string> errors)
    {
        if (config.Process != ProcessType.Consensual || config.ManagerAgentId is null)
            return;

        var agentIds = config.Agents?.Select(a => a.Id).ToHashSet() ?? [];
        if (!agentIds.Contains(config.ManagerAgentId))
            errors.Add($"Manager agent '{config.ManagerAgentId}' is not defined in agents.");
    }

    /// <summary>
    /// A task's <c>asyncExecution</c> is honoured by Sequential and accepted by Parallel only (GAP-22);
    /// the four other modes order their tasks themselves. The YAML and <c>.ork.ts</c> loaders refuse it
    /// first, naming the task as its author wrote it; this covers a configuration built in code, before
    /// <c>CrewFactory</c> builds or ingests anything — naming the task by its description.
    /// </summary>
    private static void ValidateAsyncExecution(CrewConfiguration config, List<string> errors)
    {
        if (config.Process.AcceptsAsyncExecution || config.Tasks is null)
            return;

        errors.AddRange(config.Tasks.Where(t => t.AsyncExecution).Select(task =>
            $"Task '{Abbreviate(task.Description)}' asks for asyncExecution, which the {config.Process.Value} process does not honour: " +
            "it orders its tasks itself. Use the Sequential process (an async task runs alongside the tasks after it) " +
            "or Parallel, or remove asyncExecution."));
    }

    /// <summary>A task description short enough for a message: cut at 60 characters with an ellipsis.</summary>
    private static string Abbreviate(string description) =>
        description.Length <= 60 ? description : string.Concat(description.AsSpan(0, 57), "...");

    /// <summary>Returns true when the task dependency graph contains at least one cycle.</summary>
    public static bool HasCircularDependencies(IReadOnlyList<TaskConfiguration> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var taskMap = tasks.ToDictionary(t => t.Id, t => t.Dependencies);
        var visited = new HashSet<TaskId>();
        var inStack = new HashSet<TaskId>();

        return taskMap.Keys.Any(taskId => DetectCycle(taskId, taskMap, visited, inStack));
    }

    private static bool DetectCycle(
        TaskId taskId,
        Dictionary<TaskId, IReadOnlyList<TaskId>> taskMap,
        HashSet<TaskId> visited,
        HashSet<TaskId> inStack)
    {
        if (inStack.Contains(taskId))
            return true;

        if (visited.Contains(taskId))
            return false;

        visited.Add(taskId);
        inStack.Add(taskId);

        if (taskMap.TryGetValue(taskId, out var deps))
        {
            foreach (var dep in deps)
            {
                if (taskMap.ContainsKey(dep) && DetectCycle(dep, taskMap, visited, inStack))
                    return true;
            }
        }

        inStack.Remove(taskId);
        return false;
    }
}
