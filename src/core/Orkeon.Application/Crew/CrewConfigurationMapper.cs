using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Application.Crew;

/// <summary>
/// Exports a domain crew as a crew configuration. The other direction is the loaders' (YAML,
/// <c>.ork.ts</c>), which build crews through <c>CrewFactory</c>.
/// </summary>
public static class CrewConfigurationMapper
{
    /// <summary>
    /// Converts a domain crew to a crew configuration, including the full agent and task exports.
    /// </summary>
    /// <remarks>
    /// The crew aggregate references its agents and tasks by identifier only, so the
    /// materialized entities must be supplied by the caller (typically loaded from
    /// repositories). Every identifier registered on the crew must have a matching entity;
    /// a missing entity throws instead of silently truncating the exported configuration.
    /// Entities not referenced by the crew are ignored.
    /// <para>
    /// The export covers the documented YAML perimeter (what <c>YamlCrewMapper</c> reads).
    /// Properties without a domain-side representation are not exported: the crew
    /// <c>MemoryProvider</c> name and the crew-level graph block. A task's own <c>tools:</c> are
    /// exported by name, like an agent's.
    /// </para>
    /// </remarks>
    /// <param name="crew">The crew aggregate to export.</param>
    /// <param name="agents">The materialized agent entities referenced by <paramref name="crew"/>.</param>
    /// <param name="tasks">The materialized task entities referenced by <paramref name="crew"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an agent or task referenced by the crew is missing from the supplied entities.
    /// </exception>
    public static CrewConfiguration ToConfiguration(
        this DomainCrew crew,
        IEnumerable<DomainAgent> agents,
        IEnumerable<CrewTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(tasks);

        return new CrewConfiguration
        {
            Name = crew.Name ?? "Unnamed Crew",
            Goal = crew.Goal.Value,
            Process = crew.ProcessType,
            Verbose = crew.Verbose,
            Memory = crew.MemoryEnabled,
            Planning = crew.Planning,
            ManagerAgentId = crew.ManagerAgentId,
            Agents = ExportAgents(crew, agents),
            Tasks = ExportTasks(crew, tasks),
            ExecutionConfig = new ExecutionConfig
            {
                MaxRPM = crew.MaxRpm,
                DefaultTimeout = TimeSpan.FromMinutes(5),
                MaxRetries = AgentDefaults.MaxRetryLimit,
                EnableDebugMode = crew.Verbose,
                ExecutorSettings = new Dictionary<string, object>
                {
                    ["ManagerLlm"] = ManagerLlmOf(crew, agents)
                }
            }
        };
    }

    /// <summary>
    /// What the crew's manager runs on (GAP-19) — <c>provider:&lt;name&gt;</c> for the provider C# set
    /// with <c>WithManagerLlm</c> or the one its manager agent carries (GAP-34), else
    /// <c>profile:&lt;name&gt;</c> for its manager agent's profile —, or empty for a crew whose mode has
    /// no manager (an autonomous crew's manager has no agent).
    /// </summary>
    private static string ManagerLlmOf(DomainCrew crew, IEnumerable<DomainAgent> agents)
    {
        if (crew.ProcessType == ProcessType.Autonomous)
            return Interfaces.ManagerLlm.Describe(crew, managerAgent: null);
        if (crew.ProcessType != ProcessType.Hierarchical)
            return string.Empty;

        var managerAgent = crew.ManagerAgentId is { } managerId
            ? agents.FirstOrDefault(agent => agent.Id == managerId)
            : null;
        return Interfaces.ManagerLlm.Describe(crew, managerAgent);
    }

    private static List<AgentConfiguration> ExportAgents(DomainCrew crew, IEnumerable<DomainAgent> agents)
    {
        var agentsById = new Dictionary<AgentId, DomainAgent>();
        foreach (var agent in agents)
            agentsById[agent.Id] = agent;

        var result = new List<AgentConfiguration>(crew.Agents.Count);
        foreach (var agentId in crew.Agents)
        {
            if (!agentsById.TryGetValue(agentId, out var agent))
                throw new InvalidOperationException(
                    $"Agent '{agentId}' is referenced by crew '{crew.Id}' but was not provided to ToConfiguration; the export would be incomplete.");

            result.Add(ToAgentConfiguration(agent));
        }

        return result;
    }

    private static AgentConfiguration ToAgentConfiguration(DomainAgent agent)
    {
        return new AgentConfiguration
        {
            Id = agent.Id,
            Role = agent.Role.Value,
            Goal = agent.Goal.Value,
            Backstory = agent.Backstory?.Value ?? string.Empty,
            Tools = agent.Tools.Select(tool => tool.Name).ToList(),
            AllowDelegation = agent.AllowDelegation,
            MaxIterations = agent.MaxIterations,
            MaxRPM = agent.MaxRpm,
            Verbose = agent.Verbose,
            LlmConfig = agent.LlmConfig,
            Guardrails = agent.Guardrails,
            KnowledgeAttachments = agent.KnowledgeAttachments.ToList()
        };
    }

    private static List<TaskConfiguration> ExportTasks(DomainCrew crew, IEnumerable<CrewTask> tasks)
    {
        var tasksById = new Dictionary<TaskId, CrewTask>();
        foreach (var task in tasks)
            tasksById[task.Id] = task;

        var result = new List<TaskConfiguration>(crew.Tasks.Count);
        foreach (var taskId in crew.Tasks)
        {
            if (!tasksById.TryGetValue(taskId, out var task))
                throw new InvalidOperationException(
                    $"Task '{taskId}' is referenced by crew '{crew.Id}' but was not provided to ToConfiguration; the export would be incomplete.");

            result.Add(ToTaskConfiguration(task));
        }

        return result;
    }

    private static TaskConfiguration ToTaskConfiguration(CrewTask task)
    {
        return new TaskConfiguration
        {
            Id = task.Id,
            Description = task.Description.Value,
            ExpectedOutput = task.ExpectedOutput.Value,
            AssignedAgentId = task.AssignedAgent,
            Dependencies = task.Dependencies.ToList(),
            Tools = task.Tools.Select(tool => tool.Name).ToList(),
            AsyncExecution = task.AsyncExecution,
            HumanInput = task.HumanInput,
            Context = new Dictionary<string, object>(task.Context),
            Deliverable = task.Deliverable,
            LlmOverride = task.LlmOverride,
            Guardrails = task.Guardrails
        };
    }
}

