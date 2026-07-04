using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Agent aggregate root representing an intelligent agent in the crew.
/// Delegates tool management to AgentToolManager and memory management to AgentMemoryManager.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724", Justification = "Agent is the core aggregate-root type; the Orkeon.Domain.Agent namespace deliberately shares the name. Renaming would break the entire public API.")]
public sealed class Agent : AggregateRoot<AgentId>
{
    private readonly List<ITool> _tools;
    private readonly List<TaskId> _assignedTasks;
    private readonly List<AgentMemory> _memories;
    private readonly AgentToolManager _toolManager;
    private readonly AgentMemoryManager _memoryManager;
    private TaskId? _currentTask;
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
    /// Gets the function calling LLM provider.
    /// </summary>
    public ILlmProvider? FunctionCallingLlm { get; private set; }

    /// <summary>
    /// Optional per-agent LLM configuration. Set when a YAML crew declares an agent-level
    /// <c>llm:</c> block; the executor merges these values onto the chat options used for
    /// every LLM call this agent makes (Experiment 07 friction #7).
    /// </summary>
    public SharedKernel.ValueObjects.LlmConfig? LlmConfig { get; private set; }

    /// <summary>
    /// Gets the step callback for execution progress.
    /// </summary>
    public IStepCallback? StepCallback { get; private set; }

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
    public IReadOnlyList<ITool> Tools => _tools.AsReadOnly();

    /// <summary>
    /// Gets the tasks assigned to this agent.
    /// </summary>
    public IReadOnlyList<TaskId> AssignedTasks => _assignedTasks.AsReadOnly();

    /// <summary>
    /// Gets the agent's memories.
    /// </summary>
    public IReadOnlyList<AgentMemory> Memories => _memories.AsReadOnly();

    /// <summary>
    /// Gets the currently executing task.
    /// </summary>
    public TaskId? CurrentTask => _currentTask;

    /// <summary>
    /// Private constructor for the Agent.
    /// </summary>
    private Agent(AgentId id) : base(id)
    {
        _tools = [];
        _assignedTasks = [];
        _memories = [];
        _toolManager = new AgentToolManager(_tools, () => ToolAccessPolicy!);
        _memoryManager = new AgentMemoryManager(_memories);
        Role = AgentRole.From(AgentDefaults.UnassignedValue);
        Goal = AgentGoal.From(AgentDefaults.UnassignedValue);
        Status = AgentStatus.Idle;
        ToolAccessPolicy = ToolAccessPolicy.CreateUnrestricted();
    }

    /// <summary>
    /// Creates a new agent with the specified options.
    /// </summary>
    public static Agent Create(AgentCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Role is null) throw new ArgumentNullException(nameof(options), "options.Role cannot be null.");
        if (options.Goal is null) throw new ArgumentNullException(nameof(options), "options.Goal cannot be null.");

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
            FunctionCallingLlm = options.FunctionCallingLlm,
            StepCallback = options.StepCallback,
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

        agent.RaiseDomainEvent(new AgentCreatedEvent
        {
            AgentId = agent.Id,
            Role = agent.Role,
            Goal = agent.Goal
        });

        return agent;
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
        ILlmProvider? functionCallingLlm = null,
        IEnumerable<ITool>? tools = null,
        IStepCallback? stepCallback = null,
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
            FunctionCallingLlm = functionCallingLlm,
            Tools = tools,
            StepCallback = stepCallback,
            Guardrails = guardrails
        });
    }
#pragma warning restore S107

    /// <summary>
    /// Rehydrates an <see cref="Agent"/> from persistence without raising domain events.
    /// Use this factory when loading an existing agent from a database or external store.
    /// </summary>
#pragma warning disable S107 // Restore factory requires all persisted state; by design
    internal static Agent Restore(
        AgentId id,
        AgentRole role,
        AgentGoal goal,
        AgentBackstory? backstory,
        bool allowDelegation,
        int maxIterations,
        int maxRpm,
        bool verbose,
        AgentStatus status,
        TimeSpan? maxExecutionTime,
        bool cacheEnabled,
        string? systemTemplate,
        string? promptTemplate,
        string? responseTemplate,
        int maxRetryLimit,
        ILlmProvider? functionCallingLlm,
        IStepCallback? stepCallback,
        ToolAccessPolicy? toolAccessPolicy = null,
        IEnumerable<ITool>? tools = null,
        IEnumerable<TaskId>? assignedTasks = null,
        IEnumerable<AgentMemory>? memories = null,
        TaskId? currentTask = null,
        GuardrailsConfig? guardrails = null)
        => Restore(new AgentSnapshot
        {
            Id = id,
            Role = role,
            Goal = goal,
            Backstory = backstory,
            AllowDelegation = allowDelegation,
            MaxIterations = maxIterations,
            MaxRpm = maxRpm,
            Verbose = verbose,
            Status = status,
            MaxExecutionTime = maxExecutionTime,
            CacheEnabled = cacheEnabled,
            SystemTemplate = systemTemplate,
            PromptTemplate = promptTemplate,
            ResponseTemplate = responseTemplate,
            MaxRetryLimit = maxRetryLimit,
            FunctionCallingLlm = functionCallingLlm,
            StepCallback = stepCallback,
            ToolAccessPolicy = toolAccessPolicy,
            Tools = tools,
            AssignedTasks = assignedTasks,
            Memories = memories,
            CurrentTask = currentTask,
            Guardrails = guardrails
        });
#pragma warning restore S107

    /// <summary>
    /// Rehydrates an <see cref="Agent"/> from an <see cref="AgentSnapshot"/> without
    /// raising domain events. This is the preferred reconstruction entry point: named
    /// snapshot members avoid the positional-argument fragility of the flat overload.
    /// </summary>
    public static Agent Restore(AgentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Id);
        ArgumentNullException.ThrowIfNull(snapshot.Role);
        ArgumentNullException.ThrowIfNull(snapshot.Goal);

        var agent = new Agent(snapshot.Id)
        {
            Role = snapshot.Role,
            Goal = snapshot.Goal,
            Backstory = snapshot.Backstory,
            AllowDelegation = snapshot.AllowDelegation,
            MaxIterations = snapshot.MaxIterations,
            MaxRpm = snapshot.MaxRpm,
            Verbose = snapshot.Verbose,
            Status = snapshot.Status,
            MaxExecutionTime = snapshot.MaxExecutionTime,
            CacheEnabled = snapshot.CacheEnabled,
            SystemTemplate = snapshot.SystemTemplate,
            PromptTemplate = snapshot.PromptTemplate,
            ResponseTemplate = snapshot.ResponseTemplate,
            MaxRetryLimit = snapshot.MaxRetryLimit,
            FunctionCallingLlm = snapshot.FunctionCallingLlm,
            StepCallback = snapshot.StepCallback,
            ToolAccessPolicy = snapshot.ToolAccessPolicy ?? ToolAccessPolicy.CreateUnrestricted(),
            Guardrails = snapshot.Guardrails
        };

        if (snapshot.Tools != null)
            agent._tools.AddRange(snapshot.Tools);

        if (snapshot.AssignedTasks != null)
            agent._assignedTasks.AddRange(snapshot.AssignedTasks);

        if (snapshot.Memories != null)
            agent._memories.AddRange(snapshot.Memories);

        agent._currentTask = snapshot.CurrentTask;

        return agent;
    }

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
    /// Assigns a task to this agent.
    /// </summary>
    public void AssignTask(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (_assignedTasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is already assigned to this agent.");

        if (_currentTask != null)
            throw new InvalidOperationException($"Agent is currently executing task {_currentTask}. Cannot assign new task.");

        _assignedTasks.Add(taskId);

        RaiseDomainEvent(new AgentAssignedToTaskEvent
        {
            AgentId = Id,
            TaskId = taskId
        });
    }

    /// <summary>
    /// Starts executing a task.
    /// </summary>
    public void StartTask(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (!_assignedTasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is not assigned to this agent.");

        if (_currentTask != null)
            throw new InvalidOperationException($"Agent is already executing task {_currentTask}.");

        if (Status != AgentStatus.Idle)
            throw new InvalidOperationException($"Agent must be idle to start a task. Current status: {Status}.");

        _currentTask = taskId;
        Status = AgentStatus.Busy;

        RaiseDomainEvent(new AgentStartedTaskEvent
        {
            AgentId = Id,
            TaskId = taskId
        });
    }

    /// <summary>
    /// Completes the current task with the given output.
    /// </summary>
    public void CompleteTask(TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (_currentTask == null)
            throw new InvalidOperationException("No task is currently being executed.");

        var completedTaskId = _currentTask!;
        _currentTask = null;
        Status = AgentStatus.Idle;

        RaiseDomainEvent(new AgentCompletedTaskEvent
        {
            AgentId = Id,
            TaskId = completedTaskId,
            Output = output
        });
    }

    /// <summary>
    /// Fails the current task with the given reason.
    /// </summary>
    public void FailTask(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_currentTask == null)
            throw new InvalidOperationException("No task is currently being executed.");

        var failedTaskId = _currentTask!;
        _currentTask = null;
        Status = AgentStatus.Idle;

        RaiseDomainEvent(new AgentFailedTaskEvent
        {
            AgentId = Id,
            TaskId = failedTaskId,
            Reason = reason
        });
    }

    /// <summary>
    /// Adds a tool to the agent's capabilities.
    /// </summary>
    public void AddTool(ITool tool)
    {
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
    /// Updates the agent's memory.
    /// </summary>
    public void UpdateMemory(AgentMemory memory)
    {
        var addedMemory = _memoryManager.AddMemory(memory);

        RaiseDomainEvent(new AgentMemoryUpdatedEvent
        {
            AgentId = Id,
            MemoryId = addedMemory.Id,
            MemoryType = addedMemory.Type.ToString()
        });
    }

    /// <summary>
    /// Initiates collaboration with another agent.
    /// </summary>
    public CollaborationId CollaborateWith(AgentId collaboratorId, TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(collaboratorId);
        ArgumentNullException.ThrowIfNull(taskId);

        if (!_assignedTasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is not assigned to this agent.");

        if (!AllowDelegation)
            throw new InvalidOperationException("This agent does not allow delegation.");

        var collaborationId = CollaborationId.Create();

        RaiseDomainEvent(new AgentCollaborationStartedEvent
        {
            InitiatorId = Id,
            CollaboratorId = collaboratorId,
            TaskId = taskId,
            CollaborationId = collaborationId
        });

        return collaborationId;
    }

    /// <summary>
    /// Validates whether this agent is capable of executing a task.
    /// Checks tool availability.
    /// </summary>
    public ExecutionValidationResult ValidateForExecution()
    {
        var issues = new List<string>();

        if (_tools.Count == 0)
        {
            issues.Add("Agent has no tools assigned");
        }

        return issues.Count == 0
            ? ExecutionValidationResult.Success()
            : ExecutionValidationResult.Failure(issues);
    }

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
    public void UpdateConfiguration(
        bool? allowDelegation = null,
        int? maxIterations = null,
        int? maxRpm = null,
        bool? verbose = null)
    {
        if (Status != AgentStatus.Idle)
            throw new InvalidOperationException("Cannot update configuration while agent is working.");

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
            _currentTask = null;

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
