using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Configuration;
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
    /// Gets the crew's name — the <c>name:</c> of its configuration (YAML, crew directory,
    /// <c>.ork.ts</c>), trimmed. It scopes the crew's long-term memory: recorded at kickoff next
    /// to <see cref="MemoryProvider"/>, it lets the crews of one name read what their earlier runs
    /// stored, and no other crew's. Null for a crew built without one (C#), whose memory lasts one
    /// run.
    /// </summary>
    public string? Name { get; private set; }

    /// <summary>
    /// Gets the process type used by this crew.
    /// </summary>
    public ProcessType ProcessType { get; private set; }

    /// <summary>
    /// Gets whether verbose logging is enabled.
    /// </summary>
    public bool Verbose { get; private set; }

    /// <summary>
    /// Gets whether planning is enabled: before its first task, the run has a planner — on
    /// <see cref="PlanningLlm"/> when set, else on the host's default LLM profile (GAP-29) — write a
    /// step-by-step plan for each task, which the task then reads in its prompt, in every mode. The
    /// plan changes neither the order of the tasks nor who runs them (GAP-31).
    /// </summary>
    public bool Planning { get; private set; }

    /// <summary>
    /// Gets the memory-provider selection (e.g. <c>redis</c>, <c>sqlite</c>): where the memory of a
    /// crew with <see cref="MemoryEnabled"/> lives. Null falls back to the host's configured default
    /// provider (<c>Memory:Provider</c>). Resolved to a concrete <c>IMemoryProvider</c> at kickoff.
    /// Set only with <see cref="MemoryEnabled"/>: a provider holds what a crew remembers, and a crew
    /// without memory remembers nothing (GAP-30).
    /// </summary>
    public string? MemoryProvider { get; private set; }

    /// <summary>
    /// Gets the crew's manager agent, one of its agents: the hierarchical manager — it assigns each
    /// task and reviews its output — or the consensual crew's arbiter of the <c>ManagerDecision</c>
    /// fallback. Neither runs a task. Null in the four other modes, which have no manager agent
    /// (<see cref="ProcessType.AcceptsManagerAgent"/>, GAP-33).
    /// </summary>
    public AgentId? ManagerAgentId { get; private set; }

    /// <summary>
    /// Gets the graph-orchestration configuration (retry cycles, circuit-breaker preset/limits).
    /// Only consumed when <see cref="ProcessType"/> is <c>Graph</c>. Null falls back to the
    /// strategy's built-in defaults.
    /// </summary>
    public GraphConfig? GraphConfig { get; private set; }

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
    /// Gets the provider the crew's manager runs on (C# <c>WithManagerLlm</c>, CrewAI's
    /// <c>manager_llm</c>): the hierarchical manager assigns and reviews on it, and the autonomous
    /// one hands the tasks out on it, in place of the manager agent's <c>llm:</c> profile or the
    /// host's default (GAP-19). A crew with one needs no manager agent: every agent is then a worker.
    /// Set only in those two modes (<see cref="ProcessType.AcceptsManagerLlm"/>, GAP-33).
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
    /// Gets the provider the crew plans on (C# <c>WithPlanningLlm</c>), metered like a provider the
    /// host registers; null plans on the host's default LLM profile. Set only with
    /// <see cref="Planning"/>: a crew that does not plan refuses one (GAP-33).
    /// </summary>
    public ILlmProvider? PlanningLlm { get; private set; }

    /// <summary>
    /// Gets whether the crew remembers (<c>memory: true</c>, <c>CrewBuilder.EnableMemory()</c>): only
    /// then does a run store the result of each task in the crew's memory and recall its memories
    /// before each task (GAP-30). Off by default.
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

        if (!options.MemoryEnabled && !string.IsNullOrWhiteSpace(options.MemoryProvider))
            throw new ArgumentException(ProviderWithoutMemory(options.MemoryProvider), nameof(options));

        if (!options.Planning && options.PlanningLlm is not null)
            throw new ArgumentException(PlanningProviderWithoutPlanning, nameof(options));

        // A manager where the mode uses one, never elsewhere (GAP-33): kept, it would be ignored.
        if (options.ManagerAgentId is not null && !options.ProcessType.AcceptsManagerAgent)
            throw new ArgumentException(ManagerAgentTheModeHasNoneOf(options.ProcessType), nameof(options));

        if (options.ManagerLlm is not null && !options.ProcessType.AcceptsManagerLlm)
            throw new ArgumentException(ManagerLlmTheModeNeverCalls(options.ProcessType), nameof(options));

        var crew = new Crew(CrewId.Create())
        {
            Goal = CrewGoal.From(options.Goal),
            Name = string.IsNullOrWhiteSpace(options.Name) ? null : options.Name.Trim(),
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
            PlanningLlm = options.PlanningLlm,
            MemoryEnabled = options.MemoryEnabled,
            MemoryProvider = options.MemoryProvider,
            AllowDynamicAgents = options.AllowDynamicAgents,
            MaxConcurrentDynamicAgents = options.MaxConcurrentDynamicAgents,
            ToolAccessPolicy = options.ToolAccessPolicy,
            GraphConfig = options.GraphConfig
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

        // A hierarchical crew with no manager at all takes its first agent as manager. A crew given
        // a manager LLM has one: every agent it adds is a worker (GAP-19).
        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null && ManagerLlm == null)
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

        // A hierarchical manager agent that leaves is replaced by the first agent left — unless the
        // crew has a manager LLM, which then manages on its own (GAP-19). A consensual crew's
        // arbiter that leaves leaves the crew without one: the manager is a member (GAP-33).
        if (ManagerAgentId == agentId)
        {
            ManagerAgentId = ProcessType == ProcessType.Hierarchical && ManagerLlm == null
                ? _memberManager.FirstOrDefault()
                : null;
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

        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null && ManagerLlm == null)
            throw new InvalidOperationException("Hierarchical process requires a manager agent or a manager LLM.");

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
    /// Completes the current execution successfully: every task the run ran succeeded. A run with a
    /// task that did not succeed fails instead (<see cref="FailExecution"/>, GAP-32).
    /// </summary>
    /// <param name="completedTasks">The tasks the run completed, each counted once by its final outcome.</param>
    public void CompleteExecution(int completedTasks)
    {
        if (Status != CrewStatus.Executing || _currentProcessId == null)
            throw new InvalidOperationException("No execution is currently in progress.");

        var execution = _executions.FirstOrDefault(e => e.ProcessId == _currentProcessId);
        if (execution == null)
            throw new InvalidOperationException("Current execution not found.");

        execution.Complete(completedTasks);

        Status = CrewStatus.Idle;
        var processId = _currentProcessId;
        _currentProcessId = null;

        RaiseDomainEvent(new CrewExecutionCompletedEvent
        {
            CrewId = Id,
            ProcessId = processId,
            Duration = execution.Duration!.Value,
            CompletedTasks = completedTasks,
        });
    }

    /// <summary>
    /// Fails the current execution: a task did not succeed, or the run stopped on an exception or a
    /// cancellation (GAP-32). The crew stays <see cref="CrewStatus.Failed"/> until its next run.
    /// </summary>
    /// <param name="reason">Why the run failed: the crew's error, naming every task that did not succeed.</param>
    /// <param name="exception">The exception that stopped the run, when one did.</param>
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
    /// Changes the process type, and with it the crew's manager (GAP-33): in a mode with a manager
    /// agent (<see cref="ProcessType.AcceptsManagerAgent"/>) the manager is
    /// <paramref name="managerAgentId"/>, one of the crew's agents; in a mode without one the crew
    /// keeps no manager agent — the agent that was one stays a member, hence a worker — and refuses
    /// one passed with the change. A crew with a manager LLM turns only to a mode that reads it
    /// (<see cref="ProcessType.AcceptsManagerLlm"/>).
    /// </summary>
    /// <param name="newProcessType">The mode the crew turns to.</param>
    /// <param name="managerAgentId">
    /// The manager agent in the new mode — required by <see cref="ProcessType.Hierarchical"/> unless
    /// the crew has a manager LLM, optional in <see cref="ProcessType.Consensual"/> (its arbiter),
    /// refused elsewhere.
    /// </param>
    public void ChangeProcessType(ProcessType newProcessType, AgentId? managerAgentId = null)
    {
        ArgumentNullException.ThrowIfNull(newProcessType);

        if (Status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot change process type while crew is executing.");

        if (managerAgentId is not null && !newProcessType.AcceptsManagerAgent)
            throw new InvalidOperationException(ManagerAgentTheModeHasNoneOf(newProcessType));

        if (ManagerLlm is not null && !newProcessType.AcceptsManagerLlm)
            throw new InvalidOperationException(ManagerLlmTheModeNeverCalls(newProcessType));

        if (newProcessType == ProcessType.Hierarchical)
        {
            // A crew with a manager LLM needs no manager agent (GAP-19).
            if ((managerAgentId == null && ManagerLlm == null) || !_memberManager.Any())
                throw new InvalidOperationException("Hierarchical process requires a manager agent.");
        }

        if (managerAgentId is not null && !_memberManager.Contains(managerAgentId))
            throw new InvalidOperationException($"Agent {managerAgentId} is not in this crew.");

        ManagerAgentId = newProcessType.AcceptsManagerAgent ? managerAgentId : null;

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
        {
            if (!update.Planning.Value && PlanningLlm is not null)
                throw new InvalidOperationException(PlanningProviderWithoutPlanning);
            Planning = update.Planning.Value;
        }

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
        {
            if (!update.MemoryEnabled.Value && !string.IsNullOrWhiteSpace(MemoryProvider))
                throw new InvalidOperationException(ProviderWithoutMemory(MemoryProvider));
            MemoryEnabled = update.MemoryEnabled.Value;
        }
    }

    /// <summary>
    /// The refusal of a memory provider named for a crew whose memory is off (GAP-30), with the
    /// remedy in both spellings.
    /// </summary>
    private static string ProviderWithoutMemory(string memoryProvider) =>
        $"The crew names a memory provider ('{memoryProvider}') but its memory is off: a provider holds " +
        "what a crew remembers, and a crew without memory remembers nothing. Turn memory on — memory: true " +
        "in YAML, CrewBuilder.EnableMemory() in C# — or remove the provider (memoryProvider:, WithMemoryProvider).";

    /// <summary>
    /// The refusal of a planning provider given to a crew that does not plan (GAP-33): the provider is
    /// what the planner runs on, the switch is <see cref="Planning"/>.
    /// </summary>
    private const string PlanningProviderWithoutPlanning =
        "The crew is given a planning provider (CrewBuilder.WithPlanningLlm, CrewCreateOptions.PlanningLlm) but does " +
        "not plan: the provider is what the planner runs on, and a crew without planning makes no plan. Switch " +
        "planning on — .Planning() in C#, planning: true in YAML — or remove the provider.";

    /// <summary>
    /// The refusal of a manager agent named for a mode that has none (GAP-33), with the two modes that
    /// use one — and, for an autonomous crew, what its manager is instead.
    /// </summary>
    private static string ManagerAgentTheModeHasNoneOf(ProcessType processType) =>
        $"The {processType.Value} process has no manager agent, yet the crew names one: in that mode the agent " +
        "would only be one more worker. Remove the manager, or use the Hierarchical process — the manager assigns " +
        "each task and reviews its output — or the Consensual one — it arbitrates when the vote fails " +
        "(Orkeon:Consensus:FallbackStrategy: ManagerDecision)." +
        (processType == ProcessType.Autonomous
            ? " An autonomous crew's manager is an LLM: the host's default profile, or the provider " +
              "CrewBuilder.WithManagerLlm sets in C#."
            : string.Empty);

    /// <summary>
    /// The refusal of a manager LLM given to a crew whose mode never calls it (GAP-33), with the two
    /// modes that read one.
    /// </summary>
    private static string ManagerLlmTheModeNeverCalls(ProcessType processType) =>
        $"The {processType.Value} process reads no manager LLM, yet the crew is given one (CrewBuilder.WithManagerLlm): " +
        "its provider would never be called. Remove it, or use the Hierarchical process — the manager assigns and " +
        "reviews on it — or the Autonomous one — the manager hands the tasks out on it.";

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
    /// Sets the manager agent, one of the crew's agents: the hierarchical manager, or the consensual
    /// crew's arbiter. A mode without a manager agent refuses it (GAP-33).
    /// </summary>
    public void SetManagerAgent(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (!ProcessType.AcceptsManagerAgent)
            throw new InvalidOperationException(ManagerAgentTheModeHasNoneOf(ProcessType));

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

        // The manager is an agent of the crew, or the crew's manager LLM (GAP-19).
        if (ProcessType == ProcessType.Hierarchical && ManagerAgentId == null && ManagerLlm == null)
            validationResult.AddError(nameof(ManagerAgentId), "Hierarchical process requires a manager agent or a manager LLM.");

        // In both modes with a manager agent — the hierarchical manager, the consensual arbiter (GAP-33).
        if (ProcessType.AcceptsManagerAgent && ManagerAgentId != null && !_memberManager.Contains(ManagerAgentId))
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
