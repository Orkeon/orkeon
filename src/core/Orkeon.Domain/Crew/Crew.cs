using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Crew aggregate root representing a team of agents working together.
/// Delegates member management to CrewMemberManager and task management to CrewTaskManager.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724", Justification = "Crew is the core aggregate-root type; the conflict is with the unrelated Orkeon.Domain.Constants.Crew constants namespace. Renaming would break the entire public API.")]
public sealed class Crew : AggregateRoot<CrewId>
{
    private readonly List<AgentId> _agents;
    private readonly List<TaskId> _tasks;
    private readonly List<CrewExecution> _executions;
    private readonly CrewMemberManager _memberManager;
    private readonly CrewTaskManager _taskManager;
    private ProcessId? _currentProcessId;
    private Func<IEnumerable<AgentId>, string, System.Threading.Tasks.Task>? _killAllFunc;

    /// <summary>
    /// Gets the crew's goal.
    /// </summary>
    public CrewGoal Goal { get; private set; }

    /// <summary>
    /// Gets the process type used by this crew.
    /// </summary>
    public ProcessType ProcessType { get; private set; }

    /// <summary>
    /// Gets whether verbose logging is enabled.
    /// </summary>
    public bool Verbose { get; private set; }

    /// <summary>
    /// Gets whether planning is enabled.
    /// </summary>
    public bool Planning { get; private set; }

    /// <summary>
    /// Gets the manager agent ID for hierarchical process.
    /// </summary>
    public AgentId? ManagerAgentId { get; private set; }

    /// <summary>
    /// Gets the crew status.
    /// </summary>
    public CrewStatus Status { get; private set; }

    /// <summary>
    /// Gets the maximum requests per minute.
    /// </summary>
    public int MaxRpm { get; private set; } = CrewDefaults.DefaultMaxRpm;

    /// <summary>
    /// Gets whether to share crew information.
    /// </summary>
    public bool ShareCrew { get; private set; } = true;

    /// <summary>
    /// Gets the output log file path.
    /// </summary>
    public string? OutputLogFile { get; private set; }

    /// <summary>
    /// Gets the manager LLM for hierarchical process.
    /// </summary>
    public ILlmProvider? ManagerLlm { get; private set; }

    /// <summary>
    /// Gets the language for crew operations.
    /// </summary>
    public LanguageCode Language { get; private set; } = LanguageCode.Default;

    /// <summary>
    /// Gets whether to output full details.
    /// </summary>
    public bool FullOutput { get; private set; }

    /// <summary>
    /// Gets the step callback for execution progress.
    /// </summary>
    public IStepCallback? StepCallback { get; private set; }

    /// <summary>
    /// Gets the task callback for task completion.
    /// </summary>
    public ITaskCallback? TaskCallback { get; private set; }

    /// <summary>
    /// Gets the planning LLM provider.
    /// </summary>
    public ILlmProvider? PlanningLlm { get; private set; }

    /// <summary>
    /// Gets whether memory is enabled.
    /// </summary>
    public bool MemoryEnabled { get; private set; }

    /// <summary>
    /// Gets whether dynamic agent creation is allowed during execution.
    /// </summary>
    public bool AllowDynamicAgents { get; private set; }

    /// <summary>
    /// Gets the maximum number of concurrent dynamic agents (null for unlimited).
    /// </summary>
    public int? MaxConcurrentDynamicAgents { get; private set; }

    /// <summary>
    /// Gets the optional tool access control policy override for this crew.
    /// When set, this policy overrides individual agent policies.
    /// </summary>
    public ToolAccessPolicy? ToolAccessPolicy { get; private set; }

    /// <summary>
    /// Gets the agents in this crew.
    /// </summary>
    public IReadOnlyList<AgentId> Agents => _agents.AsReadOnly();

    /// <summary>
    /// Gets the tasks assigned to this crew.
    /// </summary>
    public IReadOnlyList<TaskId> Tasks => _tasks.AsReadOnly();

    /// <summary>
    /// Gets the execution history.
    /// </summary>
    public IReadOnlyList<CrewExecution> Executions => _executions.AsReadOnly();

    /// <summary>
    /// Gets the current process ID if executing.
    /// </summary>
    public ProcessId? CurrentProcessId => _currentProcessId;

    /// <summary>
    /// Private constructor for the Crew.
    /// </summary>
    private Crew(CrewId id) : base(id)
    {
        _agents = [];
        _tasks = [];
        _executions = [];
        _memberManager = new CrewMemberManager(_agents);
        _taskManager = new CrewTaskManager(_tasks);
        Goal = CrewGoal.From("Unassigned");
        Status = CrewStatus.Idle;
        ProcessType = ProcessType.Sequential;
    }

    /// <summary>
    /// Creates a new crew with the specified options.
    /// </summary>
    public static Crew Create(CrewCreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Goal))
            throw new ArgumentException("options.Goal cannot be empty.", nameof(options));

        var crew = new Crew(CrewId.Create())
        {
            Goal = CrewGoal.From(options.Goal),
            ProcessType = options.ProcessType,
            Verbose = options.Verbose,
            Planning = options.Planning,
            Status = CrewStatus.Idle,
            MaxRpm = options.MaxRpm > 0 ? options.MaxRpm : throw new ArgumentException("options.MaxRpm must be positive.", nameof(options)),
            ShareCrew = options.ShareCrew,
            OutputLogFile = options.OutputLogFile,
            ManagerLlm = options.ManagerLlm,
            ManagerAgentId = options.ManagerAgentId,
            Language = !string.IsNullOrWhiteSpace(options.Language) ? LanguageCode.From(options.Language) : LanguageCode.Default,
            FullOutput = options.FullOutput,
            StepCallback = options.StepCallback,
            TaskCallback = options.TaskCallback,
            PlanningLlm = options.PlanningLlm,
            MemoryEnabled = options.MemoryEnabled,
            AllowDynamicAgents = options.AllowDynamicAgents,
            MaxConcurrentDynamicAgents = options.MaxConcurrentDynamicAgents,
            ToolAccessPolicy = options.ToolAccessPolicy
        };

        if (options.ProcessType == ProcessType.Hierarchical && options.ManagerAgentId == null && options.ManagerLlm == null)
        {
            throw new ArgumentException("Hierarchical process requires either a manager agent or manager LLM.", nameof(options));
        }

        crew.RaiseDomainEvent(new CrewCreatedEvent
        {
            CrewId = crew.Id,
            Goal = crew.Goal.Value,
            ProcessType = crew.ProcessType
        });

        return crew;
    }

    /// <summary>
    /// Creates a new crew with the specified parameters.
    /// Convenience overload that delegates to <see cref="Create(CrewCreateOptions)"/>.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use Create(CrewCreateOptions) instead
    public static Crew Create(
        string goal,
        ProcessType? processType = null,
        bool verbose = false,
        bool planning = false,
        int maxRpm = 100,
        bool shareCrew = true,
        string? outputLogFile = null,
        ILlmProvider? managerLlm = null,
        AgentId? managerAgentId = null,
        string language = "en",
        bool fullOutput = false,
        IStepCallback? stepCallback = null,
        ITaskCallback? taskCallback = null,
        ILlmProvider? planningLlm = null,
        bool memoryEnabled = false)
    {
        return Create(new CrewCreateOptions
        {
            Goal = goal,
            ProcessType = processType ?? ProcessType.Sequential,
            Verbose = verbose,
            Planning = planning,
            MaxRpm = maxRpm,
            ShareCrew = shareCrew,
            OutputLogFile = outputLogFile,
            ManagerLlm = managerLlm,
            ManagerAgentId = managerAgentId,
            Language = language,
            FullOutput = fullOutput,
            StepCallback = stepCallback,
            TaskCallback = taskCallback,
            PlanningLlm = planningLlm,
            MemoryEnabled = memoryEnabled
        });
    }
#pragma warning restore S107

    /// <summary>
    /// Adds an agent to the crew.
    /// </summary>
    public void AddAgent(AgentId agentId)
    {
        _memberManager.AddAgent(agentId, Status);

        RaiseDomainEvent(new AgentJoinedCrewEvent
        {
            CrewId = Id,
            AgentId = agentId
        });

        // If hierarchical and no manager set, first agent becomes manager
        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null)
        {
            ManagerAgentId = agentId;
        }
    }

    /// <summary>
    /// Removes an agent from the crew.
    /// </summary>
    public void RemoveAgent(AgentId agentId, string reason)
    {
        _memberManager.RemoveAgent(agentId, reason, Status);

        RaiseDomainEvent(new AgentLeftCrewEvent
        {
            CrewId = Id,
            AgentId = agentId,
            Reason = reason
        });

        // If removed agent was manager, assign new manager
        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == agentId)
        {
            ManagerAgentId = _memberManager.FirstOrDefault();
        }
    }

    /// <summary>
    /// Adds a task to the crew.
    /// </summary>
    public void AddTask(TaskId taskId)
    {
        _taskManager.AddTask(taskId, Status);

        RaiseDomainEvent(new TaskAddedToCrewEvent
        {
            CrewId = Id,
            TaskId = taskId
        });
    }

    /// <summary>
    /// Removes a task from the crew.
    /// </summary>
    public void RemoveTask(TaskId taskId, string reason)
    {
        _taskManager.RemoveTask(taskId, reason, Status);

        RaiseDomainEvent(new TaskRemovedFromCrewEvent
        {
            CrewId = Id,
            TaskId = taskId,
            Reason = reason
        });
    }

    /// <summary>
    /// Starts crew execution.
    /// </summary>
    public ProcessId StartExecution()
    {
        if (Status == CrewStatus.Executing)
            throw new InvalidOperationException("Crew is already executing.");

        if (!_memberManager.Any())
            throw new InvalidOperationException("Cannot execute crew without agents.");

        if (!_taskManager.Any())
            throw new InvalidOperationException("Cannot execute crew without tasks.");

        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null)
            throw new InvalidOperationException("Hierarchical process requires a manager agent.");

        _currentProcessId = ProcessId.Create();
        Status = CrewStatus.Executing;

        var execution = new CrewExecution(_currentProcessId, DateTime.UtcNow);
        _executions.Add(execution);

        RaiseDomainEvent(new CrewExecutionStartedEvent
        {
            CrewId = Id,
            ProcessId = _currentProcessId
        });

        return _currentProcessId;
    }

    /// <summary>
    /// Completes the current execution successfully.
    /// </summary>
    public void CompleteExecution(int completedTasks, int failedTasks)
    {
        if (Status != CrewStatus.Executing || _currentProcessId == null)
            throw new InvalidOperationException("No execution is currently in progress.");

        var execution = _executions.FirstOrDefault(e => e.ProcessId == _currentProcessId);
        if (execution == null)
            throw new InvalidOperationException("Current execution not found.");

        execution.Complete(completedTasks, failedTasks);

        Status = CrewStatus.Idle;
        var processId = _currentProcessId;
        _currentProcessId = null;

        RaiseDomainEvent(new CrewExecutionCompletedEvent
        {
            CrewId = Id,
            ProcessId = processId,
            Duration = execution.Duration!.Value,
            CompletedTasks = completedTasks,
            FailedTasks = failedTasks
        });
    }

    /// <summary>
    /// Fails the current execution.
    /// </summary>
    public void FailExecution(string reason, Exception? exception = null)
    {
        if (Status != CrewStatus.Executing || _currentProcessId == null)
            throw new InvalidOperationException("No execution is currently in progress.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var execution = _executions.FirstOrDefault(e => e.ProcessId == _currentProcessId);
        if (execution == null)
            throw new InvalidOperationException("Current execution not found.");

        execution.Fail(reason);

        Status = CrewStatus.Failed;
        var processId = _currentProcessId;
        _currentProcessId = null;

        RaiseDomainEvent(new CrewExecutionFailedEvent
        {
            CrewId = Id,
            ProcessId = processId,
            Reason = reason,
            Exception = exception
        });
    }

    /// <summary>
    /// Updates the crew's goal.
    /// </summary>
    public void UpdateGoal(string newGoal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newGoal);

        if (Status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot update goal while crew is executing.");

        var oldGoal = Goal;
        Goal = CrewGoal.From(newGoal);

        RaiseDomainEvent(new CrewGoalUpdatedEvent
        {
            CrewId = Id,
            OldGoal = oldGoal.Value,
            NewGoal = Goal.Value
        });
    }

    /// <summary>
    /// Changes the process type.
    /// </summary>
    public void ChangeProcessType(ProcessType newProcessType, AgentId? managerAgentId = null)
    {
        if (Status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot change process type while crew is executing.");

        if (newProcessType == ProcessType.Hierarchical)
        {
            if (managerAgentId == null || !_memberManager.Any())
                throw new InvalidOperationException("Hierarchical process requires a manager agent.");

            ManagerAgentId = managerAgentId;
        }

        var oldProcessType = ProcessType;
        ProcessType = newProcessType;

        RaiseDomainEvent(new CrewProcessTypeChangedEvent
        {
            CrewId = Id,
            OldProcessType = oldProcessType,
            NewProcessType = newProcessType
        });
    }

    /// <summary>
    /// Updates crew configuration using an options object.
    /// </summary>
    public void UpdateConfiguration(CrewConfigurationUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update.Verbose.HasValue)
            Verbose = update.Verbose.Value;

        if (update.Planning.HasValue)
            Planning = update.Planning.Value;

        if (update.MaxRpm.HasValue)
        {
            if (update.MaxRpm.Value <= 0)
                throw new ArgumentException("Max RPM must be positive.", nameof(update));
            MaxRpm = update.MaxRpm.Value;
        }

        if (update.ShareCrew.HasValue)
            ShareCrew = update.ShareCrew.Value;

        if (update.OutputLogFile != null)
            OutputLogFile = update.OutputLogFile;

        if (!string.IsNullOrWhiteSpace(update.Language))
            Language = LanguageCode.From(update.Language);

        if (update.FullOutput.HasValue)
            FullOutput = update.FullOutput.Value;

        if (update.MemoryEnabled.HasValue)
            MemoryEnabled = update.MemoryEnabled.Value;
    }

    /// <summary>
    /// Updates crew configuration.
    /// Convenience overload that delegates to <see cref="UpdateConfiguration(CrewConfigurationUpdate)"/>.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use UpdateConfiguration(CrewConfigurationUpdate) instead
    public void UpdateConfiguration(
        bool? verbose = null,
        bool? planning = null,
        int? maxRpm = null,
        bool? shareCrew = null,
        string? outputLogFile = null,
        string? language = null,
        bool? fullOutput = null,
        bool? memoryEnabled = null)
    {
        UpdateConfiguration(new CrewConfigurationUpdate
        {
            Verbose = verbose,
            Planning = planning,
            MaxRpm = maxRpm,
            ShareCrew = shareCrew,
            OutputLogFile = outputLogFile,
            Language = language,
            FullOutput = fullOutput,
            MemoryEnabled = memoryEnabled
        });
    }
#pragma warning restore S107

    /// <summary>
    /// Sets the manager agent for hierarchical process.
    /// </summary>
    public void SetManagerAgent(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (ProcessType != ProcessType.Hierarchical)
            throw new InvalidOperationException("Manager agent is only applicable for hierarchical process.");

        if (!_memberManager.Contains(agentId))
            throw new InvalidOperationException($"Agent {agentId} is not in this crew.");

        ManagerAgentId = agentId;
    }

    /// <summary>
    /// Validates the crew configuration.
    /// </summary>
    public ValidationResult Validate()
    {
        var validationResult = new ValidationResult();

        if (!_memberManager.Any())
            validationResult.AddError(nameof(Agents), "Crew must have at least one agent.");

        if (!_taskManager.Any())
            validationResult.AddError(nameof(Tasks), "Crew must have at least one task.");

        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null)
            validationResult.AddError(nameof(ManagerAgentId), "Hierarchical process requires a manager agent.");

        if (ProcessType == ProcessType.Hierarchical && !_memberManager.Contains(ManagerAgentId!))
            validationResult.AddError(nameof(ManagerAgentId), "Manager agent must be a member of the crew.");

        return validationResult;
    }

    /// <summary>
    /// Validates that the crew can be kicked off, throwing if it cannot.
    /// This is a domain validation method for use by the Application orchestration layer.
    /// </summary>
    public void ValidateCanKickoff()
    {
        var validation = Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Crew validation failed: {string.Join(", ", validation.Errors)}");
        }

        if (Status == CrewStatus.Executing)
        {
            throw new InvalidOperationException("Crew is already executing");
        }

        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null && ManagerLlm == null)
        {
            throw new InvalidOperationException(
                "Hierarchical process requires either a manager agent or manager LLM");
        }
    }

    /// <summary>
    /// Restores a Crew aggregate from persisted state without raising domain events.
    /// For use by repositories when reconstituting from storage.
    /// </summary>
#pragma warning disable S107 // Rehydration factory; parameters map 1:1 to persisted columns
    internal static Crew Restore(
        CrewId id,
        CrewGoal goal,
        ProcessType processType,
        bool verbose = false,
        bool planning = false,
        int maxRpm = 100,
        bool shareCrew = true,
        string? outputLogFile = null,
        ILlmProvider? managerLlm = null,
        AgentId? managerAgentId = null,
        string language = "en",
        bool fullOutput = false,
        ILlmProvider? planningLlm = null,
        bool memoryEnabled = false,
        CrewStatus? status = null)
    {
        var crew = new Crew(id)
        {
            Goal = goal,
            ProcessType = processType ?? ProcessType.Sequential,
            Verbose = verbose,
            Planning = planning,
            Status = status ?? CrewStatus.Idle,
            MaxRpm = maxRpm > 0 ? maxRpm : 100,
            ShareCrew = shareCrew,
            OutputLogFile = outputLogFile,
            ManagerLlm = managerLlm,
            ManagerAgentId = managerAgentId,
            Language = !string.IsNullOrWhiteSpace(language) ? LanguageCode.From(language) : LanguageCode.Default,
            FullOutput = fullOutput,
            PlanningLlm = planningLlm,
            MemoryEnabled = memoryEnabled
        };

        return crew;
    }
#pragma warning restore S107

    /// <summary>
    /// Injects the kill-all delegate from the Application layer.
    /// </summary>
    public void SetKillAllStrategy(Func<IEnumerable<AgentId>, string, System.Threading.Tasks.Task> killAllFunc)
    {
        ArgumentNullException.ThrowIfNull(killAllFunc);
        _killAllFunc = killAllFunc;
    }

    /// <summary>
    /// Stops all agents in this crew.
    /// </summary>
    public async System.Threading.Tasks.Task StopAllAgentsAsync(string reason = "Crew stopped all agents")
    {
        if (_killAllFunc is not null)
        {
            await _killAllFunc(_agents.AsReadOnly(), reason).ConfigureAwait(false);
        }
    }

}
