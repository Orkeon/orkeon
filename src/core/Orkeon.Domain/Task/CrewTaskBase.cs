using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.SharedKernel.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Tools;

namespace Orkeon.Domain.Task;

/// <summary>
/// Abstract base class for task aggregate roots with typed context.
/// Provides the full task lifecycle (assign, start, complete, fail, cancel, reopen),
/// dependency management, output validation, and typed context support. Every orchestration
/// mode drives it during a run (GAP-21): a task is assigned to the agent that runs it, started,
/// then completed, failed or — skipped behind a dependency that did not succeed, never reached, or
/// interrupted by a cancellation — cancelled; the next run of its crew reopens it.
/// </summary>
public abstract class CrewTaskBase<TContext> : AggregateRoot<TaskId>, ICrewTask
    where TContext : class, new()
{
    private readonly List<TaskId> _dependencies;
    private readonly List<IBaseTool> _tools;
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
    /// Gets when the task ended: completed, or failed. Null while it runs, and for a task cancelled
    /// or skipped before it ended.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Gets whether the author asked for asynchronous execution (YAML <c>asyncExecution:</c>,
    /// <c>.Async()</c>, <c>.asyncExecution()</c> in a script) — CrewAI's <c>async_execution</c>.
    /// <para>
    /// A sequential crew honours it (GAP-22): the task is launched and the next one starts at once;
    /// a task that depends on it waits for it, and only then reads its output; the crew waits for
    /// every task it launched before it reports, and its output stays the last declared task's.
    /// A parallel crew accepts it without an effect of its own — every task of a dependency wave
    /// already runs at once. The four other modes order their tasks themselves, so a crew asking
    /// it of them is refused when it is loaded or built (<c>ProcessType.AcceptsAsyncExecution</c>).
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
    /// Gets the tools this task adds to its agent's own for this task only (YAML <c>tools:</c> on
    /// a task). They never replace the agent's tools; a name the agent already holds is ignored
    /// when the belt is composed.
    /// </summary>
    public IReadOnlyList<IBaseTool> Tools => _tools.AsReadOnly();

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
        _tools = [];
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
    /// Updates the task context.
    /// </summary>
    public void UpdateContext(Action<TContext> updateAction)
    {
        _context.Update(updateAction);
        MarkAsUpdated();
    }

    /// <summary>
    /// Assigns the task to <paramref name="agentId"/>, before it starts or while it runs: a run
    /// hands a task over when its manager picks another agent than the one declared, when a failed
    /// task goes to a peer, or when a vote retains another agent's answer. Assigning the agent it
    /// already has changes nothing; a task that ended cannot be assigned.
    /// </summary>
    /// <param name="agentId">The agent identifier to assign to.</param>
    public void AssignTo(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (HasEnded)
            throw new InvalidOperationException($"Cannot assign task in {Status} status.");

        if (AssignedAgent == agentId)
            return;

        AssignedAgent = agentId;

        RaiseDomainEvent(new TaskAssignedEvent
        {
            TaskId = Id,
            AgentId = agentId
        });
    }

    /// <summary>
    /// Starts the task under the agent it is assigned to. When it may start is the run's decision:
    /// the run starts a task once the tasks it depends on succeeded (<see cref="CanExecute"/>), and
    /// skips it otherwise.
    /// </summary>
    /// <param name="agentId">The agent that runs the task.</param>
    public void Start(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (AssignedAgent == null || AssignedAgent != agentId)
            throw new InvalidOperationException($"Task must be assigned to agent {agentId} before starting.");

        StartCore(agentId);
    }

    /// <summary>
    /// Starts a task no single agent runs: a consensual task is answered by every agent of its
    /// crew, and completed under the author of the answer its vote retains
    /// (<see cref="AssignTo"/>, then <see cref="Complete"/>).
    /// </summary>
    public void Start() => StartCore(agentId: null);

    private void StartCore(AgentId? agentId)
    {
        if (Status != TaskStatus.Pending)
            throw new InvalidOperationException($"Cannot start task in {Status} status.");

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

        CompletedAt = DateTime.UtcNow;
        UpdateStatus(TaskStatus.Failed, errorMessage);

        RaiseDomainEvent(new TaskFailedEvent
        {
            TaskId = Id,
            AgentId = AssignedAgent,
            ErrorMessage = errorMessage,
            Exception = exception
        });
    }

    /// <summary>
    /// Cancels the task: a run cancels a task it skips behind a dependency that did not succeed, a
    /// task it never reached, and a task its cancellation interrupted. The reason says which. A
    /// cancelled task did not end: its <see cref="CompletedAt"/> stays empty.
    /// </summary>
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

    /// <summary>
    /// Puts a task an earlier run started or ended back to <see cref="TaskStatus.Pending"/>, for the
    /// next run of its crew — a crew kicked off again without being reloaded runs the same tasks.
    /// Its output and its dates are cleared; its assignment and its dependencies are kept. A pending
    /// task is left as it is.
    /// </summary>
    public void Reopen()
    {
        if (Status == TaskStatus.Pending)
            return;

        Output = null;
        StartedAt = null;
        CompletedAt = null;
        UpdateStatus(TaskStatus.Pending, "Reopened for a new run");
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

    /// <summary>
    /// Adds a tool the agent holds while it runs this task, on top of its own. A second tool of
    /// the same name (case-insensitive) is ignored.
    /// </summary>
    /// <param name="tool">The tool to add.</param>
    public void AddTool(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (_tools.Exists(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
            return;

        _tools.Add(tool);
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

    /// <summary>
    /// Gets how long the task ran, from its start to its end. Zero while it runs, and for a task
    /// that never started.
    /// </summary>
    public TimeSpan GetExecutionTime() =>
        StartedAt is { } started && CompletedAt is { } ended ? ended - started : TimeSpan.Zero;

    /// <summary>Whether the task ended — completed, failed or cancelled.</summary>
    private bool HasEnded =>
        Status == TaskStatus.Completed || Status == TaskStatus.Failed || Status == TaskStatus.Cancelled;

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
