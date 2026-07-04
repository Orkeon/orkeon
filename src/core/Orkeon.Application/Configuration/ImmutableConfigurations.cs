using System.Collections.Immutable;
using Orkeon.Application.Constants.Execution;
using Orkeon.Application.Constants.Resilience;
using static Orkeon.Domain.Constants.Llm.LlmDefaults;
using static Orkeon.Domain.Constants.Memory.MemoryDefaults;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Constants.Resilience;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Immutable configuration records for Orkeon.
/// Phase 3.3.1: Thread-safe, immutable configurations using C# records.
/// </summary>

/// <summary>
/// Immutable crew configuration with builder pattern support.
/// </summary>
public sealed record CrewConfiguration
{
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets or sets the process type.</summary>
    public ProcessType ProcessType { get; init; } = ProcessType.Sequential;
    /// <summary>Gets or sets the verbosity.</summary>
    public VerbosityLevel Verbosity { get; init; } = VerbosityLevel.Normal;
    /// <summary>
    /// Gets or sets a value indicating whether allow code execution.
    /// </summary>
    public bool AllowCodeExecution { get; init; }

    /// <summary>
    /// Maximum number of agents allowed in this crew. Default: 10.
    /// </summary>
    public int MaxAgents { get; init; } = CrewDefaults.DefaultMaxAgents;

    /// <summary>
    /// Maximum number of tasks allowed in this crew. Default: 100.
    /// </summary>
    public int MaxTasks { get; init; } = CrewDefaults.DefaultMaxTasks;

    /// <summary>
    /// Maximum wall-clock time for the entire crew execution. Default: 30 minutes.
    /// </summary>
    public TimeSpan ExecutionTimeout { get; init; } = CrewDefaults.DefaultExecutionTimeout;
    /// <summary>Gets or sets the agents.</summary>
    public ImmutableList<AgentConfiguration> Agents { get; init; } = [];
    /// <summary>Gets or sets the tasks.</summary>
    public ImmutableList<TaskConfiguration> Tasks { get; init; } = [];
    /// <summary>Gets or sets the memory.</summary>
    public MemoryConfiguration Memory { get; init; } = MemoryConfiguration.Default;
    /// <summary>Gets or sets the retry.</summary>
    public RetryConfiguration Retry { get; init; } = RetryConfiguration.Default;
    /// <summary>Metadata.</summary>
    public ImmutableDictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a new configuration with updated agents.
    /// </summary>
    public CrewConfiguration WithAgents(IEnumerable<AgentConfiguration> agents) =>
        this with { Agents = agents.ToImmutableList() };

    /// <summary>
    /// Creates a new configuration with an additional agent.
    /// </summary>
    public CrewConfiguration AddAgent(AgentConfiguration agent) =>
        this with { Agents = Agents.Add(agent) };

    /// <summary>
    /// Creates a new configuration with updated tasks.
    /// </summary>
    public CrewConfiguration WithTasks(IEnumerable<TaskConfiguration> tasks) =>
        this with { Tasks = tasks.ToImmutableList() };

    /// <summary>
    /// Creates a new configuration with an additional task.
    /// </summary>
    public CrewConfiguration AddTask(TaskConfiguration task) =>
        this with { Tasks = Tasks.Add(task) };

    /// <summary>
    /// Creates a new configuration with updated memory settings.
    /// </summary>
    public CrewConfiguration WithMemory(MemoryConfiguration memory) =>
        this with { Memory = memory };

    /// <summary>
    /// Creates a new configuration with updated metadata.
    /// </summary>
    public CrewConfiguration WithMetadata(string key, object value) =>
        this with { Metadata = Metadata.SetItem(key, value) };

    /// <summary>
    /// Validates the configuration immutably.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Crew name is required");

        if (MaxAgents <= 0)
            errors.Add("MaxAgents must be greater than 0");

        if (MaxTasks <= 0)
            errors.Add("MaxTasks must be greater than 0");

        if (ExecutionTimeout <= TimeSpan.Zero)
            errors.Add("ExecutionTimeout must be positive");

        if (Agents.Count > MaxAgents)
            errors.Add($"Agent count ({Agents.Count}) exceeds maximum ({MaxAgents})");

        if (Tasks.Count > MaxTasks)
            errors.Add($"Task count ({Tasks.Count}) exceeds maximum ({MaxTasks})");

        // Validate each agent configuration
        foreach (var (agent, index) in Agents.Select((a, i) => (a, i)))
        {
            var agentValidation = agent.Validate();
            if (!agentValidation.IsValid)
                errors.AddRange(agentValidation.Errors.Select(e => $"Agent[{index}]: {e}"));
        }

        // Validate each task configuration
        foreach (var (task, index) in Tasks.Select((t, i) => (t, i)))
        {
            var taskValidation = task.Validate();
            if (!taskValidation.IsValid)
                errors.AddRange(taskValidation.Errors.Select(e => $"Task[{index}]: {e}"));
        }

        return new ValidationResult(errors.ToImmutableList());
    }
}

/// <summary>
/// Immutable agent configuration.
/// </summary>
public sealed record AgentConfiguration
{
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the role.</summary>
    public required string Role { get; init; }
    /// <summary>Gets or sets the goal.</summary>
    public required string Goal { get; init; }
    /// <summary>Gets or sets the backstory.</summary>
    public required string Backstory { get; init; }
    /// <summary>Gets or sets the type.</summary>
    public AgentType Type { get; init; } = AgentType.Worker;
    /// <summary>
    /// Gets or sets a value indicating whether verbose.
    /// </summary>
    public bool Verbose { get; init; }
    /// <summary>
    /// Gets or sets a value indicating whether allow delegation.
    /// </summary>
    public bool AllowDelegation { get; init; }

    /// <summary>
    /// Maximum execution time for a single agent task, in seconds. Default: 300 (5 minutes).
    /// </summary>
    public int MaxExecutionTime { get; init; } = ExecutionDefaults.DefaultMaxExecutionSeconds;
    /// <summary>Gets or sets the tools.</summary>
    public ImmutableList<string> Tools { get; init; } = [];
    /// <summary>Gets or sets the llm.</summary>
    public LlmConfiguration Llm { get; init; } = LlmConfiguration.Default;
    /// <summary>Parameters.</summary>
    public ImmutableDictionary<string, object> Parameters { get; init; } = [];

    /// <summary>
    /// Creates a new configuration with additional tools.
    /// </summary>
    public AgentConfiguration WithTools(IEnumerable<string> tools) =>
        this with { Tools = tools.ToImmutableList() };

    /// <summary>
    /// Creates a new configuration with an additional tool.
    /// </summary>
    public AgentConfiguration AddTool(string tool) =>
        this with { Tools = Tools.Add(tool) };

    /// <summary>
    /// Creates a new configuration with updated LLM settings.
    /// </summary>
    public AgentConfiguration WithLlm(LlmConfiguration llm) =>
        this with { Llm = llm };

    /// <summary>
    /// Creates a new configuration with updated parameters.
    /// </summary>
    public AgentConfiguration WithParameter(string key, object value) =>
        this with { Parameters = Parameters.SetItem(key, value) };

    /// <summary>
    /// Validates the agent configuration.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Agent name is required");

        if (string.IsNullOrWhiteSpace(Role))
            errors.Add("Agent role is required");

        if (string.IsNullOrWhiteSpace(Goal))
            errors.Add("Agent goal is required");

        if (string.IsNullOrWhiteSpace(Backstory))
            errors.Add("Agent backstory is required");

        if (MaxExecutionTime <= 0)
            errors.Add("MaxExecutionTime must be positive");

        var llmValidation = Llm.Validate();
        if (!llmValidation.IsValid)
            errors.AddRange(llmValidation.Errors.Select(e => $"LLM: {e}"));

        return new ValidationResult(errors.ToImmutableList());
    }
}

/// <summary>
/// Immutable task configuration.
/// </summary>
public sealed record TaskConfiguration
{
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets or sets the expected output.</summary>
    public required string ExpectedOutput { get; init; }
    /// <summary>Gets or sets the assigned agent.</summary>
    public string? AssignedAgent { get; init; }
    /// <summary>Gets or sets the priority.</summary>
    public TaskPriority Priority { get; init; } = TaskPriority.Medium;
    /// <summary>
    /// Gets or sets a value indicating whether is async.
    /// </summary>
    public bool IsAsync { get; init; }
    /// <summary>Gets or sets the estimated duration.</summary>
    public TimeSpan EstimatedDuration { get; init; } = CrewDefaults.DefaultEstimatedTaskDuration;
    /// <summary>Gets or sets the dependencies.</summary>
    public ImmutableList<string> Dependencies { get; init; } = [];
    /// <summary>Gets or sets the required tools.</summary>
    public ImmutableList<string> RequiredTools { get; init; } = [];
    /// <summary>Context.</summary>
    public ImmutableDictionary<string, object> Context { get; init; } = [];

    /// <summary>
    /// Creates a new configuration with dependencies.
    /// </summary>
    public TaskConfiguration WithDependencies(IEnumerable<string> dependencies) =>
        this with { Dependencies = dependencies.ToImmutableList() };

    /// <summary>
    /// Creates a new configuration with an additional dependency.
    /// </summary>
    public TaskConfiguration AddDependency(string dependency) =>
        this with { Dependencies = Dependencies.Add(dependency) };

    /// <summary>
    /// Creates a new configuration with required tools.
    /// </summary>
    public TaskConfiguration WithRequiredTools(IEnumerable<string> tools) =>
        this with { RequiredTools = tools.ToImmutableList() };

    /// <summary>
    /// Creates a new configuration with additional context.
    /// </summary>
    public TaskConfiguration WithContext(string key, object value) =>
        this with { Context = Context.SetItem(key, value) };

    /// <summary>
    /// Creates a new configuration assigned to an agent.
    /// </summary>
    public TaskConfiguration AssignToAgent(string agentName) =>
        this with { AssignedAgent = agentName };

    /// <summary>
    /// Validates the task configuration.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Task name is required");

        if (string.IsNullOrWhiteSpace(Description))
            errors.Add("Task description is required");

        if (string.IsNullOrWhiteSpace(ExpectedOutput))
            errors.Add("Task expected output is required");

        if (EstimatedDuration <= TimeSpan.Zero)
            errors.Add("EstimatedDuration must be positive");

        return new ValidationResult(errors.ToImmutableList());
    }
}

/// <summary>
/// Immutable LLM configuration.
/// </summary>
public sealed record LlmConfiguration
{
    /// <summary>Gets or sets the provider.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets or sets the model.</summary>
    public required string Model { get; init; }

    /// <summary>
    /// Sampling temperature for LLM responses. Range: 0.0 (deterministic) to 2.0 (creative). Default: <see cref="LlmDefaults.DefaultTemperature"/>.
    /// </summary>
    public double Temperature { get; init; } = LlmDefaults.DefaultTemperature;

    /// <summary>
    /// Maximum number of tokens in the LLM response. Default: <see cref="LlmDefaults.DefaultMaxTokens"/>.
    /// </summary>
    public int MaxTokens { get; init; } = DefaultMaxTokens;

    /// <summary>
    /// Maximum number of retry attempts on transient LLM errors. Default: <see cref="AgentDefaults.MaxRetryLimit"/>.
    /// </summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>
    /// Maximum time to wait for a single LLM request to complete. Default: 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = HttpDefaults.DefaultHttpTimeout;
    /// <summary>Parameters.</summary>
    public ImmutableDictionary<string, object> Parameters { get; init; } = [];

    /// <summary>
    /// Gets or sets the default.
    /// </summary>
    public static LlmConfiguration Default => new()
    {
        Provider = "OpenAI",
        Model = LlmDefaults.LegacyModelName,
        Temperature = LlmDefaults.DefaultTemperature,
        MaxTokens = 1000,
        MaxRetries = AgentDefaults.MaxRetryLimit,
        Timeout = HttpDefaults.DefaultHttpTimeout
    };

    /// <summary>
    /// Creates a new configuration with updated parameters.
    /// </summary>
    public LlmConfiguration WithParameter(string key, object value) =>
        this with { Parameters = Parameters.SetItem(key, value) };

    /// <summary>
    /// Creates a new configuration with updated temperature.
    /// </summary>
    public LlmConfiguration WithTemperature(double temperature) =>
        this with { Temperature = Math.Clamp(temperature, 0.0, 2.0) };

    /// <summary>
    /// Creates a new configuration with updated max tokens.
    /// </summary>
    public LlmConfiguration WithMaxTokens(int maxTokens) =>
        this with { MaxTokens = Math.Max(1, maxTokens) };

    /// <summary>
    /// Validates the LLM configuration.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Provider))
            errors.Add("LLM provider is required");

        if (string.IsNullOrWhiteSpace(Model))
            errors.Add("LLM model is required");

        if (Temperature < 0.0 || Temperature > 2.0)
            errors.Add("Temperature must be between 0.0 and 2.0");

        if (MaxTokens <= 0)
            errors.Add("MaxTokens must be positive");

        if (MaxRetries < 0)
            errors.Add("MaxRetries cannot be negative");

        if (Timeout <= TimeSpan.Zero)
            errors.Add("Timeout must be positive");

        return new ValidationResult(errors.ToImmutableList());
    }
}

/// <summary>
/// Immutable memory configuration.
/// </summary>
public sealed record MemoryConfiguration
{
    /// <summary>Gets or sets the provider.</summary>
    public required string Provider { get; init; }
    /// <summary>
    /// Gets or sets a value indicating whether enable short term.
    /// </summary>
    public bool EnableShortTerm { get; init; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether enable long term.
    /// </summary>
    public bool EnableLongTerm { get; init; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether enable episodic.
    /// </summary>
    public bool EnableEpisodic { get; init; }

    /// <summary>
    /// Maximum number of short-term memory entries to retain. Default: 1000.
    /// </summary>
    public int MaxShortTermEntries { get; init; } = DefaultMaxShortTermEntries;

    /// <summary>
    /// Maximum number of long-term memory entries to retain. Default: 10000.
    /// </summary>
    public int MaxLongTermEntries { get; init; } = DefaultMaxLongTermEntries;

    /// <summary>
    /// How long memory entries are retained before automatic eviction. Default: 30 days.
    /// </summary>
    public TimeSpan RetentionPeriod { get; init; } = MemoryDefaults.DefaultRetentionPeriod;
    /// <summary>Provider Settings.</summary>
    public ImmutableDictionary<string, object> ProviderSettings { get; init; } = [];

    /// <summary>
    /// Gets or sets the default.
    /// </summary>
    public static MemoryConfiguration Default => new()
    {
        Provider = DefaultProvider,
        EnableShortTerm = true,
        EnableLongTerm = false,
        EnableEpisodic = false,
        MaxShortTermEntries = DefaultMaxShortTermEntries,
        MaxLongTermEntries = DefaultMaxLongTermEntries,
        RetentionPeriod = TimeSpan.FromDays(DefaultRetentionDays)
    };

    /// <summary>
    /// Creates a new configuration with provider settings.
    /// </summary>
    public MemoryConfiguration WithProviderSetting(string key, object value) =>
        this with { ProviderSettings = ProviderSettings.SetItem(key, value) };

    /// <summary>
    /// Creates a new configuration with updated retention period.
    /// </summary>
    public MemoryConfiguration WithRetentionPeriod(TimeSpan period) =>
        this with { RetentionPeriod = period };
}

/// <summary>
/// Immutable retry configuration.
/// </summary>
public sealed record RetryConfiguration
{
    /// <summary>
    /// Maximum number of retry attempts before giving up. Default: <see cref="AgentDefaults.MaxRetryLimit"/>.
    /// </summary>
    public int MaxAttempts { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>
    /// Delay before the first retry attempt. Default: 1 second.
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = ResilienceDefaults.DefaultRetryInitialDelay;

    /// <summary>
    /// Upper bound on retry delay after exponential backoff. Default: 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = ResilienceDefaults.DefaultRetryMaxDelay;

    /// <summary>
    /// Multiplier applied to the delay between successive retry attempts. Default: 2.0 (exponential backoff).
    /// </summary>
    public double BackoffMultiplier { get; init; } = RetryDefaults.DefaultBackoffMultiplier;

    /// <summary>
    /// Whether to add random jitter to retry delays to prevent thundering-herd effects. Default: true.
    /// </summary>
    public bool UseJitter { get; init; } = true;
    /// <summary>Gets or sets the retryable exceptions.</summary>
    public ImmutableList<Type> RetryableExceptions { get; init; } = [];

    /// <summary>
    /// Gets or sets the default.
    /// </summary>
    public static RetryConfiguration Default => new()
    {
        MaxAttempts = 3,
        InitialDelay = ResilienceDefaults.DefaultRetryInitialDelay,
        MaxDelay = ResilienceDefaults.DefaultRetryMaxDelay,
        BackoffMultiplier = 2.0,
        UseJitter = true
    };

    /// <summary>
    /// Creates a new configuration with retryable exceptions.
    /// </summary>
    public RetryConfiguration WithRetryableExceptions(IEnumerable<Type> exceptions) =>
        this with { RetryableExceptions = exceptions.ToImmutableList() };

    /// <summary>
    /// Creates a new configuration with an additional retryable exception.
    /// </summary>
    public RetryConfiguration AddRetryableException<T>() where T : Exception =>
        this with { RetryableExceptions = RetryableExceptions.Add(typeof(T)) };
}

/// <summary>
/// Immutable validation result.
/// </summary>
public sealed record ValidationResult(ImmutableList<string> Errors)
{
    /// <summary>
    /// Gets or sets a value indicating whether is valid.
    /// </summary>
    public bool IsValid => Errors.IsEmpty;

    /// <summary>
    /// Success.
    /// </summary>
    public static ValidationResult Success() => new([]);
    /// <summary>
    /// Failed.
    /// </summary>
    public static ValidationResult Failed(params string[] errors) => new(errors.ToImmutableList());
    /// <summary>
    /// Failed.
    /// </summary>
    public static ValidationResult Failed(IEnumerable<string> errors) => new(errors.ToImmutableList());
}

/// <summary>
/// Enumerations for configuration options.
/// </summary>
public enum ProcessType
{
    /// <summary>Sequential.</summary>
    Sequential,
    /// <summary>Parallel.</summary>
    Parallel,
    /// <summary>Hierarchical.</summary>
    Hierarchical,
    /// <summary>Consensus.</summary>
    Consensus
}

/// <summary>
/// VerbosityLevel type.
/// </summary>
public enum VerbosityLevel
{
    /// <summary>Silent.</summary>
    Silent,
    /// <summary>Normal.</summary>
    Normal,
    /// <summary>Verbose.</summary>
    Verbose,
    /// <summary>Debug.</summary>
    Debug
}

/// <summary>
/// AgentType type.
/// </summary>
public enum AgentType
{
    /// <summary>Worker.</summary>
    Worker,
    /// <summary>Manager.</summary>
    Manager,
    /// <summary>Observer.</summary>
    Observer,
    /// <summary>Human.</summary>
    Human
}

/// <summary>
/// TaskPriority type.
/// </summary>
public enum TaskPriority
{
    /// <summary>Low.</summary>
    Low,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>High.</summary>
    High,
    /// <summary>Critical.</summary>
    Critical
}
