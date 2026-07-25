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
    /// <param name="llmProviderFactory">Factory used to validate agent LLM configurations.</param>
    /// <param name="agentPostProcessor">Optional hook invoked with each materialized agent entity.</param>
    /// <param name="taskPostProcessor">Optional hook invoked with each materialized task entity.</param>
    public static DomainCrew ToDomainCrew(this CrewConfiguration configuration,
        Func<string, IBaseTool> toolResolver,
        ILlmProviderFactory llmProviderFactory,
        Action<DomainAgent>? agentPostProcessor = null,
        Action<CrewTask>? taskPostProcessor = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(toolResolver);
        ArgumentNullException.ThrowIfNull(llmProviderFactory);

        var crew = CreateCrew(configuration);
        MapAgents(configuration, crew, toolResolver, llmProviderFactory, agentPostProcessor);
        MapTasks(configuration, crew, taskPostProcessor);

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
        ILlmProviderFactory llmProviderFactory,
        Action<DomainAgent>? agentPostProcessor)
    {
        foreach (var agentConfig in configuration.Agents)
        {
            var resolvedTools = ResolveTools(agentConfig.Tools, toolResolver, configuration.Verbose);

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

            var agent = builder.Build();

            TryCreateLlmProvider(agent, agentConfig.LlmConfig, llmProviderFactory, configuration.Verbose);
            agentPostProcessor?.Invoke(agent);
            crew.AddAgent(agent.Id);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort tool resolution: a tool resolver throwing for one name (unknown/misconfigured tool) is logged and skipped so a single bad tool name cannot abort mapping the whole crew configuration.")]
    private static List<Domain.Common.ITool> ResolveTools(
        IEnumerable<string> toolNames,
        Func<string, IBaseTool> toolResolver, bool verbose)
    {
        var tools = new List<Domain.Common.ITool>();
        foreach (var toolName in toolNames)
        {
            try
            {
                var tool = toolResolver(toolName);
                if (tool is Domain.Common.ITool agentTool)
                    tools.Add(agentTool);
            }
            catch (Exception ex)
            {
                if (verbose)
                    Console.WriteLine($"Warning: Could not resolve tool '{toolName}': {ex.Message}");
            }
        }
        return tools;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort provider pre-validation: a factory failure (missing key/unknown provider) is logged as a warning so configuration mapping still completes; the provider is created for real later.")]
    private static void TryCreateLlmProvider(
        DomainAgent agent, LlmConfig? llmConfig,
        ILlmProviderFactory llmProviderFactory, bool verbose)
    {
        if (llmConfig == null)
            return;

        try
        {
            _ = llmProviderFactory.Create(llmConfig);
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.WriteLine($"Warning: Could not create LLM provider for agent '{agent.Id}': {ex.Message}");
        }
    }

    private static void MapTasks(
        CrewConfiguration configuration, DomainCrew crew, Action<CrewTask>? taskPostProcessor)
    {
        foreach (var taskConfig in configuration.Tasks)
        {
            var builder = new CrewTaskBuilder()
                .Description(TaskDescription.From(taskConfig.Description))
                .ExpectedOutput(taskConfig.ExpectedOutput)
                .Async(taskConfig.AsyncExecution)
                .HumanInput(taskConfig.HumanInput);

            if (taskConfig.Guardrails is not null)
                builder.WithGuardrails(taskConfig.Guardrails);

            var task = builder.Build();

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
    /// Properties without a domain-side representation are not exported: task
    /// <c>RequiredTools</c> (the entity stores opaque <see cref="ToolId"/> values, not tool
    /// names), per-task and crew-level circuit breaker blocks, the crew <c>MemoryProvider</c>
    /// name, and the crew-level graph block.
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
            Name = "Unnamed Crew", // Crew aggregate doesn't have Name property
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
                    ["ManagerLlm"] = crew.ManagerLlm?.GetType().Name ?? string.Empty
                }
            }
        };
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
            AsyncExecution = task.AsyncExecution,
            HumanInput = task.HumanInput,
            Context = new Dictionary<string, object>(task.Context),
            Deliverable = task.Deliverable,
            LlmOverride = task.LlmOverride,
            Guardrails = task.Guardrails
        };
    }
}
