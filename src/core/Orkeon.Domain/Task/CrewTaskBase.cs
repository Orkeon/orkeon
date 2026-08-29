using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.SharedKernel.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

using Orkeon.Domain.SharedKernel;

namespace Orkeon.Domain.Task;

/// <summary>
/// Abstract base class for task aggregate roots with typed context.
/// Provides the full task lifecycle (assign, start, complete, fail, cancel),
/// dependency management, output validation, and typed context support.
/// </summary>
public abstract class CrewTaskBase<TContext> : AggregateRoot<TaskId>, ICrewTask
    where TContext : class, new()
{
    private readonly List<TaskId> _dependencies;
    private readonly List<ToolId> _requiredTools;
    private readonly TypedTaskContext<TContext> _context;
    private readonly TaskDependencyManager _dependencyManager;

    /// <summary>Gets the task identifier (explicit interface implementation).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1033", Justification = "ICrewTask.TaskId maps to the publicly inherited Id property, which derived classes can already access.")]
    TaskId ICrewTask.TaskId => Id;

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public TaskDescription Description { get; private set; }

    /// <summary>
    /// Gets the expected output description.
    /// </summary>
    public ExpectedOutput ExpectedOutput { get; private set; }

    /// <summary>
    /// Gets the agent assigned to this task.
    /// </summary>
    public AgentId? AssignedAgent { get; private set; }

    /// <summary>
    /// Gets the task status.
    /// </summary>
    public TaskStatus Status { get; private set; }

    /// <summary>
    /// Gets the task output if completed.
    /// </summary>
    public TaskOutput? Output { get; private set; }

    /// <summary>
    /// Gets the task priority.
    /// </summary>
    public TaskPriority Priority { get; private set; }

    /// <summary>
    /// Gets when the task was created.
    /// </summary>
    public new DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Gets when the task was started.
    /// </summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>
    /// Gets when the task was completed.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Gets whether the author asked for asynchronous execution.
    /// <para>
    /// <b>Recorded, not yet honoured.</b> The YAML <c>asyncExecution:</c> field is parsed,
    /// mapped and stored here, and no orchestration strategy reads it: concurrency comes from
    /// <c>ProcessType.Parallel</c>, which now runs dependency waves. Kept because the field is
    /// already in shipped crew files and dropping it would fail them at load; stated here
    /// because "asynchronous execution" reads as a promise the engine does not keep.
    /// </para>
    /// </summary>
    public bool AsyncExecution { get; private set; }

    /// <summary>
    /// Gets the JSON schema for output validation.
    /// </summary>
    public JsonSchema? OutputJson { get; private set; }

    /// <summary>
    /// Gets the output type for structured data.
    /// </summary>
    public Type? OutputPydantic { get; private set; }

    /// <summary>
    /// Gets the output file path.
    /// </summary>
    public string? OutputFile { get; private set; }

    /// <summary>
    /// Gets the framework-managed deliverable contract for this task, or <c>null</c>
    /// when the task falls back to legacy <see cref="DeliverableSource.ToolCall"/> semantics.
    /// </summary>
    public TaskDeliverable? Deliverable { get; private set; }

    /// <summary>
    /// Gets the task callback.
    /// </summary>
    public ITaskCallback? Callback { get; private set; }

    /// <summary>
    /// Optional per-task LLM override. Overrides the agent's LlmConfig on the scope of this
    /// task alone (e.g. <c>response_format: json_object</c>, or <c>temperature: 0.0</c> for
    /// a strict extraction). Applied by <c>LlmConfigResolver</c> before every provider
    /// call.
    /// </summary>
    public LlmConfigOverride? LlmOverride { get; private set; }

    /// <summary>
    /// Optional per-task guardrails. Injected into this task's prompt in addition to the assigned
    /// agent's guardrails (agent rules first, then task rules). <c>null</c> means no task guardrails.
    /// </summary>
    public Orkeon.Domain.Agent.GuardrailsConfig? Guardrails { get; private set; }

    /// <summary>
    /// Gets whether human input is required.
    /// </summary>
    public bool HumanInput { get; private set; }

    /// <summary>
    /// Gets the task dependencies.
    /// </summary>
    public IReadOnlyList<TaskId> Dependencies => _dependencies.AsReadOnly();

    /// <summary>
    /// Gets the required tools for this task.
    /// </summary>
    public IReadOnlyList<ToolId> RequiredTools => _requiredTools.AsReadOnly();

    /// <summary>
    /// Gets the typed task context.
    /// </summary>
    public ITaskContext<TContext> Context => _context;

    /// <summary>
    /// Gets the typed context data directly.
    /// </summary>
    public TContext TypedContext => _context.Data;

    /// <summary>
    /// Protected constructor for the Task.
    /// </summary>
    protected CrewTaskBase(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        TaskPriority? priority = null,
        TaskOutputOptions? outputOptions = null,
        TContext? initialContext = null) : base(id)
    {
        ArgumentNullException.ThrowIfNull(expectedOutput);

        _dependencies = [];
        _requiredTools = [];
        _dependencyManager = new TaskDependencyManager(_dependencies);

        var opts = outputOptions ?? TaskOutputOptions.Default;
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
        ExpectedOutput = expectedOutput;
        Priority = priority ?? TaskPriority.Normal;
        Status = TaskStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        AsyncExecution = opts.AsyncExecution;
        OutputJson = opts.OutputJson;
        OutputPydantic = opts.OutputPydantic;
        OutputFile = opts.OutputFile;
        Deliverable = opts.Deliverable;
        Callback = opts.Callback;
        HumanInput = opts.HumanInput;

        var metadata = new TaskContextMetadata(Id, AgentId.Create(), typeof(TContext).Name);
        _context = new TypedTaskContext<TContext>(initialContext ?? new TContext(), metadata);

        RaiseDomainEvent(new TaskCreatedEvent
        {
            TaskId = Id,
            Description = Description
        });
    }

    /// <summary>
    /// Restore constructor: rehydrates a task from persistence without raising domain events.
    /// </summary>
#pragma warning disable S107 // Restore constructor requires all persisted state; by design
    protected CrewTaskBase(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        TaskStatus status,
        AgentId? assignedAgent,
        TaskOutput? output,
        TaskPriority priority,
        DateTime createdAt,
        DateTime? startedAt,
        DateTime? completedAt,
        bool asyncExecution,
        JsonSchema? outputJson,
        Type? outputPydantic,
        string? outputFile,
        ITaskCallback? callback,
        bool humanInput,
        IEnumerable<TaskId>? dependencies,
        IEnumerable<ToolId>? requiredTools,
        TContext? contextData,
        TaskDeliverable? deliverable = null) : base(id)
    {
        _dependencies = [];
        _requiredTools = [];
        _dependencyManager = new TaskDependencyManager(_dependencies);

        ArgumentNullException.ThrowIfNull(description);

        Description = description;
        ArgumentNullException.ThrowIfNull(expectedOutput);
        ExpectedOutput = expectedOutput;
        Status = status;
        AssignedAgent = assignedAgent;
        Output = output;
        Priority = priority ?? TaskPriority.Normal;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        AsyncExecution = asyncExecution;
        OutputJson = outputJson;
        OutputPydantic = outputPydantic;
        OutputFile = outputFile;
        Deliverable = deliverable;
        Callback = callback;
        HumanInput = humanInput;

        var metadata = new TaskContextMetadata(Id, AgentId.Create(), typeof(TContext).Name);
        _context = new TypedTaskContext<TContext>(contextData ?? new TContext(), metadata);

        if (dependencies != null)
            _dependencies.AddRange(dependencies);

        if (requiredTools != null)
            _requiredTools.AddRange(requiredTools);

        // No domain events raised — this is a restore from persistence
    }
#pragma warning restore S107

    /// <summary>
    /// Updates the task context.
    /// </summary>
    public void UpdateContext(Action<TContext> updateAction)
    {
        _context.Update(updateAction);
        MarkAsUpdated();
    }

    /// <inheritdoc />
    public void AssignTo(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException($"Cannot assign task in {Status} status.");

        if (AssignedAgent != null)
            throw new InvalidOperationException($"Task is already assigned to agent {AssignedAgent}.");

        AssignedAgent = agentId;

        RaiseDomainEvent(new TaskAssignedEvent
        {
            TaskId = Id,
            AgentId = agentId
        });
    }

    /// <inheritdoc />
    public void Start(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (AssignedAgent == null || AssignedAgent != agentId)
            throw new InvalidOperationException($"Task must be assigned to agent {agentId} before starting.");

        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException($"Cannot start task in {Status} status.");

        if (_dependencyManager.HasUncompletedDependencies())
        {
            UpdateStatus(TaskStatus.Blocked, "Waiting for dependencies to complete");
            return;
        }

        StartedAt = DateTime.UtcNow;
        UpdateStatus(TaskStatus.InProgress);

        RaiseDomainEvent(new TaskStartedEvent
        {
            TaskId = Id,
            AgentId = agentId
        });
    }

    /// <inheritdoc />
    public void Complete(AgentId agentId, TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(output);

        if (AssignedAgent != agentId)
            throw new InvalidOperationException($"Only the assigned agent {AssignedAgent} can complete this task.");

        if (Status != TaskStatus.InProgress)
            throw new InvalidOperationException($"Cannot complete task in {Status} status.");

        Output = output;
        CompletedAt = DateTime.UtcNow;
        UpdateStatus(TaskStatus.Completed);

        var duration = CompletedAt.Value - (StartedAt ?? CreatedAt);

        RaiseDomainEvent(new TaskCompletedEvent
        {
            TaskId = Id,
            AgentId = agentId,
            Output = output,
            Duration = duration
        });
    }

    /// <inheritdoc />
    public void Fail(string errorMessage, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        if (Status == TaskStatus.Completed || Status == TaskStatus.Failed)
            throw new InvalidOperationException($"Cannot fail task in {Status} status.");

        UpdateStatus(TaskStatus.Failed, errorMessage);

        RaiseDomainEvent(new TaskFailedEvent
        {
            TaskId = Id,
            AgentId = AssignedAgent,
            ErrorMessage = errorMessage,
            Exception = exception
        });
    }

    /// <summary>Cancels the task.</summary>
    /// <param name="reason">The cancellation reason.</param>
    public void Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == TaskStatus.Completed || Status == TaskStatus.Failed)
            throw new InvalidOperationException($"Cannot cancel task in {Status} status.");

        UpdateStatus(TaskStatus.Cancelled, reason);

        RaiseDomainEvent(new TaskCancelledEvent
        {
            TaskId = Id,
            Reason = reason
        });
    }

    /// <summary>Adds a dependency on another task.</summary>
    /// <param name="dependencyId">The dependency task identifier.</param>
    public void AddDependency(TaskId dependencyId)
    {
        if (!_dependencyManager.AddDependency(dependencyId, Id, Status))
            return;

        RaiseDomainEvent(new TaskDependenciesUpdatedEvent
        {
            TaskId = Id,
            AddedDependencies = [dependencyId],
            RemovedDependencies = []
        });
    }

    /// <summary>Removes a dependency from this task.</summary>
    /// <param name="dependencyId">The dependency task identifier to remove.</param>
    public void RemoveDependency(TaskId dependencyId)
    {
        if (!_dependencyManager.RemoveDependency(dependencyId, Status))
            return;

        RaiseDomainEvent(new TaskDependenciesUpdatedEvent
        {
            TaskId = Id,
            AddedDependencies = [],
            RemovedDependencies = [dependencyId]
        });
    }

    /// <summary>Adds a required tool to this task.</summary>
    /// <param name="toolId">The tool identifier to add.</param>
    public void AddRequiredTool(ToolId toolId)
    {
        ArgumentNullException.ThrowIfNull(toolId);

        if (_requiredTools.Contains(toolId))
            return;

        _requiredTools.Add(toolId);
    }

    /// <inheritdoc />
    public bool CanExecute(Func<TaskId, bool> isTaskCompleted)
    {
        ArgumentNullException.ThrowIfNull(isTaskCompleted);

        return !_dependencyManager.HasUncompletedDependencies(isTaskCompleted);
    }

    /// <inheritdoc />
    public ValidationResult ValidateOutput(TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return TaskOutputValidator.ValidateOutput(output, OutputJson, OutputPydantic);
    }

    /// <summary>
    /// Updates task configuration.
    /// </summary>
    public void UpdateConfiguration(
        bool? asyncExecution = null,
        JsonSchema? outputJson = null,
        Type? outputPydantic = null,
        string? outputFile = null,
        bool? humanInput = null)
    {
        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException("Cannot update configuration after task has started.");

        if (asyncExecution.HasValue)
            AsyncExecution = asyncExecution.Value;

        if (outputJson != null)
            OutputJson = outputJson;

        if (outputPydantic != null)
            OutputPydantic = outputPydantic;

        if (outputFile != null)
            OutputFile = outputFile;

        if (humanInput.HasValue)
            HumanInput = humanInput.Value;
    }

    /// <summary>
    /// Assigns or replaces the <see cref="TaskDeliverable"/> contract. Callable before the task starts.
    /// </summary>
    public void SetDeliverable(TaskDeliverable? deliverable)
    {
        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException("Cannot update Deliverable after task has started.");

        deliverable?.Validate();
        Deliverable = deliverable;
    }

    /// <summary>
    /// Assigns or replaces the per-task <see cref="LlmConfigOverride"/>. Callable before the task starts.
    /// </summary>
    public void SetLlmOverride(LlmConfigOverride? llmOverride)
    {
        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException("Cannot update LlmOverride after task has started.");

        LlmOverride = llmOverride;
    }

    /// <summary>
    /// Assigns or replaces the per-task <see cref="Orkeon.Domain.Agent.GuardrailsConfig"/>. Callable before the task starts.
    /// </summary>
    public void SetGuardrails(Orkeon.Domain.Agent.GuardrailsConfig? guardrails)
    {
        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException("Cannot update Guardrails after task has started.");

        Guardrails = guardrails;
    }

    /// <inheritdoc />
    public virtual string GetContextSummary()
    {
        return _context.Data.ToString() ?? "Context: " + typeof(TContext).Name;
    }

    /// <inheritdoc />
    public TimeSpan GetExecutionTime()
    {
        if (!CompletedAt.HasValue)
            return TimeSpan.Zero;

        var startTime = StartedAt ?? CreatedAt;
        return CompletedAt.Value - startTime;
    }

    private void UpdateStatus(TaskStatus newStatus, string? reason = null)
    {
        var oldStatus = Status;
        Status = newStatus;

        var statusChangedEvent = new TaskStatusChangedEvent
        {
            TaskId = Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason
        };
        RaiseDomainEvent(statusChangedEvent);
    }
}
