using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Strongly typed context for agent execution steps.
/// </summary>
public sealed record AgentStepContext : ValueObjectRecord
{
    /// <summary>The agent's thought at this step.</summary>
    public string Thought { get; }
    /// <summary>The action taken (e.g. "tool", "think", "delegate", "final_answer").</summary>
    public string Action { get; }
    /// <summary>The input to the action.</summary>
    public string ActionInput { get; }
    /// <summary>The observation resulting from the action.</summary>
    public string Observation { get; }
    /// <summary>The tool call made, or <see langword="null"/> if no tool was called.</summary>
    public ToolCall? ToolCall { get; }
    /// <summary>The step metadata.</summary>
    public StepMetadata Metadata { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentStepContext"/>.
    /// </summary>
    public AgentStepContext(
        string Thought,
        string Action,
        string ActionInput,
        string Observation,
        ToolCall? ToolCall,
        StepMetadata Metadata)
    {
        ArgumentNullException.ThrowIfNull(Thought);
        ArgumentNullException.ThrowIfNull(Action);
        ArgumentNullException.ThrowIfNull(ActionInput);
        ArgumentNullException.ThrowIfNull(Observation);
        ArgumentNullException.ThrowIfNull(Metadata);

        this.Thought = Thought;
        this.Action = Action;
        this.ActionInput = ActionInput;
        this.Observation = Observation;
        this.ToolCall = ToolCall;
        this.Metadata = Metadata;
    }

    /// <summary>
    /// Deconstruct for backward compatibility with positional record syntax.
    /// </summary>
    public void Deconstruct(
        out string thought,
        out string action,
        out string actionInput,
        out string observation,
        out ToolCall? toolCall,
        out StepMetadata metadata)
    {
        thought = Thought;
        action = Action;
        actionInput = ActionInput;
        observation = Observation;
        toolCall = ToolCall;
        metadata = Metadata;
    }

    /// <summary>
    /// Creates a new <see cref="AgentStepContext"/> with validation.
    /// </summary>
    public static AgentStepContext Create(
        string thought,
        string action,
        string actionInput,
        string observation,
        ToolCall? toolCall,
        StepMetadata metadata)
        => new(thought, action, actionInput, observation, toolCall, metadata);

    /// <summary>
    /// Creates an empty context.
    /// </summary>
    private static readonly AgentStepContext _empty = new(
        Thought: string.Empty,
        Action: string.Empty,
        ActionInput: string.Empty,
        Observation: string.Empty,
        ToolCall: null,
        Metadata: StepMetadata.Empty);
    /// <summary>Gets an empty <see cref="AgentStepContext"/>.</summary>
    public static AgentStepContext Empty => _empty;

    /// <summary>
    /// Creates a context for a thought step.
    /// </summary>
    /// <param name="thought">The thought text.</param>
    /// <param name="metadata">Optional step metadata.</param>
    /// <returns>A context representing a thought step.</returns>
    public static AgentStepContext ForThought(string thought, StepMetadata? metadata = null)
    {
        return new AgentStepContext(
            Thought: thought,
            Action: "think",
            ActionInput: string.Empty,
            Observation: string.Empty,
            ToolCall: null,
            Metadata: metadata ?? StepMetadata.Empty);
    }

    /// <summary>
    /// Creates a context for a tool execution step.
    /// </summary>
    /// <param name="thought">The thought that led to this tool call.</param>
    /// <param name="toolCall">The tool call made.</param>
    /// <param name="observation">The observation from the tool result.</param>
    /// <param name="metadata">Optional step metadata.</param>
    /// <returns>A context representing a tool execution step.</returns>
    public static AgentStepContext ForToolExecution(
        string thought,
        ToolCall toolCall,
        string observation,
        StepMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        return new AgentStepContext(
            Thought: thought,
            Action: "tool",
            ActionInput: toolCall.ToolName,
            Observation: observation,
            ToolCall: toolCall,
            Metadata: metadata ?? StepMetadata.Empty);
    }

    /// <summary>
    /// Creates a context for a delegation step.
    /// </summary>
    /// <param name="thought">The thought that led to delegation.</param>
    /// <param name="delegateToAgent">The agent being delegated to.</param>
    /// <param name="taskDescription">The task being delegated.</param>
    /// <param name="observation">The observation from the delegation result.</param>
    /// <param name="metadata">Optional step metadata.</param>
    /// <returns>A context representing a delegation step.</returns>
    public static AgentStepContext ForDelegation(
        string thought,
        string delegateToAgent,
        string taskDescription,
        string observation,
        StepMetadata? metadata = null)
    {
        return new AgentStepContext(
            Thought: thought,
            Action: "delegate",
            ActionInput: $"{delegateToAgent}: {taskDescription}",
            Observation: observation,
            ToolCall: null,
            Metadata: metadata ?? StepMetadata.Empty);
    }

    /// <summary>
    /// Creates a context for a final answer step.
    /// </summary>
    /// <param name="thought">The thought that led to the final answer.</param>
    /// <param name="answer">The final answer.</param>
    /// <param name="metadata">Optional step metadata.</param>
    /// <returns>A context representing a final answer step.</returns>
    public static AgentStepContext ForFinalAnswer(
        string thought,
        string answer,
        StepMetadata? metadata = null)
    {
        return new AgentStepContext(
            Thought: thought,
            Action: "final_answer",
            ActionInput: answer,
            Observation: string.Empty,
            ToolCall: null,
            Metadata: metadata ?? StepMetadata.Empty);
    }
}

/// <summary>
/// Metadata for an agent execution step.
/// </summary>
public sealed record StepMetadata : ValueObjectRecord
{
    /// <summary>When the step occurred.</summary>
    public DateTime Timestamp { get; }
    /// <summary>How long the step took.</summary>
    public TimeSpan Duration { get; }
    /// <summary>The number of tokens consumed.</summary>
    public int TokensUsed { get; }
    /// <summary>An optional error message if the step failed.</summary>
    public string? Error { get; }
    /// <summary>The optional model used for this step.</summary>
    public string? Model { get; }
    /// <summary>The optional temperature used for this step.</summary>
    public double? Temperature { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="StepMetadata"/>.
    /// </summary>
    public StepMetadata(
        DateTime Timestamp,
        TimeSpan Duration,
        int TokensUsed,
        string? Error = null,
        string? Model = null,
        double? Temperature = null)
    {
        if (Duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Duration), "Duration cannot be negative.");
        if (TokensUsed < 0)
            throw new ArgumentOutOfRangeException(nameof(TokensUsed), "TokensUsed cannot be negative.");

        this.Timestamp = Timestamp;
        this.Duration = Duration;
        this.TokensUsed = TokensUsed;
        this.Error = Error;
        this.Model = Model;
        this.Temperature = Temperature;
    }

    /// <summary>
    /// Deconstruct for backward compatibility with positional record syntax.
    /// </summary>
    public void Deconstruct(
        out DateTime timestamp,
        out TimeSpan duration,
        out int tokensUsed,
        out string? error,
        out string? model,
        out double? temperature)
    {
        timestamp = Timestamp;
        duration = Duration;
        tokensUsed = TokensUsed;
        error = Error;
        model = Model;
        temperature = Temperature;
    }

    /// <summary>
    /// Creates empty metadata.
    /// </summary>
    private static readonly StepMetadata _empty = new(
        Timestamp: DateTime.UtcNow,
        Duration: TimeSpan.Zero,
        TokensUsed: 0);
    /// <summary>Gets empty step metadata.</summary>
    public static StepMetadata Empty => _empty;
}
