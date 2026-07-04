using Orkeon.Application.Interfaces;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Validates a mapped <see cref="CrewConfiguration"/> (basic fields, agents, tasks,
/// hierarchical-process rules) and detects circular task dependencies. Extracted from
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
        var warnings = new List<string>();

        ValidateCrewBasicFields(config, errors);
        ValidateAgents(config, errors);
        ValidateTasks(config, errors);
        ValidateHierarchicalProcess(config, errors, warnings);

        return new CrewDefinitionValidationResult(
            errors.Count == 0,
            errors.AsReadOnly(),
            warnings.AsReadOnly());
    }

    private static void ValidateCrewBasicFields(CrewConfiguration config, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            errors.Add("Crew name is required.");

        if (string.IsNullOrWhiteSpace(config.Goal))
            errors.Add("Crew goal is required.");
    }

    private static void ValidateAgents(CrewConfiguration config, List<string> errors)
    {
        if (config.Agents == null || config.Agents.Count == 0)
        {
            errors.Add("At least one agent is required.");
            return;
        }

        foreach (var agent in config.Agents)
        {
            if (string.IsNullOrWhiteSpace(agent.Role))
                errors.Add($"Agent '{agent.Id}' must have a role.");
            if (string.IsNullOrWhiteSpace(agent.Goal))
                errors.Add($"Agent '{agent.Id}' must have a goal.");
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
        if (string.IsNullOrWhiteSpace(task.Description))
            errors.Add($"Task '{task.Id}' must have a description.");

        if (string.IsNullOrWhiteSpace(task.ExpectedOutput))
            errors.Add($"Task '{task.Id}' must have an expected output.");

        if (task.AssignedAgentId != null && !agentIds.Contains(task.AssignedAgentId))
            errors.Add($"Task '{task.Id}' references unknown agent '{task.AssignedAgentId}'.");

        foreach (var dep in task.Dependencies)
        {
            if (!taskIds.Contains(dep))
                errors.Add($"Task '{task.Id}' references unknown dependency '{dep}'.");
        }
    }

    private static void ValidateHierarchicalProcess(
        CrewConfiguration config, List<string> errors, List<string> warnings)
    {
        if (config.Process != ProcessType.Hierarchical)
            return;

        if (config.ManagerAgentId == null)
        {
            warnings.Add("Hierarchical process without an explicit manager agent. The first agent will be used as manager.");
            return;
        }

        var agentIds = config.Agents?.Select(a => a.Id).ToHashSet() ?? [];
        if (!agentIds.Contains(config.ManagerAgentId))
            errors.Add($"Manager agent '{config.ManagerAgentId}' is not defined in agents.");
    }

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
