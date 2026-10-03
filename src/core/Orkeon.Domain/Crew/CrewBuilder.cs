using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Fluent builder for creating <see cref="Crew"/> instances.
/// Wraps <see cref="Crew.Create(CrewCreateOptions)"/> with a discoverable, chainable API.
/// </summary>
public sealed class CrewBuilder
{
    private string? _goal;
    private string? _name;
    private ProcessType _processType = ProcessType.Sequential;
    private bool _verbose;
    private bool _planning;
    private int? _maxRpm;
    private bool _shareCrew = true;
    private string? _outputLogFile;
    private ILlmProvider? _managerLlm;
    private AgentId? _managerAgentId;
    private string _language = CrewDefaults.DefaultLanguage;
    private bool _fullOutput;
    private ILlmProvider? _planningLlm;
    private bool _memoryEnabled;
    private string? _memoryProvider;
    private bool _allowDynamicAgents;
    private int? _maxConcurrentDynamicAgents;
    private GraphConfig? _graphConfig;

    private readonly List<DomainAgent> _agents = [];
    private readonly List<CrewTask> _tasks = [];
    private readonly List<Action<AgentBuilder>> _agentConfigurators = [];
    private readonly List<Action<CrewTaskBuilder>> _taskConfigurators = [];

    /// <summary>Sets the crew goal.</summary>
    public CrewBuilder Goal(string goal)
    {
        _goal = goal;
        return this;
    }

    /// <summary>
    /// Names the crew. The name scopes its long-term memory: a crew of this name reads what its
    /// earlier runs stored, and never another crew's. Without one, the memory lasts one run.
    /// </summary>
    public CrewBuilder Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Sets the process type to Sequential.</summary>
    public CrewBuilder Sequential()
    {
        _processType = ProcessType.Sequential;
        return this;
    }

    /// <summary>Sets the process type to Hierarchical with an optional manager agent.</summary>
    public CrewBuilder Hierarchical(DomainAgent? managerAgent = null)
    {
        _processType = ProcessType.Hierarchical;
        if (managerAgent is not null)
        {
            _managerAgentId = managerAgent.Id;
        }
        return this;
    }

    /// <summary>Sets the process type to Hierarchical with a manager agent ID.</summary>
    public CrewBuilder Hierarchical(AgentId managerAgentId)
    {
        _processType = ProcessType.Hierarchical;
        _managerAgentId = managerAgentId;
        return this;
    }

    /// <summary>Sets the process type to Parallel.</summary>
    public CrewBuilder Parallel()
    {
        _processType = ProcessType.Parallel;
        return this;
    }

    /// <summary>Sets the process type to Consensual.</summary>
    public CrewBuilder Consensual()
    {
        _processType = ProcessType.Consensual;
        return this;
    }

    /// <summary>Sets the process type explicitly.</summary>
    public CrewBuilder Process(ProcessType processType)
    {
        _processType = processType;
        return this;
    }

    /// <summary>Adds a pre-built agent to the crew.</summary>
    public CrewBuilder WithAgent(DomainAgent agent)
    {
        _agents.Add(agent);
        return this;
    }

    /// <summary>Adds an agent defined inline via a configurator action.</summary>
    public CrewBuilder WithAgent(Action<AgentBuilder> configurator)
    {
        _agentConfigurators.Add(configurator);
        return this;
    }

    /// <summary>Adds multiple pre-built agents to the crew.</summary>
    public CrewBuilder WithAgents(IEnumerable<DomainAgent> agents)
    {
        _agents.AddRange(agents);
        return this;
    }

    /// <summary>Adds a pre-built task to the crew.</summary>
    public CrewBuilder WithTask(Task.CrewTask task)
    {
        _tasks.Add(task);
        return this;
    }

    /// <summary>Adds a task defined inline via a configurator action.</summary>
    public CrewBuilder WithTask(Action<CrewTaskBuilder> configurator)
    {
        _taskConfigurators.Add(configurator);
        return this;
    }

    /// <summary>Adds multiple pre-built tasks to the crew.</summary>
    public CrewBuilder WithTasks(IEnumerable<Task.CrewTask> tasks)
    {
        _tasks.AddRange(tasks);
        return this;
    }

    /// <summary>Enables or disables verbose logging.</summary>
    public CrewBuilder Verbose(bool verbose = true)
    {
        _verbose = verbose;
        return this;
    }

    /// <summary>
    /// Enables or disables planning: before the first task, a planner — on the provider
    /// <see cref="WithPlanningLlm"/> sets, else on the host's default LLM profile — writes a
    /// step-by-step plan for each task, which the task reads in its prompt, in every mode. The plan
    /// changes neither the order of the tasks nor who runs them (GAP-31).
    /// </summary>
    public CrewBuilder Planning(bool planning = true)
    {
        _planning = planning;
        return this;
    }

    /// <summary>
    /// Sets the LLM provider the crew plans on, instead of the host's default profile; its calls are
    /// metered like those of a provider the host registers, as the planning work of the crew. Planning
    /// itself is switched on by <see cref="Planning"/>: <see cref="Build"/> refuses a planning
    /// provider for a crew that does not plan (GAP-33).
    /// </summary>
    public CrewBuilder WithPlanningLlm(ILlmProvider planningLlm)
    {
        _planningLlm = planningLlm;
        return this;
    }

    /// <summary>
    /// Sets the model requests the crew may make per minute, all its agents and its manager together —
    /// CrewAI's <c>max_rpm</c>: the request of too many waits its turn (GAP-38). Not called, the crew has
    /// no limit of its own.
    /// </summary>
    public CrewBuilder MaxRpm(int maxRpm)
    {
        _maxRpm = maxRpm;
        return this;
    }

    /// <summary>Sets the language for crew output.</summary>
    public CrewBuilder Language(string language)
    {
        _language = language;
        return this;
    }

    /// <summary>Enables or disables full output from all tasks.</summary>
    public CrewBuilder FullOutput(bool fullOutput = true)
    {
        _fullOutput = fullOutput;
        return this;
    }

    /// <summary>
    /// Enables or disables the crew's memory: with it on, a run stores the result of each task and
    /// recalls the crew's memories before each task (GAP-30). Off by default.
    /// </summary>
    public CrewBuilder EnableMemory(bool memoryEnabled = true)
    {
        _memoryEnabled = memoryEnabled;
        return this;
    }

    /// <summary>
    /// Selects the crew's memory provider (e.g. <c>redis</c>, <c>sqlite</c>); resolved at kickoff.
    /// Needs <see cref="EnableMemory"/>: <see cref="Build"/> refuses a provider for a crew without
    /// memory (GAP-30).
    /// </summary>
    public CrewBuilder WithMemoryProvider(string memoryProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryProvider);
        _memoryProvider = memoryProvider;
        return this;
    }

    /// <summary>Enables or disables sharing crew information among agents.</summary>
    public CrewBuilder ShareCrew(bool shareCrew = true)
    {
        _shareCrew = shareCrew;
        return this;
    }

    /// <summary>
    /// Sets the crew's manager agent: the hierarchical manager — it assigns each task and reviews its
    /// output — or the consensual crew's arbiter of the <c>ManagerDecision</c> fallback. It runs no
    /// task. The other processes have no manager agent: <see cref="Build"/> refuses one there (GAP-33).
    /// </summary>
    public CrewBuilder WithManager(DomainAgent managerAgent)
    {
        ArgumentNullException.ThrowIfNull(managerAgent);
        _managerAgentId = managerAgent.Id;
        return this;
    }

    /// <summary>
    /// Sets the crew's manager agent by its id — see <see cref="WithManager"/>: Hierarchical and
    /// Consensual only.
    /// </summary>
    public CrewBuilder WithManagerId(AgentId managerAgentId)
    {
        _managerAgentId = managerAgentId;
        return this;
    }

    /// <summary>
    /// Sets the provider the crew's manager runs on — CrewAI's <c>manager_llm</c>. The hierarchical
    /// manager assigns and reviews on it (and the autonomous one hands the tasks out on it), in place
    /// of the manager agent's <c>llm:</c> profile or the host's default; its calls are metered like
    /// those of a provider the host registers (GAP-19). A hierarchical crew given one needs no
    /// manager agent: every agent is then a worker. The other processes never call it:
    /// <see cref="Build"/> refuses it there (GAP-33).
    /// </summary>
    public CrewBuilder WithManagerLlm(ILlmProvider managerLlm)
    {
        _managerLlm = managerLlm;
        return this;
    }

    /// <summary>Sets the output log file path.</summary>
    public CrewBuilder OutputLogFile(string outputLogFile)
    {
        _outputLogFile = outputLogFile;
        return this;
    }

    /// <summary>Enables or disables dynamic agent creation during execution.</summary>
    public CrewBuilder AllowDynamicAgents(bool allow = true, int? maxConcurrent = null)
    {
        _allowDynamicAgents = allow;
        _maxConcurrentDynamicAgents = maxConcurrent;
        return this;
    }

    /// <summary>Sets the graph-orchestration configuration (only consumed for the Graph process).</summary>
    public CrewBuilder WithGraphConfig(GraphConfig graphConfig)
    {
        ArgumentNullException.ThrowIfNull(graphConfig);
        _graphConfig = graphConfig;
        return this;
    }

    /// <summary>
    /// Builds and returns a new <see cref="Crew"/> instance.
    /// </summary>
    /// <exception cref="BuilderValidationException">
    /// Thrown when <see cref="Goal(string)"/> has not been set, when a Hierarchical process
    /// is configured without a manager agent or manager LLM, when a task asks for asynchronous
    /// execution in a process that does not honour it (<see cref="ProcessType.AcceptsAsyncExecution"/>),
    /// or when a manager agent or a manager LLM is set for a process that has none
    /// (<see cref="ProcessType.AcceptsManagerAgent"/>, <see cref="ProcessType.AcceptsManagerLlm"/>).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by <see cref="Crew.Create(CrewCreateOptions)"/> for a memory provider without
    /// <see cref="EnableMemory"/>, or a planning provider without <see cref="Planning"/>.
    /// </exception>
    public Crew Build()
    {
        // 1. Validate goal
        if (string.IsNullOrWhiteSpace(_goal))
            throw new BuilderValidationException("Crew", "Goal is required.");

        // 2. Build inline agents from configurators
        foreach (var configurator in _agentConfigurators)
        {
            var agentBuilder = new AgentBuilder();
            configurator(agentBuilder);
            _agents.Add(agentBuilder.Build());
        }

        // 3. Build inline tasks from configurators
        foreach (var configurator in _taskConfigurators)
        {
            var taskBuilder = new CrewTaskBuilder();
            configurator(taskBuilder);
            _tasks.Add(taskBuilder.Build());
        }

        // 4. Validate Hierarchical has manager
        if (_processType == ProcessType.Hierarchical && _managerAgentId is null && _managerLlm is null)
            throw new BuilderValidationException("Crew", "Hierarchical process requires either a manager agent or a manager LLM.");

        // 4b. A task asks for asynchronous execution only where the mode honours it (GAP-22): the
        //     other modes order their tasks themselves and would drop the promise in silence.
        var asyncTasks = _tasks.Where(task => task.AsyncExecution).ToList();
        if (asyncTasks.Count > 0 && !_processType.AcceptsAsyncExecution)
        {
            var named = string.Join(", ", asyncTasks.Select(task => $"'{task.Description.Value}'"));
            throw new BuilderValidationException(
                "Crew",
                (asyncTasks.Count == 1 ? $"Task {named} asks" : $"Tasks {named} ask") +
                $" for asynchronous execution (.Async()), which the {_processType.Value} process does not honour: " +
                "it orders its tasks itself. Use .Sequential() — an async task runs alongside the tasks after it — " +
                "or .Parallel(), or drop .Async().");
        }

        // 4c. A manager where the mode uses one, never elsewhere (GAP-33): the four modes without a
        //     manager agent ran it as one more worker, and only two modes ever call a manager LLM.
        if (_managerAgentId is not null && !_processType.AcceptsManagerAgent)
        {
            throw new BuilderValidationException(
                "Crew",
                $"A manager agent is set (.WithManager, .WithManagerId or .Hierarchical(manager)), but the {_processType.Value} " +
                "process has no manager: the agent would only be one more worker. Remove it, or use .Hierarchical(manager) — " +
                "it assigns each task and reviews its output — or .Consensual() — it arbitrates when the vote fails " +
                "(Orkeon:Consensus:FallbackStrategy: ManagerDecision)." +
                (_processType == ProcessType.Autonomous
                    ? " An autonomous crew's manager is an LLM: the host's default profile, or the provider .WithManagerLlm(provider) sets."
                    : string.Empty));
        }

        if (_managerLlm is not null && !_processType.AcceptsManagerLlm)
        {
            throw new BuilderValidationException(
                "Crew",
                $".WithManagerLlm(provider) is set, but the {_processType.Value} process never calls a manager LLM. Remove it, " +
                "or use .Hierarchical() — the manager assigns and reviews on it — or the Autonomous process — the manager " +
                "hands the tasks out on it.");
        }

        // 5. Create the crew
        var crew = Crew.Create(new CrewCreateOptions
        {
            Goal = _goal,
            Name = _name,
            ProcessType = _processType,
            Verbose = _verbose,
            Planning = _planning,
            MaxRpm = _maxRpm,
            ShareCrew = _shareCrew,
            OutputLogFile = _outputLogFile,
            ManagerLlm = _managerLlm,
            ManagerAgentId = _managerAgentId,
            Language = _language,
            FullOutput = _fullOutput,
            PlanningLlm = _planningLlm,
            MemoryEnabled = _memoryEnabled,
            MemoryProvider = _memoryProvider,
            AllowDynamicAgents = _allowDynamicAgents,
            MaxConcurrentDynamicAgents = _maxConcurrentDynamicAgents,
            GraphConfig = _graphConfig
        });

        // 6. Add agents
        foreach (var agent in _agents)
        {
            crew.AddAgent(agent.Id);
        }

        // 7. Add tasks
        foreach (var task in _tasks)
        {
            crew.AddTask(task.Id);
        }

        // 8. Return crew
        return crew;
    }
}
