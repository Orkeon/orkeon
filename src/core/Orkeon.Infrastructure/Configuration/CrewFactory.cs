using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Factory for creating domain Crew objects from configuration.
/// </summary>
public partial class CrewFactory : ICrewFactory
{
    private readonly ICrewDefinitionLoader _loader;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<CrewFactory> _logger;
    private readonly ICrewRepository _crewRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly bool _strictTools;

    /// <summary>Initializes a new instance of <see cref="CrewFactory"/>.</summary>
    /// <param name="loader">The crew definition loader.</param>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="crewRepository">The crew repository for persisting created crews.</param>
    /// <param name="agentRepository">The agent repository for persisting created agents.</param>
    /// <param name="taskRepository">The task repository for persisting created tasks.</param>
    /// <param name="options">
    /// Factory options; when omitted, tool resolution is lenient (missing tools are logged and skipped).
    /// See <see cref="CrewFactoryOptions.StrictTools"/>.
    /// </param>
    public CrewFactory(
        ICrewDefinitionLoader loader,
        IToolRegistry toolRegistry,
        ILogger<CrewFactory> logger,
        ICrewRepository crewRepository,
        IAgentRepository agentRepository,
        ITaskRepository taskRepository,
        IOptions<CrewFactoryOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        ArgumentNullException.ThrowIfNull(toolRegistry);
        _toolRegistry = toolRegistry;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(crewRepository);
        _crewRepository = crewRepository;
        ArgumentNullException.ThrowIfNull(agentRepository);
        _agentRepository = agentRepository;
        ArgumentNullException.ThrowIfNull(taskRepository);
        _taskRepository = taskRepository;
        _strictTools = options?.Value.StrictTools ?? false;
    }

    /// <inheritdoc />
    public Task<DomainCrew> CreateFromConfigAsync(
        CrewConfiguration config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return CreateFromConfigCoreAsync(config, ct);
    }

    private async Task<DomainCrew> CreateFromConfigCoreAsync(
        CrewConfiguration config,
        CancellationToken ct)
    {
        ValidateConfiguration(config);

        LogCreatingCrewWithAgentsAnd(config.Name, config.Agents.Count, config.Tasks.Count);

        var agentMap = await CreateAgentsAsync(config.Agents, ct).ConfigureAwait(false);
        var taskMap = CreateTasks(config.Tasks, agentMap);

        SetupTaskDependencies(config.Tasks, taskMap);
        AddTaskContextData(config.Tasks, taskMap);

        var crew = AssembleCrew(config, agentMap, taskMap);

        // Persist all aggregates so the orchestrator can retrieve them by id
        foreach (var agent in agentMap.Values)
            await _agentRepository.AddAsync(agent, ct).ConfigureAwait(false);
        foreach (var task in taskMap.Values)
            await _taskRepository.AddAsync(task, ct).ConfigureAwait(false);
        await _crewRepository.AddAsync(crew, ct).ConfigureAwait(false);

        LogSuccessfullyCreatedCrewWithId(config.Name, crew.Id);

        return crew;
    }

    private void ValidateConfiguration(CrewConfiguration config)
    {
        var validation = _loader.Validate(config);
        if (!validation.IsValid)
        {
            var errorMessages = string.Join("; ", validation.Errors);
            throw new InvalidOperationException($"Invalid crew configuration: {errorMessages}");
        }

        foreach (var warning in validation.Warnings)
        {
            LogCrewDefinitionWarning(warning);
        }
    }

    private async Task<Dictionary<string, DomainAgent>> CreateAgentsAsync(IReadOnlyList<AgentConfiguration> agentConfigs, CancellationToken ct)
    {
        var agentMap = new Dictionary<string, DomainAgent>();

        foreach (var agentConfig in agentConfigs)
        {
            ct.ThrowIfCancellationRequested();
            var resolvedTools = await ResolveToolsAsync(agentConfig.Tools).ConfigureAwait(false);

            var builder = new AgentBuilder()
                .Role(AgentRole.From(agentConfig.Role))
                .Goal(AgentGoal.From(agentConfig.Goal))
                .AllowDelegation(agentConfig.AllowDelegation)
                .MaxIterations(agentConfig.MaxIterations > 0 ? agentConfig.MaxIterations : AgentDefaults.MaxIterations)
                .MaxRpm(agentConfig.MaxRPM > 0 ? agentConfig.MaxRPM : 10)
                .Verbose(agentConfig.Verbose);

            if (!string.IsNullOrWhiteSpace(agentConfig.Backstory))
                builder.Backstory(agentConfig.Backstory);
            if (resolvedTools.Count > 0)
                builder.WithTools(resolvedTools);

            var agent = builder.Build();

            agentMap[agentConfig.Id] = agent;

            LogCreatedAgentWithId(agentConfig.Role, agent.Id);
        }

        return agentMap;
    }

    private Dictionary<string, CrewTask> CreateTasks(
        IReadOnlyList<TaskConfiguration> taskConfigs, Dictionary<string, DomainAgent> agentMap)
    {
        var taskMap = new Dictionary<string, CrewTask>();

        foreach (var taskConfig in taskConfigs)
        {
            var builder = new CrewTaskBuilder()
                .Description(TaskDescription.From(taskConfig.Description))
                .ExpectedOutput(taskConfig.ExpectedOutput)
                .Async(taskConfig.AsyncExecution)
                .HumanInput(taskConfig.HumanInput);

            if (taskConfig.AssignedAgentId is not null
                && agentMap.TryGetValue(taskConfig.AssignedAgentId.ToString(), out var assignedAgent))
            {
                builder.AssignTo(assignedAgent);
            }

            var task = builder.Build();

            if (taskConfig.Deliverable is not null)
                task.SetDeliverable(taskConfig.Deliverable);

            if (taskConfig.LlmOverride is not null)
                task.SetLlmOverride(taskConfig.LlmOverride);

            taskMap[taskConfig.Id] = task;

            LogCreatedTaskWithDomainId(taskConfig.Id, task.Id);
        }

        return taskMap;
    }

    private static void SetupTaskDependencies(
        IReadOnlyList<TaskConfiguration> taskConfigs, Dictionary<string, CrewTask> taskMap)
    {
        foreach (var taskConfig in taskConfigs)
        {
            if (taskConfig.Dependencies.Count == 0 || !taskMap.TryGetValue(taskConfig.Id, out var task))
                continue;

            foreach (var depId in taskConfig.Dependencies)
            {
                if (taskMap.TryGetValue(depId, out var depTask))
                    task.AddDependency(depTask.Id);
            }
        }
    }

    private static void AddTaskContextData(
        IReadOnlyList<TaskConfiguration> taskConfigs, Dictionary<string, CrewTask> taskMap)
    {
        foreach (var taskConfig in taskConfigs)
        {
            if (taskConfig.Context.Count == 0 || !taskMap.TryGetValue(taskConfig.Id, out var task))
                continue;

            foreach (var kvp in taskConfig.Context)
            {
                task.AddContext(kvp.Key, kvp.Value);
            }
        }
    }

    private static DomainCrew AssembleCrew(
        CrewConfiguration config,
        Dictionary<string, DomainAgent> agentMap,
        Dictionary<string, CrewTask> taskMap)
    {
        var builder = new CrewBuilder()
            .Goal(config.Goal)
            .Process(config.Process)
            .Verbose(config.Verbose)
            .Planning(config.Planning)
            .EnableMemory(config.Memory)
            .WithAgents(agentMap.Values)
            .WithTasks(taskMap.Values);

        if (config.Process == ProcessType.Hierarchical
            && config.ManagerAgentId is not null
            && agentMap.TryGetValue(config.ManagerAgentId.ToString(), out var managerAgent))
        {
            builder.WithManager(managerAgent);
        }

        return builder.Build();
    }

    /// <inheritdoc />
    public Task<DomainCrew> CreateFromFileAsync(
        string yamlFilePath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlFilePath);

        return CreateFromFileCoreAsync(yamlFilePath, ct);

        async Task<DomainCrew> CreateFromFileCoreAsync(string yamlFilePath, CancellationToken ct)
        {
            var config = await _loader.LoadFromFileAsync(yamlFilePath, ct).ConfigureAwait(false);
            return await CreateFromConfigAsync(config, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<DomainCrew> CreateFromDirectoryAsync(
        string directoryPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        return CreateFromDirectoryCoreAsync(directoryPath, ct);

        async Task<DomainCrew> CreateFromDirectoryCoreAsync(string directoryPath, CancellationToken ct)
        {
            var config = await _loader.LoadFromDirectoryAsync(directoryPath, ct).ConfigureAwait(false);
            return await CreateFromConfigAsync(config, ct).ConfigureAwait(false);
        }
    }

    private async Task<List<ITool>> ResolveToolsAsync(
        IReadOnlyList<string> toolNames)
    {
        var tools = new List<ITool>();
        if (toolNames == null || toolNames.Count == 0)
            return tools;

        List<string>? missing = null;
        foreach (var toolName in toolNames)
        {
            var tool = await _toolRegistry.GetToolByNameAsync(toolName).ConfigureAwait(false);
            if (tool is ITool domainTool)
            {
                tools.Add(domainTool);
            }
            else
            {
                (missing ??= []).Add(toolName);
                if (!_strictTools)
                    LogToolNotFoundInRegistry(toolName);
            }
        }

        if (_strictTools && missing is { Count: > 0 })
            throw await BuildMissingToolsExceptionAsync(missing).ConfigureAwait(false);

        return tools;
    }

    private async Task<InvalidOperationException> BuildMissingToolsExceptionAsync(IReadOnlyList<string> missing)
    {
        var available = await _toolRegistry.GetAllToolsAsync().ConfigureAwait(false);
        var availableNames = available.Count > 0
            ? string.Join(", ", available.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            : "(none registered)";
        return new InvalidOperationException(
            $"Crew configuration references unknown tool(s): {string.Join(", ", missing)}. " +
            $"Available tools: {availableNames}.");
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Creating crew '{CrewName}' with {AgentCount} agents and {TaskCount} tasks")]
    private partial void LogCreatingCrewWithAgentsAnd(object crewName, int agentCount, int taskCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Successfully created crew '{CrewName}' with id {CrewId}")]
    private partial void LogSuccessfullyCreatedCrewWithId(object crewName, object crewId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Crew definition warning: {Warning}")]
    private partial void LogCrewDefinitionWarning(object warning);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Created agent '{AgentRole}' with id {AgentId}")]
    private partial void LogCreatedAgentWithId(object agentRole, object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Created task '{TaskId}' with domain id {DomainTaskId}")]
    private partial void LogCreatedTaskWithDomainId(object taskId, object domainTaskId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Tool '{ToolName}' not found in registry; skipping.")]
    private partial void LogToolNotFoundInRegistry(object toolName);

}
