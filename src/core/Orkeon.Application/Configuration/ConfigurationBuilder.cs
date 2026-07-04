using System.Collections.Immutable;
using Orkeon.Application.Constants.Execution;
using static Orkeon.Domain.Constants.Memory.MemoryDefaults;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Builder pattern for creating immutable configurations.
/// Phase 3.3.1: Fluent builder pattern for immutable configuration construction.
/// </summary>
public sealed class CrewConfigurationBuilder
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private ProcessType _processType = ProcessType.Sequential;
    private VerbosityLevel _verbosity = VerbosityLevel.Normal;
    private bool _allowCodeExecution;
    private int _maxAgents = 10;
    private int _maxTasks = 100;
    private TimeSpan _executionTimeout = CrewDefaults.DefaultExecutionTimeout;
    private readonly List<AgentConfiguration> _agents = [];
    private readonly List<TaskConfiguration> _tasks = [];
    private MemoryConfiguration _memory = MemoryConfiguration.Default;
    private RetryConfiguration _retry = RetryConfiguration.Default;
    private readonly Dictionary<string, object> _metadata = [];

    /// <summary>
    /// Sets the crew name.
    /// </summary>
    public CrewConfigurationBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the crew description.
    /// </summary>
    public CrewConfigurationBuilder WithDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _description = description;
        return this;
    }

    /// <summary>
    /// Sets the process type.
    /// </summary>
    public CrewConfigurationBuilder WithProcessType(ProcessType processType)
    {
        _processType = processType;
        return this;
    }

    /// <summary>
    /// Sets the verbosity level.
    /// </summary>
    public CrewConfigurationBuilder WithVerbosity(VerbosityLevel verbosity)
    {
        _verbosity = verbosity;
        return this;
    }

    /// <summary>
    /// Enables or disables code execution.
    /// </summary>
    public CrewConfigurationBuilder WithCodeExecution(bool allowCodeExecution)
    {
        _allowCodeExecution = allowCodeExecution;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of agents.
    /// </summary>
    public CrewConfigurationBuilder WithMaxAgents(int maxAgents)
    {
        _maxAgents = maxAgents > 0 ? maxAgents : throw new ArgumentException("Max agents must be positive", nameof(maxAgents));
        return this;
    }

    /// <summary>
    /// Sets the maximum number of tasks.
    /// </summary>
    public CrewConfigurationBuilder WithMaxTasks(int maxTasks)
    {
        _maxTasks = maxTasks > 0 ? maxTasks : throw new ArgumentException("Max tasks must be positive", nameof(maxTasks));
        return this;
    }

    /// <summary>
    /// Sets the execution timeout.
    /// </summary>
    public CrewConfigurationBuilder WithExecutionTimeout(TimeSpan timeout)
    {
        _executionTimeout = timeout > TimeSpan.Zero ? timeout : throw new ArgumentException("Timeout must be positive", nameof(timeout));
        return this;
    }

    /// <summary>
    /// Adds an agent to the crew.
    /// </summary>
    public CrewConfigurationBuilder AddAgent(AgentConfiguration agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agents.Add(agent);
        return this;
    }

    /// <summary>
    /// Adds an agent using a builder.
    /// </summary>
    public CrewConfigurationBuilder AddAgent(Action<AgentConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AgentConfigurationBuilder();
        configure(builder);
        return AddAgent(builder.Build());
    }

    /// <summary>
    /// Adds multiple agents.
    /// </summary>
    public CrewConfigurationBuilder AddAgents(IEnumerable<AgentConfiguration> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents.AddRange(agents);
        return this;
    }

    /// <summary>
    /// Adds a task to the crew.
    /// </summary>
    public CrewConfigurationBuilder AddTask(TaskConfiguration task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _tasks.Add(task);
        return this;
    }

    /// <summary>
    /// Adds a task using a builder.
    /// </summary>
    public CrewConfigurationBuilder AddTask(Action<TaskConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TaskConfigurationBuilder();
        configure(builder);
        return AddTask(builder.Build());
    }

    /// <summary>
    /// Adds multiple tasks.
    /// </summary>
    public CrewConfigurationBuilder AddTasks(IEnumerable<TaskConfiguration> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks.AddRange(tasks);
        return this;
    }

    /// <summary>
    /// Sets the memory configuration.
    /// </summary>
    public CrewConfigurationBuilder WithMemory(MemoryConfiguration memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        return this;
    }

    /// <summary>
    /// Sets the memory configuration using a builder.
    /// </summary>
    public CrewConfigurationBuilder WithMemory(Action<MemoryConfigurationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MemoryConfigurationBuilder();
        configure(builder);
        return WithMemory(builder.Build());
    }

    /// <summary>
    /// Sets the retry configuration.
    /// </summary>
    public CrewConfigurationBuilder WithRetry(RetryConfiguration retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        _retry = retry;
        return this;
    }

    /// <summary>
    /// Adds metadata.
    /// </summary>
    public CrewConfigurationBuilder WithMetadata(string key, object value)
    {
        _metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Builds the immutable crew configuration.
    /// </summary>
    public CrewConfiguration Build()
    {
        return new CrewConfiguration
        {
            Name = _name,
            Description = _description,
            ProcessType = _processType,
            Verbosity = _verbosity,
            AllowCodeExecution = _allowCodeExecution,
            MaxAgents = _maxAgents,
            MaxTasks = _maxTasks,
            ExecutionTimeout = _executionTimeout,
            Agents = _agents.ToImmutableList(),
            Tasks = _tasks.ToImmutableList(),
            Memory = _memory,
            Retry = _retry,
            Metadata = _metadata.ToImmutableDictionary()
        };
    }
}

/// <summary>
/// Builder for agent configurations.
/// </summary>
public sealed class AgentConfigurationBuilder
{
    private string _name = string.Empty;
    private string _role = string.Empty;
    private string _goal = string.Empty;
    private string _backstory = string.Empty;
    private AgentType _type = AgentType.Worker;
    private bool _verbose;
    private bool _allowDelegation;
    private int _maxExecutionTime = ExecutionDefaults.DefaultMaxExecutionSeconds;
    private readonly List<string> _tools = [];
    private LlmConfiguration _llm = LlmConfiguration.Default;
    private readonly Dictionary<string, object> _parameters = [];

    /// <summary>
    /// With Name.
    /// </summary>
    public AgentConfigurationBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// With Role.
    /// </summary>
    public AgentConfigurationBuilder WithRole(string role)
    {
        ArgumentNullException.ThrowIfNull(role);
        _role = role;
        return this;
    }

    /// <summary>
    /// With Goal.
    /// </summary>
    public AgentConfigurationBuilder WithGoal(string goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goal = goal;
        return this;
    }

    /// <summary>
    /// With Backstory.
    /// </summary>
    public AgentConfigurationBuilder WithBackstory(string backstory)
    {
        ArgumentNullException.ThrowIfNull(backstory);
        _backstory = backstory;
        return this;
    }

    /// <summary>
    /// With Type.
    /// </summary>
    public AgentConfigurationBuilder WithType(AgentType type)
    {
        _type = type;
        return this;
    }

    /// <summary>
    /// With Verbose.
    /// </summary>
    public AgentConfigurationBuilder WithVerbose(bool verbose)
    {
        _verbose = verbose;
        return this;
    }

    /// <summary>
    /// With Delegation.
    /// </summary>
    public AgentConfigurationBuilder WithDelegation(bool allowDelegation)
    {
        _allowDelegation = allowDelegation;
        return this;
    }

    /// <summary>
    /// With Max Execution Time.
    /// </summary>
    public AgentConfigurationBuilder WithMaxExecutionTime(int seconds)
    {
        _maxExecutionTime = seconds > 0 ? seconds : throw new ArgumentException("Execution time must be positive", nameof(seconds));
        return this;
    }

    /// <summary>
    /// Add Tool.
    /// </summary>
    public AgentConfigurationBuilder AddTool(string tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools.Add(tool);
        return this;
    }

    /// <summary>
    /// Add Tools.
    /// </summary>
    public AgentConfigurationBuilder AddTools(IEnumerable<string> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools.AddRange(tools);
        return this;
    }

    /// <summary>
    /// With Llm.
    /// </summary>
    public AgentConfigurationBuilder WithLlm(LlmConfiguration llm)
    {
        ArgumentNullException.ThrowIfNull(llm);
        _llm = llm;
        return this;
    }

    /// <summary>
    /// With Parameter.
    /// </summary>
    public AgentConfigurationBuilder WithParameter(string key, object value)
    {
        _parameters[key] = value;
        return this;
    }

    /// <summary>
    /// Build.
    /// </summary>
    public AgentConfiguration Build()
    {
        return new AgentConfiguration
        {
            Name = _name,
            Role = _role,
            Goal = _goal,
            Backstory = _backstory,
            Type = _type,
            Verbose = _verbose,
            AllowDelegation = _allowDelegation,
            MaxExecutionTime = _maxExecutionTime,
            Tools = _tools.ToImmutableList(),
            Llm = _llm,
            Parameters = _parameters.ToImmutableDictionary()
        };
    }
}

/// <summary>
/// Builder for task configurations.
/// </summary>
public sealed class TaskConfigurationBuilder
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _expectedOutput = string.Empty;
    private string? _assignedAgent;
    private Orkeon.Application.Configuration.TaskPriority _priority = Orkeon.Application.Configuration.TaskPriority.Medium;
    private bool _isAsync;
    private TimeSpan _estimatedDuration = CrewDefaults.DefaultEstimatedTaskDuration;
    private readonly List<string> _dependencies = [];
    private readonly List<string> _requiredTools = [];
    private readonly Dictionary<string, object> _context = [];

    /// <summary>
    /// With Name.
    /// </summary>
    public TaskConfigurationBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// With Description.
    /// </summary>
    public TaskConfigurationBuilder WithDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _description = description;
        return this;
    }

    /// <summary>
    /// With Expected Output.
    /// </summary>
    public TaskConfigurationBuilder WithExpectedOutput(string expectedOutput)
    {
        ArgumentNullException.ThrowIfNull(expectedOutput);
        _expectedOutput = expectedOutput;
        return this;
    }

    /// <summary>
    /// Assign To.
    /// </summary>
    public TaskConfigurationBuilder AssignTo(string agentName)
    {
        _assignedAgent = agentName;
        return this;
    }

    /// <summary>
    /// With Priority.
    /// </summary>
    public TaskConfigurationBuilder WithPriority(TaskPriority priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// With Async.
    /// </summary>
    public TaskConfigurationBuilder WithAsync(bool isAsync)
    {
        _isAsync = isAsync;
        return this;
    }

    /// <summary>
    /// With Estimated Duration.
    /// </summary>
    public TaskConfigurationBuilder WithEstimatedDuration(TimeSpan duration)
    {
        _estimatedDuration = duration > TimeSpan.Zero ? duration : throw new ArgumentException("Duration must be positive", nameof(duration));
        return this;
    }

    /// <summary>
    /// Add Dependency.
    /// </summary>
    public TaskConfigurationBuilder AddDependency(string dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        _dependencies.Add(dependency);
        return this;
    }

    /// <summary>
    /// Add Required Tool.
    /// </summary>
    public TaskConfigurationBuilder AddRequiredTool(string tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _requiredTools.Add(tool);
        return this;
    }

    /// <summary>
    /// With Context.
    /// </summary>
    public TaskConfigurationBuilder WithContext(string key, object value)
    {
        _context[key] = value;
        return this;
    }

    /// <summary>
    /// Build.
    /// </summary>
    public TaskConfiguration Build()
    {
        return new TaskConfiguration
        {
            Name = _name,
            Description = _description,
            ExpectedOutput = _expectedOutput,
            AssignedAgent = _assignedAgent,
            Priority = _priority,
            IsAsync = _isAsync,
            EstimatedDuration = _estimatedDuration,
            Dependencies = _dependencies.ToImmutableList(),
            RequiredTools = _requiredTools.ToImmutableList(),
            Context = _context.ToImmutableDictionary()
        };
    }
}

/// <summary>
/// Builder for memory configurations.
/// </summary>
public sealed class MemoryConfigurationBuilder
{
    private string _provider = DefaultProvider;
    private bool _enableShortTerm = true;
    private bool _enableLongTerm;
    private bool _enableEpisodic;
    private int _maxShortTermEntries = DefaultMaxShortTermEntries;
    private int _maxLongTermEntries = DefaultMaxLongTermEntries;
    private TimeSpan _retentionPeriod = MemoryDefaults.DefaultRetentionPeriod;
    private readonly Dictionary<string, object> _providerSettings = [];

    /// <summary>
    /// With Provider.
    /// </summary>
    public MemoryConfigurationBuilder WithProvider(string provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        return this;
    }

    /// <summary>
    /// Enable Short Term.
    /// </summary>
    public MemoryConfigurationBuilder EnableShortTerm(bool enable = true)
    {
        _enableShortTerm = enable;
        return this;
    }

    /// <summary>
    /// Enable Long Term.
    /// </summary>
    public MemoryConfigurationBuilder EnableLongTerm(bool enable = true)
    {
        _enableLongTerm = enable;
        return this;
    }

    /// <summary>
    /// Enable Episodic.
    /// </summary>
    public MemoryConfigurationBuilder EnableEpisodic(bool enable = true)
    {
        _enableEpisodic = enable;
        return this;
    }

    /// <summary>
    /// With Max Short Term Entries.
    /// </summary>
    public MemoryConfigurationBuilder WithMaxShortTermEntries(int maxEntries)
    {
        _maxShortTermEntries = maxEntries > 0 ? maxEntries : throw new ArgumentException("Max entries must be positive", nameof(maxEntries));
        return this;
    }

    /// <summary>
    /// With Max Long Term Entries.
    /// </summary>
    public MemoryConfigurationBuilder WithMaxLongTermEntries(int maxEntries)
    {
        _maxLongTermEntries = maxEntries > 0 ? maxEntries : throw new ArgumentException("Max entries must be positive", nameof(maxEntries));
        return this;
    }

    /// <summary>
    /// With Retention Period.
    /// </summary>
    public MemoryConfigurationBuilder WithRetentionPeriod(TimeSpan period)
    {
        _retentionPeriod = period > TimeSpan.Zero ? period : throw new ArgumentException("Retention period must be positive", nameof(period));
        return this;
    }

    /// <summary>
    /// With Provider Setting.
    /// </summary>
    public MemoryConfigurationBuilder WithProviderSetting(string key, object value)
    {
        _providerSettings[key] = value;
        return this;
    }

    /// <summary>
    /// Build.
    /// </summary>
    public MemoryConfiguration Build()
    {
        return new MemoryConfiguration
        {
            Provider = _provider,
            EnableShortTerm = _enableShortTerm,
            EnableLongTerm = _enableLongTerm,
            EnableEpisodic = _enableEpisodic,
            MaxShortTermEntries = _maxShortTermEntries,
            MaxLongTermEntries = _maxLongTermEntries,
            RetentionPeriod = _retentionPeriod,
            ProviderSettings = _providerSettings.ToImmutableDictionary()
        };
    }
}
