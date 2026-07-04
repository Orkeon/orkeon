using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Application.Services.StateManagement;

/// <summary>
/// State transition manager using pattern matching.
/// Phase 3.2.2: Modern C# pattern matching for state management.
/// </summary>
public partial class StateTransitionManager
{
    private static readonly AgentStatus[] s_agentCreatedTransitions = [AgentStatus.Idle];
    private static readonly AgentStatus[] s_agentIdleTransitions = [AgentStatus.Busy, AgentStatus.Unavailable, AgentStatus.Deactivated];
    private static readonly AgentStatus[] s_agentBusyTransitions = [AgentStatus.Idle, AgentStatus.Unavailable, AgentStatus.Error];
    private static readonly AgentStatus[] s_agentUnavailableTransitions = [AgentStatus.Idle, AgentStatus.Deactivated];
    private static readonly AgentStatus[] s_agentErrorTransitions = [AgentStatus.Idle, AgentStatus.Deactivated];

    private static readonly CrewStatus[] s_crewCreatedTransitions = [CrewStatus.Initializing];
    private static readonly CrewStatus[] s_crewInitializingTransitions = [CrewStatus.Idle, CrewStatus.Failed];
    private static readonly CrewStatus[] s_crewIdleTransitions = [CrewStatus.Executing];
    private static readonly CrewStatus[] s_crewExecutingTransitions = [CrewStatus.Paused, CrewStatus.Completed, CrewStatus.Failed, CrewStatus.Cancelled];
    private static readonly CrewStatus[] s_crewPausedTransitions = [CrewStatus.Executing, CrewStatus.Cancelled];
    private static readonly CrewStatus[] s_crewFailedTransitions = [CrewStatus.Idle];
    private static readonly CrewStatus[] s_crewErrorTransitions = [CrewStatus.Idle, CrewStatus.Failed];

    private readonly ILogger<StateTransitionManager> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="StateTransitionManager"/>.
    /// </summary>
    public StateTransitionManager(ILogger<StateTransitionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Transitions agent state using pattern matching validation.
    /// </summary>
    public StateTransitionResult<AgentStatus> TransitionAgentState(
        AgentStatus currentState,
        AgentStateEvent stateEvent,
        AgentContext context)
    {
        // First check for invalid transitions with specific error messages
        var errorMessage = (currentState, stateEvent, context) switch
        {
            ({ Value: "Deactivated" }, AgentStateEvent.AssignTask, _)
                => "Cannot assign task to deactivated agent",
            ({ Value: "Idle" }, AgentStateEvent.CompleteTask, _)
                => "No task to complete",
            ({ Value: "Created" }, AgentStateEvent.AssignTask, _)
                => "Agent must be initialized first",
            ({ Value: "Busy" }, AgentStateEvent.Initialize, _)
                => "Agent is already initialized",
            ({ Value: "Idle" }, AgentStateEvent.AssignTask, var ctx) when !ctx.HasRequiredResources
                => "Insufficient resources",
            ({ Value: "Error" }, AgentStateEvent.Reset, var ctx) when !ctx.CanRecover
                => "Cannot retry",
            _ => null
        };

        if (errorMessage != null)
        {
            LogInvalidAgentTransition(currentState, stateEvent, errorMessage);

            return new StateTransitionResult<AgentStatus>(
                currentState,
                currentState,
                stateEvent,
                false,
                errorMessage);
        }

        var newState = (currentState, stateEvent, context) switch
        {
            // Initialization transitions
            ({ Value: "Created" }, AgentStateEvent.Initialize, var ctx) when ctx.IsValid
                => AgentStatus.Idle,

            // Activation transitions
            ({ Value: "Unavailable" }, AgentStateEvent.Activate, _)
                => AgentStatus.Idle,

            // Task assignment transitions
            ({ Value: "Idle" }, AgentStateEvent.AssignTask, var ctx) when ctx.CanAcceptTask && ctx.HasRequiredResources
                => AgentStatus.Busy,

            // Task execution transitions
            ({ Value: "Busy" }, AgentStateEvent.CompleteTask, _)
                => AgentStatus.Idle,
            ({ Value: "Busy" }, AgentStateEvent.FailTask, _)
                => AgentStatus.Error,

            // Error recovery transitions
            ({ Value: "Error" }, AgentStateEvent.Reset, var ctx) when ctx.CanRecover
                => AgentStatus.Idle,

            // Deactivation transitions
            ({ Value: "Idle" }, AgentStateEvent.Deactivate, _)
                => AgentStatus.Deactivated,
            ({ Value: "Busy" }, AgentStateEvent.Deactivate, _)
                => AgentStatus.Unavailable,

            // Invalid transitions (stay in current state)
            _ => currentState
        };

        var isValid = newState != currentState || (currentState == AgentStatus.Idle && stateEvent == AgentStateEvent.Activate);

        if (isValid && newState != currentState && _logger.IsEnabled(LogLevel.Information))
        {
            LogAgentStateTransition(currentState, newState, stateEvent);
        }

        return new StateTransitionResult<AgentStatus>(
            currentState,
            newState,
            stateEvent,
            isValid,
            null);
    }

    /// <summary>
    /// Transitions crew state using pattern matching validation.
    /// </summary>
    public StateTransitionResult<CrewStatus> TransitionCrewState(
        CrewStatus currentState,
        CrewStateEvent stateEvent,
        CrewContext context)
    {
        // First check for invalid transitions with specific error messages
        var errorMessage = (currentState, stateEvent, context) switch
        {
            ({ Value: "Completed" }, CrewStateEvent.StartExecution, _)
                => "Crew has already completed",
            ({ Value: "Cancelled" }, CrewStateEvent.ResumeExecution, _)
                => "Cannot resume cancelled crew",
            ({ Value: "Idle" }, CrewStateEvent.CompleteExecution, _)
                => "No execution to complete",
            ({ Value: "Created" }, CrewStateEvent.StartExecution, _)
                => "Crew must be initialized first",
            ({ Value: "Idle" }, CrewStateEvent.StartExecution, var ctx) when !ctx.HasMinimumAgents
                => "No available agents",
            ({ Value: "Failed" }, CrewStateEvent.Reset, var ctx) when !ctx.CanRecover
                => "Recovery not available",
            _ => null
        };

        if (errorMessage != null)
        {
            LogInvalidCrewTransition(currentState, stateEvent, errorMessage);

            return new StateTransitionResult<CrewStatus>(
                currentState,
                currentState,
                stateEvent,
                false,
                errorMessage);
        }

        var newState = (currentState, stateEvent, context) switch
        {
            // Formation transitions
            ({ Value: "Created" }, CrewStateEvent.AddAgent, _)
                => CrewStatus.Initializing,

            // Initialization transitions
            ({ Value: "Initializing" }, CrewStateEvent.StartExecution, _)
                => CrewStatus.Idle,
            ({ Value: "Idle" }, CrewStateEvent.StartExecution, _)
                => CrewStatus.Executing,

            // During execution
            ({ Value: "Executing" }, CrewStateEvent.PauseExecution, _)
                => CrewStatus.Paused,
            ({ Value: "Paused" }, CrewStateEvent.ResumeExecution, _)
                => CrewStatus.Executing,
            ({ Value: "Executing" }, CrewStateEvent.CompleteExecution, _)
                => CrewStatus.Completed,

            // Error handling
            ({ Value: "Executing" }, CrewStateEvent.ErrorOccurred, _)
                => CrewStatus.Failed,

            // Recovery
            ({ Value: "Failed" }, CrewStateEvent.Reset, _)
                => CrewStatus.Idle,

            // Cancellation (from any state except Cancelled and Completed)
            (var s, CrewStateEvent.Dissolve, _) when s != CrewStatus.Cancelled && s != CrewStatus.Completed
                => CrewStatus.Cancelled,

            // Invalid transitions (stay in current state)
            _ => currentState
        };

        var isValid = newState != currentState;

        if (isValid && _logger.IsEnabled(LogLevel.Information))
        {
            LogCrewStateTransition(currentState, newState, stateEvent);
        }

        return new StateTransitionResult<CrewStatus>(
            currentState,
            newState,
            stateEvent,
            isValid,
            null);
    }

    /// <summary>
    /// Gets allowed next agent states using pattern matching.
    /// </summary>
    public static IReadOnlyList<AgentStatus> GetAllowedNextStates(AgentStatus currentState)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        return currentState.Value switch
        {
            "Created" => s_agentCreatedTransitions,
            "Idle" => s_agentIdleTransitions,
            "Busy" => s_agentBusyTransitions,
            "Unavailable" => s_agentUnavailableTransitions,
            "Error" => s_agentErrorTransitions,
            _ => []
        };
    }

    /// <summary>
    /// Gets allowed next crew states using pattern matching.
    /// </summary>
    public static IReadOnlyList<CrewStatus> GetAllowedNextStates(CrewStatus currentState)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        return currentState.Value switch
        {
            "Created" => s_crewCreatedTransitions,
            "Initializing" => s_crewInitializingTransitions,
            "Idle" => s_crewIdleTransitions,
            "Executing" => s_crewExecutingTransitions,
            "Paused" => s_crewPausedTransitions,
            "Failed" => s_crewFailedTransitions,
            "Error" => s_crewErrorTransitions,
            _ => []
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid agent state transition attempted: {CurrentState} with event {StateEvent}: {Reason}")]
    private partial void LogInvalidAgentTransition(AgentStatus currentState, AgentStateEvent stateEvent, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent state transition: {CurrentState} -> {NewState} (Event: {StateEvent})")]
    private partial void LogAgentStateTransition(AgentStatus currentState, AgentStatus newState, AgentStateEvent stateEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid crew state transition attempted: {CurrentState} with event {StateEvent}: {Reason}")]
    private partial void LogInvalidCrewTransition(CrewStatus currentState, CrewStateEvent stateEvent, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Crew state transition: {CurrentState} -> {NewState} (Event: {StateEvent})")]
    private partial void LogCrewStateTransition(CrewStatus currentState, CrewStatus newState, CrewStateEvent stateEvent);
}

/// <summary>
/// Result of a state transition operation.
/// </summary>
public record StateTransitionResult<TState>(
    TState FromState,
    TState ToState,
    Enum Event,
    bool IsValid,
    string? ErrorMessage = null)
    where TState : notnull
{
    /// <summary>
    /// Gets or sets a value indicating whether state changed.
    /// </summary>
    public bool StateChanged => !FromState.Equals(ToState);
}

/// <summary>
/// Agent state events for transitions.
/// </summary>
public enum AgentStateEvent
{
    /// <summary>Initialize.</summary>
    Initialize,
    /// <summary>Activate.</summary>
    Activate,
    /// <summary>Deactivate.</summary>
    Deactivate,
    /// <summary>Assign Task.</summary>
    AssignTask,
    /// <summary>Start Execution.</summary>
    StartExecution,
    /// <summary>Complete Task.</summary>
    CompleteTask,
    /// <summary>Fail Task.</summary>
    FailTask,
    /// <summary>Request Collaboration.</summary>
    RequestCollaboration,
    /// <summary>End Collaboration.</summary>
    EndCollaboration,
    /// <summary>Reset.</summary>
    Reset
}

/// <summary>
/// Crew state events for transitions.
/// </summary>
public enum CrewStateEvent
{
    /// <summary>Add Agent.</summary>
    AddAgent,
    /// <summary>Remove Agent.</summary>
    RemoveAgent,
    /// <summary>Start Execution.</summary>
    StartExecution,
    /// <summary>Pause Execution.</summary>
    PauseExecution,
    /// <summary>Resume Execution.</summary>
    ResumeExecution,
    /// <summary>Complete Execution.</summary>
    CompleteExecution,
    /// <summary>Error Occurred.</summary>
    ErrorOccurred,
    /// <summary>Reset.</summary>
    Reset,
    /// <summary>Dissolve.</summary>
    Dissolve
}

/// <summary>
/// Context for agent state transitions.
/// </summary>
public record AgentContext(
    bool IsValid,
    bool HasRequiredResources,
    bool CanAcceptTask,
    bool CanRetry,
    bool CanRecover,
    int RetryCount = 0,
    TimeSpan Uptime = default);

/// <summary>
/// Context for crew state transitions.
/// </summary>
public record CrewContext(
    bool HasMinimumAgents,
    bool AllAgentsReady,
    bool CanResume,
    bool CanRecover,
    bool CanReset,
    int AgentCount = 0,
    int ReadyAgentCount = 0);
