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
    private ProcessType _processType = ProcessType.Sequential;
    private bool _verbose;
    private bool _planning;
    private int _maxRpm = CrewDefaults.DefaultMaxRpm;
    private bool _shareCrew = true;
    private string? _outputLogFile;
    private ILlmProvider? _managerLlm;
    private AgentId? _managerAgentId;
    private string _language = CrewDefaults.DefaultLanguage;
    private bool _fullOutput;
    private IStepCallback? _stepCallback;
    private ITaskCallback? _taskCallback;
    private ILlmProvider? _planningLlm;
    private bool _memoryEnabled;
    private string? _memoryProvider;
    private bool _allowDynamicAgents;
    private int? _maxConcurrentDynamicAgents;
    private GraphConfig? _graphConfig;
    private CircuitBreakerConfig? _circuitBreaker;

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

    /// <summary>Enables or disables planning before execution.</summary>
    public CrewBuilder Planning(bool planning = true)
    {
        _planning = planning;
        return this;
    }

    /// <summary>Sets the LLM provider for planning.</summary>
    public CrewBuilder WithPlanningLlm(ILlmProvider planningLlm)
    {
        _planningLlm = planningLlm;
        return this;
    }

    /// <summary>Sets the maximum requests per minute.</summary>
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

    /// <summary>Enables or disables memory for the crew.</summary>
    public CrewBuilder EnableMemory(bool memoryEnabled = true)
    {
        _memoryEnabled = memoryEnabled;
        return this;
    }

    /// <summary>Selects the crew's memory provider (e.g. <c>redis</c>, <c>sqlite</c>); resolved at kickoff.</summary>
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

    /// <summary>Sets the manager agent for hierarchical process.</summary>
    public CrewBuilder WithManager(DomainAgent managerAgent)
    {
        ArgumentNullException.ThrowIfNull(managerAgent);
        _managerAgentId = managerAgent.Id;
        return this;
    }

    /// <summary>Sets the manager agent ID for hierarchical process.</summary>
    public CrewBuilder WithManagerId(AgentId managerAgentId)
    {
        _managerAgentId = managerAgentId;
        return this;
    }

    /// <summary>Sets the manager LLM for hierarchical process.</summary>
    public CrewBuilder WithManagerLlm(ILlmProvider managerLlm)
    {
        _managerLlm = managerLlm;
        return this;
    }

    /// <summary>Sets the step callback for execution progress.</summary>
    public CrewBuilder WithStepCallback(IStepCallback stepCallback)
    {
        _stepCallback = stepCallback;
        return this;
    }

    /// <summary>Sets the task callback for task completion.</summary>
    public CrewBuilder WithTaskCallback(ITaskCallback taskCallback)
    {
        _taskCallback = taskCallback;
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

    /// <summary>Sets the crew-level circuit-breaker configuration.</summary>
    public CrewBuilder WithCircuitBreaker(CircuitBreakerConfig circuitBreaker)
    {
        ArgumentNullException.ThrowIfNull(circuitBreaker);
        _circuitBreaker = circuitBreaker;
        return this;
    }

    /// <summary>
    /// Builds and returns a new <see cref="Crew"/> instance.
    /// </summary>
    /// <exception cref="BuilderValidationException">
    /// Thrown when <see cref="Goal(string)"/> has not been set, or when a Hierarchical process
    /// is configured without a manager agent or manager LLM.
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

        // 5. Create the crew
        var crew = Crew.Create(new CrewCreateOptions
        {
            Goal = _goal,
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
            StepCallback = _stepCallback,
            TaskCallback = _taskCallback,
            PlanningLlm = _planningLlm,
            MemoryEnabled = _memoryEnabled,
            MemoryProvider = _memoryProvider,
            AllowDynamicAgents = _allowDynamicAgents,
            MaxConcurrentDynamicAgents = _maxConcurrentDynamicAgents,
            GraphConfig = _graphConfig,
            CircuitBreaker = _circuitBreaker
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
