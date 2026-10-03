using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Knowledge;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Agent aggregate root representing an intelligent agent in the crew.
/// Delegates tool management to AgentToolManager. A run drives its task lifecycle (GAP-21):
/// <see cref="AssignTask"/>, <see cref="StartTask"/>, then <see cref="CompleteTask"/> or
/// <see cref="FailTask"/> — several tasks at once when the parallel mode runs a wave. What a crew
/// remembers is not held here: the crew's memory is stored and recalled by the memory coordinator.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724", Justification = "Agent is the core aggregate-root type; the Orkeon.Domain.Agent namespace deliberately shares the name. Renaming would break the entire public API.")]
public sealed class Agent : AggregateRoot<AgentId>
{
    private readonly List<IBaseTool> _tools;
    private readonly List<TaskId> _assignedTasks;
    private readonly List<TaskId> _currentTasks;
    private readonly List<KnowledgeAttachment> _knowledgeAttachments;
    private readonly AgentToolManager _toolManager;
    private Func<ICrewTask, IEnumerable<AgentId>, CancellationToken, System.Threading.Tasks.Task<AgentId?>>? _agentSelectionFunc;
    private CancellationTokenSource? _lifecycleCts;

    /// <summary>
    /// Gets the agent's role.
    /// </summary>
    public AgentRole Role { get; private set; }

    /// <summary>
    /// Gets the agent's goal.
    /// </summary>
    public AgentGoal Goal { get; private set; }

    /// <summary>
    /// Gets the agent's backstory.
    /// </summary>
    public AgentBackstory? Backstory { get; private set; }

    /// <summary>
    /// Gets whether the agent allows delegation.
    /// </summary>
    public bool AllowDelegation { get; private set; }

    /// <summary>
    /// Gets the maximum number of iterations for task execution.
    /// </summary>
    public int MaxIterations { get; private set; }

    /// <summary>
    /// Gets the maximum requests per minute.
    /// </summary>
    public int MaxRpm { get; private set; }

    /// <summary>
    /// Gets whether verbose logging is enabled.
    /// </summary>
    public bool Verbose { get; private set; }

    /// <summary>
    /// Gets the agent's current status.
    /// </summary>
    public AgentStatus Status { get; private set; }

    /// <summary>
    /// Gets the maximum execution time for tasks.
    /// </summary>
    public TimeSpan? MaxExecutionTime { get; private set; }

    /// <summary>
    /// Gets whether caching is enabled.
    /// </summary>
    public bool CacheEnabled { get; private set; } = true;

    /// <summary>
    /// Gets the system template for prompts.
    /// </summary>
    public string? SystemTemplate { get; private set; }

    /// <summary>
    /// Gets the prompt template.
    /// </summary>
    public string? PromptTemplate { get; private set; }

    /// <summary>
    /// Gets the response template.
    /// </summary>
    public string? ResponseTemplate { get; private set; }

    /// <summary>
    /// Gets the maximum retry limit.
    /// </summary>
    public int MaxRetryLimit { get; private set; } = AgentDefaults.MaxRetryLimit;

    /// <summary>
    /// The provider this agent's turns run on when it carries its own — CrewAI's <c>llm</c> given as
    /// an object: <c>AgentBuilder.WithLlm</c>, or a Microsoft Agent Framework agent through
    /// <c>WithAgentFrameworkAgent</c> (GAP-34). Its tasks and their correction round, its ballot and
    /// its work as a hierarchical manager run on it, metered as this agent's work where the run
    /// resolves it; a task's <c>llm_override</c> profile still moves that task. Null — the default —
    /// runs the agent on its <see cref="LlmConfig"/> profile, else the host's default. Exclusive with
    /// a profile, and a provider that runs its own tools
    /// (<see cref="SharedKernel.ValueObjects.LlmProviderCapabilities.RunsOwnTools"/>) leaves the agent
    /// no Orkeon tool and no delegation (<see cref="AgentLlmRules"/>).
    /// </summary>
    public ILlmProvider? Llm { get; private set; }

    /// <summary>
    /// Optional per-agent LLM configuration. Set when a YAML crew declares an agent-level
    /// <c>llm:</c> block; the executor merges these values onto the chat options used for
    /// every LLM call this agent makes (Experiment 07 friction #7).
    /// </summary>
    public SharedKernel.ValueObjects.LlmConfig? LlmConfig { get; private set; }

    /// <summary>
    /// Gets the tool access control policy for this agent.
    /// </summary>
    public ToolAccessPolicy ToolAccessPolicy { get; private set; }

    /// <summary>
    /// Gets the configurable guardrails injected into the agent's system prompt.
    /// Null means no guardrails are applied.
    /// </summary>
    public GuardrailsConfig? Guardrails { get; private set; }

    /// <summary>
    /// Gets the tools available to this agent.
    /// </summary>
    public IReadOnlyList<IBaseTool> Tools => _tools.AsReadOnly();

    /// <summary>
    /// Gets the knowledge (RAG) collections attached to this agent. Empty by default.
    /// Consumed at execution-context assembly time to retrieve and inject relevant
    /// context into the agent's prompts (RAG-03/C4 — wiring arrives in a later lot).
    /// </summary>
    public IReadOnlyList<KnowledgeAttachment> KnowledgeAttachments => _knowledgeAttachments.AsReadOnly();

    /// <summary>
    /// Gets the tasks assigned to this agent.
    /// </summary>
    public IReadOnlyList<TaskId> AssignedTasks => _assignedTasks.AsReadOnly();

    /// <summary>
    /// Gets the tasks the agent is running, in the order it started them: none when it is idle,
    /// several when the parallel mode runs a wave that gives it more than one task.
    /// </summary>
    public IReadOnlyList<TaskId> CurrentTasks => _currentTasks.AsReadOnly();

    /// <summary>
    /// Private constructor for the Agent.
    /// </summary>
    private Agent(AgentId id) : base(id)
    {
        _tools = [];
        _assignedTasks = [];
        _currentTasks = [];
        _knowledgeAttachments = [];
        _toolManager = new AgentToolManager(_tools, () => ToolAccessPolicy!);
        Role = AgentRole.From(AgentDefaults.UnassignedValue);
        Goal = AgentGoal.From(AgentDefaults.UnassignedValue);
        Status = AgentStatus.Idle;
        ToolAccessPolicy = ToolAccessPolicy.CreateUnrestricted();
    }

    /// <summary>
    /// Creates a new agent with the specified options.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An option is out of range, or the agent's own provider (<see cref="AgentCreateOptions.Llm"/>)
    /// comes with a host profile, or runs its own tools and comes with tools or delegation.
    /// </exception>
    public static Agent Create(AgentCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Role is null) throw new ArgumentNullException(nameof(options), "options.Role cannot be null.");
        if (options.Goal is null) throw new ArgumentNullException(nameof(options), "options.Goal cannot be null.");
        if (OwnProviderRefusal(options) is { } refusal)
            throw new ArgumentException(refusal, nameof(options));

        var agent = new Agent(AgentId.Create())
        {
            Role = options.Role,
            Goal = options.Goal,
            Backstory = options.Backstory,
            AllowDelegation = options.AllowDelegation,
            MaxIterations = options.MaxIterations > 0 ? options.MaxIterations : throw new ArgumentException("options.MaxIterations must be positive.", nameof(options)),
            MaxRpm = options.MaxRpm > 0 ? options.MaxRpm : throw new ArgumentException("options.MaxRpm must be positive.", nameof(options)),
            Verbose = options.Verbose,
            Status = AgentStatus.Idle,
            MaxExecutionTime = options.MaxExecutionTime,
            CacheEnabled = options.CacheEnabled,
            SystemTemplate = options.SystemTemplate,
            PromptTemplate = options.PromptTemplate,
            ResponseTemplate = options.ResponseTemplate,
            MaxRetryLimit = options.MaxRetryLimit > 0 ? options.MaxRetryLimit : throw new ArgumentException("options.MaxRetryLimit must be positive.", nameof(options)),
            Llm = options.Llm,
            ToolAccessPolicy = options.ToolAccessPolicy ?? ToolAccessPolicy.CreateUnrestricted(),
            Guardrails = options.Guardrails,
            LlmConfig = options.LlmConfig
        };

        if (options.Tools != null)
        {
            foreach (var tool in options.Tools)
            {
                agent._tools.Add(tool);
            }
        }

        if (options.KnowledgeAttachments != null)
        {
            foreach (var attachment in options.KnowledgeAttachments)
            {
                ArgumentNullException.ThrowIfNull(attachment);
                attachment.Validate();
                agent._knowledgeAttachments.Add(attachment);
            }
        }

        agent.RaiseDomainEvent(new AgentCreatedEvent
        {
            AgentId = agent.Id,
            Role = agent.Role,
            Goal = agent.Goal
        });

        return agent;
    }

    /// <summary>
    /// Why the agent's own provider cannot come with the rest of <paramref name="options"/>, or null
    /// (GAP-34): a host profile besides it — the agent runs on one or the other —, or, on a provider
    /// that runs its own tools, Orkeon tools or delegation it would never call.
    /// </summary>
    private static string? OwnProviderRefusal(AgentCreateOptions options)
    {
        if (options.Llm is not { } llm)
            return null;

        var who = $"Agent '{options.Role.Value}'";
        if (!AgentLlmRules.NamesNoProfile(options.LlmConfig?.Profile))
        {
            return $"{who} is given its own provider ({llm.Name}) and the host profile '{options.LlmConfig!.Profile!.Trim()}': " +
                "an agent runs on one or the other. Remove the profile from its LlmConfig to run on its own provider, " +
                "or remove the provider to run on the profile.";
        }

        if (!llm.Capabilities.RunsOwnTools)
            return null;

        var tools = (options.Tools ?? []).Select(tool => tool.Name).ToList();
        if (options.AllowDelegation)
            tools.AddRange(AgentLlmRules.DelegationTools);

        return tools.Count == 0 ? null : AgentLlmRules.OwnToolsRefusal(who, llm.Name, tools);
    }

    /// <summary>
    /// Creates a new agent with the specified parameters.
    /// Convenience overload that delegates to <see cref="Create(AgentCreateOptions)"/>.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use Create(AgentCreateOptions) instead
    public static Agent Create(
        AgentRole role,
        AgentGoal goal,
        AgentBackstory? backstory = null,
        bool allowDelegation = false,
        int maxIterations = AgentDefaults.MaxIterations,
        int maxRpm = AgentDefaults.MaxRequestsPerMinute,
        bool verbose = false,
        TimeSpan? maxExecutionTime = null,
        bool cacheEnabled = true,
        string? systemTemplate = null,
        string? promptTemplate = null,
        string? responseTemplate = null,
        int maxRetryLimit = AgentDefaults.MaxRetryLimit,
        ILlmProvider? llm = null,
        IEnumerable<IBaseTool>? tools = null,
        GuardrailsConfig? guardrails = null)
    {
        return Create(new AgentCreateOptions
        {
            Role = role,
            Goal = goal,
            Backstory = backstory,
            AllowDelegation = allowDelegation,
            MaxIterations = maxIterations,
            MaxRpm = maxRpm,
            Verbose = verbose,
            MaxExecutionTime = maxExecutionTime,
            CacheEnabled = cacheEnabled,
            SystemTemplate = systemTemplate,
            PromptTemplate = promptTemplate,
            ResponseTemplate = responseTemplate,
            MaxRetryLimit = maxRetryLimit,
            Llm = llm,
            Tools = tools,
            Guardrails = guardrails
        });
    }
#pragma warning restore S107

    /// <summary>
    /// Injects the agent selection strategy as a delegate.
    /// This allows the Application layer to provide selection logic without creating a dependency from Domain to Application.
    /// </summary>
    public void SetAgentSelectionStrategy(
        Func<ICrewTask, IEnumerable<AgentId>, CancellationToken, System.Threading.Tasks.Task<AgentId?>> selectionFunc)
    {
        ArgumentNullException.ThrowIfNull(selectionFunc);
        _agentSelectionFunc = selectionFunc;
    }

    /// <summary>
    /// Assigns a task to this agent. An agent busy with other tasks can still be given one: the
    /// parallel mode gives an agent every task of a wave declared for it.
    /// </summary>
    public void AssignTask(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (_assignedTasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is already assigned to this agent.");

        _assignedTasks.Add(taskId);

        RaiseDomainEvent(new AgentAssignedToTaskEvent
        {
            AgentId = Id,
            TaskId = taskId
        });
    }

    /// <summary>
    /// Starts running an assigned task. The agent is <see cref="AgentStatus.Busy"/> until it has
    /// completed or failed every task it started; it may run several at once.
    /// </summary>
    public void StartTask(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (!_assignedTasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is not assigned to this agent.");

        if (_currentTasks.Contains(taskId))
            throw new InvalidOperationException($"Agent is already executing task {taskId}.");

        if (Status != AgentStatus.Idle && Status != AgentStatus.Busy)
            throw new InvalidOperationException($"Agent must be idle or busy to start a task. Current status: {Status}.");

        _currentTasks.Add(taskId);
        Status = AgentStatus.Busy;

        RaiseDomainEvent(new AgentStartedTaskEvent
        {
            AgentId = Id,
            TaskId = taskId
        });
    }

    /// <summary>
    /// Completes a task the agent is running, with the output it produced. The agent is idle again
    /// once it runs no other task.
    /// </summary>
    public void CompleteTask(TaskId taskId, TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        EndTask(taskId);

        RaiseDomainEvent(new AgentCompletedTaskEvent
        {
            AgentId = Id,
            TaskId = taskId,
            Output = output
        });
    }

    /// <summary>
    /// Fails a task the agent is running, saying why. The agent is idle again once it runs no
    /// other task.
    /// </summary>
    public void FailTask(TaskId taskId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EndTask(taskId);

        RaiseDomainEvent(new AgentFailedTaskEvent
        {
            AgentId = Id,
            TaskId = taskId,
            Reason = reason
        });
    }

    private void EndTask(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (!_currentTasks.Remove(taskId))
            throw new InvalidOperationException($"Agent is not executing task {taskId}.");

        if (_currentTasks.Count == 0)
            Status = AgentStatus.Idle;
    }

    /// <summary>
    /// Adds a tool to the agent's capabilities.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The agent answers through its own provider, which runs its own tools (GAP-34): the tool would
    /// never be called.
    /// </exception>
    public void AddTool(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (Llm is { Capabilities.RunsOwnTools: true } llm)
            throw new InvalidOperationException(AgentLlmRules.OwnToolsRefusal($"Agent '{Role.Value}'", llm.Name, [tool.Name]));

        var toolName = _toolManager.AddTool(tool, Role.ToString());

        RaiseDomainEvent(new AgentCapabilitiesUpdatedEvent
        {
            AgentId = Id,
            AddedTools = [toolName],
            RemovedTools = []
        });
    }

    /// <summary>
    /// Removes a tool from the agent's capabilities.
    /// </summary>
    public void RemoveTool(string toolName)
    {
        _toolManager.RemoveTool(toolName, Role.ToString());

        RaiseDomainEvent(new AgentCapabilitiesUpdatedEvent
        {
            AgentId = Id,
            AddedTools = [],
            RemovedTools = [toolName]
        });
    }

    /// <summary>
    /// Checks if the agent has a specific tool.
    /// </summary>
    public bool HasTool(string toolName) =>
        _toolManager.HasTool(toolName);

    /// <summary>
    /// Updates the agent's goal.
    /// </summary>
    public void UpdateGoal(AgentGoal newGoal)
    {
        ArgumentNullException.ThrowIfNull(newGoal);

        if (Status != AgentStatus.Idle)
            throw new InvalidOperationException("Cannot update goal while agent is working.");

        Goal = newGoal;
    }

    /// <summary>
    /// Updates the agent's backstory.
    /// </summary>
    public void UpdateBackstory(AgentBackstory? backstory)
    {
        Backstory = backstory;
    }

    /// <summary>
    /// Updates the agent's configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The agent is working, or delegation is switched on for an agent whose own provider runs its own
    /// tools (GAP-34): the delegation tools would never be called.
    /// </exception>
    public void UpdateConfiguration(
        bool? allowDelegation = null,
        int? maxIterations = null,
        int? maxRpm = null,
        bool? verbose = null)
    {
        if (Status != AgentStatus.Idle)
            throw new InvalidOperationException("Cannot update configuration while agent is working.");

        if (allowDelegation == true && Llm is { Capabilities.RunsOwnTools: true } llm)
        {
            throw new InvalidOperationException(AgentLlmRules.OwnToolsRefusal(
                $"Agent '{Role.Value}'", llm.Name, AgentLlmRules.DelegationTools));
        }

        if (allowDelegation.HasValue)
            AllowDelegation = allowDelegation.Value;

        if (maxIterations.HasValue)
        {
            if (maxIterations.Value <= 0)
                throw new ArgumentException("Max iterations must be positive.", nameof(maxIterations));
            MaxIterations = maxIterations.Value;
        }

        if (maxRpm.HasValue)
        {
            if (maxRpm.Value <= 0)
                throw new ArgumentException("Max RPM must be positive.", nameof(maxRpm));
            MaxRpm = maxRpm.Value;
        }

        if (verbose.HasValue)
            Verbose = verbose.Value;
    }

    /// <summary>
    /// Updates the agent's tool access policy.
    /// This controls which tools the agent can access.
    /// </summary>
    public void UpdateToolAccessPolicy(ToolAccessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (Status != AgentStatus.Idle)
            throw new InvalidOperationException("Cannot update tool access policy while agent is working.");

        ToolAccessPolicy = policy;
    }

    /// <summary>
    /// Executes a task with the provided context and tools.
    /// </summary>
    /// <remarks>
    /// This method validates inputs but does not contain execution logic.
    /// Use <c>ExecutionOrchestrator.ExecuteWithChatClientAsync</c> for actual task execution.
    /// </remarks>
    public System.Threading.Tasks.Task<AgentStep> ExecuteTaskAsync(
        ICrewTask task,
        ILlmProvider llm,
        IToolRegistry toolRegistry,
        CancellationToken cancellationToken = default)
    {
        // Null guards
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(toolRegistry);

        // Validation
        ValidateCanExecute(task);

        throw new NotSupportedException(
            "Direct task execution on Agent is not supported. " +
            "Use ExecutionOrchestrator.ExecuteWithChatClientAsync instead.");
    }

    /// <summary>
    /// Determines if this agent should delegate a task to another agent.
    /// </summary>
    public async System.Threading.Tasks.Task<DelegationDecision> ShouldDelegateAsync(
        ICrewTask task,
        IEnumerable<AgentId> availableAgentIds,
        CancellationToken ct = default)
    {
        if (!AllowDelegation)
            return DelegationDecision.NoDelegation();

        // Delegation decision logic
        var bestAgentId = await SelectBestAgentForTaskAsync(task, availableAgentIds, ct).ConfigureAwait(false);
        if (bestAgentId != null && bestAgentId != this.Id)
        {
            return DelegationDecision.DelegateTo(bestAgentId);
        }

        return DelegationDecision.NoDelegation();
    }

    /// <summary>
    /// Registers a cancellation token source for lifecycle management.
    /// </summary>
    public void RegisterCancellation(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        _lifecycleCts = cts;
    }

    /// <summary>
    /// Stops the agent by cancelling its lifecycle token.
    /// </summary>
    public async System.Threading.Tasks.Task StopAsync(string reason = "Agent stopped")
    {
        if (_lifecycleCts is null)
            throw new InvalidOperationException("Agent has no registered cancellation token. Call RegisterCancellation first.");

        if (!_lifecycleCts.IsCancellationRequested)
        {
            await _lifecycleCts.CancelAsync().ConfigureAwait(false);
            Status = AgentStatus.Idle;
            _currentTasks.Clear();

            RaiseDomainEvent(new AgentKilledEvent
            {
                AgentId = Id,
                Reason = reason
            });
        }
    }

    private void ValidateCanExecute(ICrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (Status != AgentStatus.Idle)
            throw new InvalidOperationException($"Agent must be idle to execute task. Current status: {Status}");
    }

    private async System.Threading.Tasks.Task<AgentId?> SelectBestAgentForTaskAsync(
        ICrewTask task,
        IEnumerable<AgentId> availableAgentIds,
        CancellationToken ct = default)
    {
        if (_agentSelectionFunc is not null)
        {
            return await _agentSelectionFunc(task, availableAgentIds, ct).ConfigureAwait(false);
        }

        // Fallback: return the first agent ID that is not this agent.
        // More sophisticated matching requires the Application layer strategy.
        return availableAgentIds
            .FirstOrDefault(id => id != this.Id);
    }
}
