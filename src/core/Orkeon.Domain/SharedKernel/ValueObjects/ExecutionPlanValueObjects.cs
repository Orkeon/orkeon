using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Execution plan value object for structured task planning.
/// </summary>
public sealed record ExecutionPlan : ValueObjectRecord
{
    /// <summary>Gets the plan name.</summary>
    public string Name { get; init; }
    /// <summary>Gets the ordered list of execution steps.</summary>
    public ImmutableList<ExecutionStep> Steps { get; init; }
    /// <summary>Gets the execution strategy.</summary>
    public ExecutionStrategy Strategy { get; init; }
    /// <summary>Gets the execution context parameters.</summary>
    public ExecutionContextParameters Context { get; init; }
    /// <summary>Gets the total estimated duration.</summary>
    public TimeSpan EstimatedDuration { get; init; }
    /// <summary>Gets when this plan was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Initializes a new <see cref="ExecutionPlan"/>.</summary>
    /// <param name="name">The plan name (non-empty).</param>
    /// <param name="steps">The execution steps (at least one required).</param>
    /// <param name="strategy">The execution strategy (default Sequential).</param>
    /// <param name="context">Optional execution context parameters.</param>
    private ExecutionPlan(
        string name,
        IEnumerable<ExecutionStep> steps,
        ExecutionStrategy? strategy = null,
        ExecutionContextParameters? context = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Plan name cannot be empty", nameof(name)) : name;
        Steps = steps.ToImmutableList();
        Strategy = strategy ?? ExecutionStrategy.Sequential;
        Context = context ?? ExecutionContextParameters.Empty;
        EstimatedDuration = Steps.Aggregate(TimeSpan.Zero, (total, step) => total + step.EstimatedDuration);
        CreatedAt = DateTime.UtcNow;

        if (Steps.IsEmpty)
            throw new ArgumentException("Execution plan must have at least one step", nameof(steps));
    }

    /// <summary>Creates a new <see cref="ExecutionPlan"/>.</summary>
    /// <param name="name">The plan name (non-empty).</param>
    /// <param name="steps">The execution steps (at least one required).</param>
    /// <param name="strategy">The execution strategy (default Sequential).</param>
    /// <param name="context">Optional execution context parameters.</param>
    /// <returns>A new <see cref="ExecutionPlan"/>.</returns>
    public static ExecutionPlan Create(
        string name,
        IEnumerable<ExecutionStep> steps,
        ExecutionStrategy? strategy = null,
        ExecutionContextParameters? context = null) =>
        new(name, steps, strategy, context);

    /// <summary>Gets the number of steps in the plan.</summary>
    public int StepCount => Steps.Count;
    /// <summary>Gets whether the plan can execute steps in parallel.</summary>
    public bool CanExecuteInParallel => Strategy == ExecutionStrategy.Parallel || Strategy == ExecutionStrategy.Mixed;

    /// <summary>Returns a new plan with the given step added.</summary>
    /// <param name="step">The step to add.</param>
    /// <returns>A new <see cref="ExecutionPlan"/> with the step added.</returns>
    public ExecutionPlan AddStep(ExecutionStep step)
    {
        var newSteps = Steps.Add(step);
        var newDuration = newSteps.Aggregate(TimeSpan.Zero, (total, s) => total + s.EstimatedDuration);
        return this with { Steps = newSteps, EstimatedDuration = newDuration };
    }

    /// <summary>Returns a new plan with the specified context parameter set.</summary>
    /// <param name="key">The context key.</param>
    /// <param name="value">The context value.</param>
    /// <returns>A new <see cref="ExecutionPlan"/> with the context parameter set.</returns>
    public ExecutionPlan WithContext(string key, object value) => this with { Context = Context.With(key, value) };

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Name}: {StepCount} steps, {Strategy}, ~{EstimatedDuration.TotalMinutes:F0}m");
}

/// <summary>
/// Execution step value object for granular task planning.
/// </summary>
public sealed record ExecutionStep : ValueObjectRecord
{
    /// <summary>Gets the step name.</summary>
    public string Name { get; init; }
    /// <summary>Gets the step description.</summary>
    public string Description { get; init; }
    /// <summary>Gets the optional task ID associated with this step.</summary>
    public TaskId? TaskId { get; init; }
    /// <summary>Gets the optional agent assigned to this step.</summary>
    public AgentId? AssignedAgent { get; init; }
    /// <summary>Gets the estimated duration of this step.</summary>
    public TimeSpan EstimatedDuration { get; init; }
    /// <summary>Gets the list of dependencies for this step.</summary>
    public ImmutableList<string> Dependencies { get; init; }
    /// <summary>Gets the step parameters.</summary>
    public ExecutionStepParameters Parameters { get; init; }

    /// <summary>Initializes a new <see cref="ExecutionStep"/>.</summary>
    /// <param name="name">The step name (non-empty).</param>
    /// <param name="description">The step description (non-empty).</param>
    /// <param name="estimatedDuration">The estimated duration (positive).</param>
    /// <param name="taskId">Optional associated task ID.</param>
    /// <param name="assignedAgent">Optional assigned agent ID.</param>
    /// <param name="dependencies">Optional step dependencies.</param>
    /// <param name="parameters">Optional step parameters.</param>
    private ExecutionStep(
        string name,
        string description,
        TimeSpan estimatedDuration,
        TaskId? taskId = null,
        AgentId? assignedAgent = null,
        IEnumerable<string>? dependencies = null,
        ExecutionStepParameters? parameters = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Step name cannot be empty", nameof(name)) : name;
        Description = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("Step description cannot be empty", nameof(description)) : description;
        EstimatedDuration = estimatedDuration > TimeSpan.Zero ? estimatedDuration : throw new ArgumentException("Duration must be positive", nameof(estimatedDuration));
        TaskId = taskId;
        AssignedAgent = assignedAgent;
        Dependencies = (dependencies ?? []).ToImmutableList();
        Parameters = parameters ?? ExecutionStepParameters.Empty;
    }

    /// <summary>Creates a new <see cref="ExecutionStep"/>.</summary>
    /// <param name="name">The step name (non-empty).</param>
    /// <param name="description">The step description (non-empty).</param>
    /// <param name="estimatedDuration">The estimated duration (positive).</param>
    /// <param name="taskId">Optional associated task ID.</param>
    /// <param name="assignedAgent">Optional assigned agent ID.</param>
    /// <param name="dependencies">Optional step dependencies.</param>
    /// <param name="parameters">Optional step parameters.</param>
    /// <returns>A new <see cref="ExecutionStep"/>.</returns>
    public static ExecutionStep Create(
        string name,
        string description,
        TimeSpan estimatedDuration,
        TaskId? taskId = null,
        AgentId? assignedAgent = null,
        IEnumerable<string>? dependencies = null,
        ExecutionStepParameters? parameters = null) =>
        new(name, description, estimatedDuration, taskId, assignedAgent, dependencies, parameters);

    /// <summary>Gets whether this step has dependencies.</summary>
    public bool HasDependencies => Dependencies.Count > 0;
    /// <summary>Gets whether this step has an assigned agent.</summary>
    public bool IsAssigned => AssignedAgent != null;

    /// <summary>Returns a new step assigned to the specified agent.</summary>
    /// <param name="agentId">The agent to assign.</param>
    /// <returns>A new <see cref="ExecutionStep"/> assigned to the agent.</returns>
    public ExecutionStep AssignTo(AgentId agentId) => this with { AssignedAgent = agentId };
    /// <summary>Returns a new step with the specified parameter set.</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>A new <see cref="ExecutionStep"/> with the parameter set.</returns>
    public ExecutionStep WithParameter(string key, object value) => this with { Parameters = Parameters.With(key, value) };

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} (~{EstimatedDuration.TotalMinutes:F0}m)");
}

/// <summary>Execution strategy for running steps.</summary>
public sealed record ExecutionStrategy
{
    /// <summary>Gets the string value of this execution strategy.</summary>
    public string Value { get; }
    private ExecutionStrategy(string value) => Value = value;

    /// <summary>Executes steps one after another in order.</summary>
    public static readonly ExecutionStrategy Sequential = new("Sequential");
    /// <summary>Executes all steps concurrently.</summary>
    public static readonly ExecutionStrategy Parallel = new("Parallel");
    /// <summary>Executes some steps sequentially and others in parallel.</summary>
    public static readonly ExecutionStrategy Mixed = new("Mixed");
    /// <summary>Dynamically adapts the execution order based on results.</summary>
    public static readonly ExecutionStrategy Adaptive = new("Adaptive");

    private static readonly Dictionary<string, ExecutionStrategy> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Sequential)] = Sequential,
        [nameof(Parallel)] = Parallel,
        [nameof(Mixed)] = Mixed,
        [nameof(Adaptive)] = Adaptive,
    };

    /// <summary>Gets all valid execution strategies.</summary>
    public static IReadOnlyCollection<ExecutionStrategy> All => s_all.Values;

    /// <summary>Creates an <see cref="ExecutionStrategy"/> from its string representation.</summary>
    public static ExecutionStrategy From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown ExecutionStrategy: '{value}'", nameof(value));

    /// <summary>Attempts to create an <see cref="ExecutionStrategy"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out ExecutionStrategy? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(ExecutionStrategy s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
