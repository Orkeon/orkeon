using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Application.Crew;

/// <summary>
/// Maps between crew configuration models and domain entities.
/// </summary>
public static class CrewConfigurationMapper
{
    /// <summary>
    /// Converts a crew configuration to a domain crew.
    /// </summary>
    /// <param name="configuration">The crew configuration to materialize.</param>
    /// <param name="toolResolver">Resolves tool names to tool instances.</param>
    /// <param name="llmProfiles">
    /// The host's named LLM profiles: an agent or a task naming a profile it does not offer fails
    /// the mapping with the list of known ones (GAP-17). Null offers the default profile alone.
    /// </param>
    /// <param name="agentPostProcessor">Optional hook invoked with each materialized agent entity.</param>
    /// <param name="taskPostProcessor">Optional hook invoked with each materialized task entity.</param>
    /// <param name="logger">
    /// Optional logger for best-effort mapping warnings (unresolvable tools). Defaults to <see cref="NullLogger"/> — the mapper is an
    /// extension method, so DI callers pass their own logger explicitly.
    /// </param>
    public static DomainCrew ToDomainCrew(this CrewConfiguration configuration,
        Func<string, IBaseTool> toolResolver,
        ILlmProfileRegistry? llmProfiles,
        Action<DomainAgent>? agentPostProcessor = null,
        Action<CrewTask>? taskPostProcessor = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(toolResolver);

        logger ??= NullLogger.Instance;
        var crew = CreateCrew(configuration);
        MapAgents(configuration, crew, toolResolver, llmProfiles, agentPostProcessor, logger);
        MapTasks(configuration, crew, toolResolver, llmProfiles, taskPostProcessor, logger);

        return crew;
    }

    private static DomainCrew CreateCrew(CrewConfiguration configuration)
    {
        var builder = new CrewBuilder()
            .Goal(configuration.Goal ?? "Default goal")
            .Process(configuration.Process)
            .Verbose(configuration.Verbose)
            .Planning(configuration.Planning)
            .MaxRpm(configuration.ExecutionConfig?.MaxConcurrentTasks ?? 10)
            .EnableMemory(configuration.Memory);

        // Carry the declared memory provider onto the aggregate so it survives to kickoff (P2-O-02).
        if (!string.IsNullOrWhiteSpace(configuration.MemoryProvider))
            builder.WithMemoryProvider(configuration.MemoryProvider);

        return builder.Build();
    }

    private static void MapAgents(
        CrewConfiguration configuration,
        DomainCrew crew,
        Func<string, IBaseTool> toolResolver,
        ILlmProfileRegistry? llmProfiles,
        Action<DomainAgent>? agentPostProcessor,
        ILogger logger)
    {
        foreach (var agentConfig in configuration.Agents)
        {
            // A profile the host does not offer fails here, like an unknown tool under
            // StrictTools: the crew would otherwise fail at its first task, or not at all.
            LlmProfiles.EnsureKnown(llmProfiles, agentConfig.LlmConfig?.Profile, $"Agent '{agentConfig.Role}'");

            var resolvedTools = ResolveTools(agentConfig.Tools, toolResolver, logger);
            var agent = BuildAgent(agentConfig, resolvedTools);

            agentPostProcessor?.Invoke(agent);
            crew.AddAgent(agent.Id);
        }
    }

    /// <summary>
    /// Materializes a single agent entity from its configuration block: the always-present
    /// attributes are pushed onto the builder unconditionally, the optional ones only when
    /// the configuration actually carries a value.
    /// </summary>
    private static DomainAgent BuildAgent(
        AgentConfiguration agentConfig, List<IBaseTool> resolvedTools)
    {
        var builder = new AgentBuilder()
            .Role(AgentRole.From(agentConfig.Role))
            .Goal(AgentGoal.From(agentConfig.Goal))
            .AllowDelegation(agentConfig.AllowDelegation)
            .MaxIterations(agentConfig.MaxIterations)
            .MaxRpm(agentConfig.MaxRPM)
            .Verbose(agentConfig.Verbose);

        // AgentConfiguration.Backstory defaults to string.Empty and the AgentBackstory
        // value object rejects blank values, so an empty backstory means "no backstory".
        if (!string.IsNullOrWhiteSpace(agentConfig.Backstory))
            builder.Backstory(agentConfig.Backstory);
        if (agentConfig.SystemTemplate != null)
            builder.SystemTemplate(agentConfig.SystemTemplate);
        if (agentConfig.PromptTemplate != null)
            builder.PromptTemplate(agentConfig.PromptTemplate);
        if (agentConfig.ResponseTemplate != null)
            builder.ResponseTemplate(agentConfig.ResponseTemplate);
        if (resolvedTools.Count > 0)
            builder.WithTools(resolvedTools);
        if (agentConfig.Guardrails != null)
            builder.WithGuardrails(agentConfig.Guardrails);
        if (agentConfig.LlmConfig != null)
            builder.WithLlmConfig(agentConfig.LlmConfig);
        foreach (var attachment in agentConfig.KnowledgeAttachments)
            builder.WithKnowledge(attachment);

        return builder.Build();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort tool resolution: a tool resolver throwing for one name (unknown/misconfigured tool) is logged and skipped so a single bad tool name cannot abort mapping the whole crew configuration.")]
    private static List<IBaseTool> ResolveTools(
        IEnumerable<string> toolNames,
        Func<string, IBaseTool> toolResolver, ILogger logger)
    {
        var tools = new List<IBaseTool>();
        foreach (var toolName in toolNames)
        {
            try
            {
                var tool = toolResolver(toolName);
                if (tool is not null)
                    tools.Add(tool);
            }
            catch (Exception ex)
            {
                CrewConfigurationMapperLog.LogToolResolutionFailed(logger, ex, toolName);
            }
        }
        return tools;
    }

    private static void MapTasks(
        CrewConfiguration configuration, DomainCrew crew,
        Func<string, IBaseTool> toolResolver, ILlmProfileRegistry? llmProfiles,
        Action<CrewTask>? taskPostProcessor, ILogger logger)
    {
        foreach (var taskConfig in configuration.Tasks)
        {
            LlmProfiles.EnsureKnown(llmProfiles, taskConfig.LlmOverride?.Profile, $"Task '{(taskConfig.Description.Length <= 60 ? taskConfig.Description : string.Concat(taskConfig.Description.AsSpan(0, 57), "..."))}'");

            var builder = new CrewTaskBuilder()
                .Description(TaskDescription.From(taskConfig.Description))
                .ExpectedOutput(taskConfig.ExpectedOutput)
                .Async(taskConfig.AsyncExecution)
                .HumanInput(taskConfig.HumanInput)
                .WithTools(ResolveTools(taskConfig.Tools, toolResolver, logger));

            if (taskConfig.Guardrails is not null)
                builder.WithGuardrails(taskConfig.Guardrails);

            var task = builder.Build();
            if (taskConfig.LlmOverride is not null)
                task.SetLlmOverride(taskConfig.LlmOverride);

            taskPostProcessor?.Invoke(task);
            crew.AddTask(task.Id);
        }
    }

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
                MaxConcurrentTasks = crew.MaxRpm,
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

/// <summary>
/// Source-generated log messages for <see cref="CrewConfigurationMapper"/> (static class,
/// so the messages live in this satellite).
/// </summary>
internal static partial class CrewConfigurationMapperLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not resolve tool '{ToolName}'; the tool is skipped and crew mapping continues")]
    public static partial void LogToolResolutionFailed(ILogger logger, Exception ex, string toolName);
}
