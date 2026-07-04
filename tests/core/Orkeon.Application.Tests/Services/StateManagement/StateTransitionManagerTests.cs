using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Crew.ValueObjects;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Services.StateManagement;

namespace Orkeon.Application.Tests.Services.StateManagement;

public class StateTransitionManagerTests
{
    private readonly StateTransitionManager _stateTransitionManager;
    private readonly TestLogger<StateTransitionManager> _logger;

    public StateTransitionManagerTests()
    {
        _logger = new TestLogger<StateTransitionManager>();
        _stateTransitionManager = new StateTransitionManager(_logger);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new StateTransitionManager(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #region Agent State Transition Tests

    [Theory]
    [InlineData("Created", AgentStateEvent.Initialize, "Idle")]
    [InlineData("Idle", AgentStateEvent.AssignTask, "Busy")]
    [InlineData("Busy", AgentStateEvent.CompleteTask, "Idle")]
    [InlineData("Busy", AgentStateEvent.Deactivate, "Unavailable")]
    [InlineData("Unavailable", AgentStateEvent.Activate, "Idle")]
    [InlineData("Idle", AgentStateEvent.Deactivate, "Deactivated")]
    [InlineData("Busy", AgentStateEvent.FailTask, "Error")]
    [InlineData("Error", AgentStateEvent.Reset, "Idle")]
    public void ShouldSucceed_WhenUsingTransitionAgentStateWithValidTransitions(
        string currentStateName, AgentStateEvent stateEvent, string expectedStateName)
    {
        var currentState = AgentStatus.From(currentStateName);
        var expectedState = AgentStatus.From(expectedStateName);
        var context = new AgentContext(IsValid: true, HasRequiredResources: true, CanAcceptTask: true, CanRetry: true, CanRecover: true);
        var result = _stateTransitionManager.TransitionAgentState(currentState, stateEvent, context);
        Assert.True(result.IsValid);
        Assert.Equal(expectedState, result.ToState);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("Deactivated", AgentStateEvent.AssignTask, "Cannot assign task to deactivated agent")]
    [InlineData("Idle", AgentStateEvent.CompleteTask, "No task to complete")]
    [InlineData("Created", AgentStateEvent.AssignTask, "Agent must be initialized first")]
    [InlineData("Busy", AgentStateEvent.Initialize, "Agent is already initialized")]
    public void ShouldFail_WhenUsingTransitionAgentStateWithInvalidTransitions(
        string currentStateName, AgentStateEvent stateEvent, string expectedReason)
    {
        var currentState = AgentStatus.From(currentStateName);
        var context = new AgentContext(IsValid: true, HasRequiredResources: true, CanAcceptTask: true, CanRetry: true, CanRecover: true);
        var result = _stateTransitionManager.TransitionAgentState(currentState, stateEvent, context);
        Assert.False(result.IsValid);
        Assert.Equal(currentState, result.ToState);
        Assert.Contains(expectedReason, result.ErrorMessage);
    }

    [Fact]
    public void ShouldFail_WhenUsingTransitionAgentStateAssigningTaskWithoutResources()
    {
        var context = new AgentContext(IsValid: true, HasRequiredResources: false, CanAcceptTask: false, CanRetry: true, CanRecover: true);
        var result = _stateTransitionManager.TransitionAgentState(AgentStatus.Idle, AgentStateEvent.AssignTask, context);
        Assert.False(result.IsValid);
        Assert.Equal(AgentStatus.Idle, result.ToState);
        Assert.Contains("Insufficient resources", result.ErrorMessage);
    }

    [Fact]
    public void ShouldFail_WhenUsingTransitionAgentStateRecoveringWithoutRetryCapability()
    {
        var context = new AgentContext(IsValid: true, HasRequiredResources: true, CanAcceptTask: true, CanRetry: false, CanRecover: false);
        var result = _stateTransitionManager.TransitionAgentState(AgentStatus.Error, AgentStateEvent.Reset, context);
        Assert.False(result.IsValid);
        Assert.Equal(AgentStatus.Error, result.ToState);
        Assert.Contains("Cannot retry", result.ErrorMessage);
    }

    #endregion

    #region Crew State Transition Tests

    [Theory]
    [InlineData("Created", CrewStateEvent.AddAgent, "Initializing")]
    [InlineData("Initializing", CrewStateEvent.StartExecution, "Idle")]
    [InlineData("Idle", CrewStateEvent.StartExecution, "Executing")]
    [InlineData("Executing", CrewStateEvent.PauseExecution, "Paused")]
    [InlineData("Paused", CrewStateEvent.ResumeExecution, "Executing")]
    [InlineData("Executing", CrewStateEvent.CompleteExecution, "Completed")]
    [InlineData("Executing", CrewStateEvent.ErrorOccurred, "Failed")]
    [InlineData("Executing", CrewStateEvent.Dissolve, "Cancelled")]
    [InlineData("Failed", CrewStateEvent.Reset, "Idle")]
    public void ShouldSucceed_WhenUsingTransitionCrewStateWithValidTransitions(
        string currentStateName, CrewStateEvent stateEvent, string expectedStateName)
    {
        var currentState = CrewStatus.From(currentStateName);
        var expectedState = CrewStatus.From(expectedStateName);
        var context = new CrewContext(HasMinimumAgents: true, AllAgentsReady: true, CanResume: true, CanRecover: true, CanReset: true, AgentCount: 3, ReadyAgentCount: 1);
        var result = _stateTransitionManager.TransitionCrewState(currentState, stateEvent, context);
        Assert.True(result.IsValid);
        Assert.Equal(expectedState, result.ToState);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData("Completed", CrewStateEvent.StartExecution, "Crew has already completed")]
    [InlineData("Cancelled", CrewStateEvent.ResumeExecution, "Cannot resume cancelled crew")]
    [InlineData("Idle", CrewStateEvent.CompleteExecution, "No execution to complete")]
    [InlineData("Created", CrewStateEvent.StartExecution, "Crew must be initialized first")]
    public void ShouldFail_WhenUsingTransitionCrewStateWithInvalidTransitions(
        string currentStateName, CrewStateEvent stateEvent, string expectedReason)
    {
        var currentState = CrewStatus.From(currentStateName);
        var context = new CrewContext(HasMinimumAgents: true, AllAgentsReady: true, CanResume: true, CanRecover: true, CanReset: true, AgentCount: 3, ReadyAgentCount: 1);
        var result = _stateTransitionManager.TransitionCrewState(currentState, stateEvent, context);
        Assert.False(result.IsValid);
        Assert.Equal(currentState, result.ToState);
        Assert.Contains(expectedReason, result.ErrorMessage);
    }

    [Fact]
    public void ShouldFail_WhenUsingTransitionCrewStateStartingWithNoAvailableAgents()
    {
        var context = new CrewContext(HasMinimumAgents: false, AllAgentsReady: false, CanResume: false, CanRecover: true, CanReset: true, AgentCount: 0, ReadyAgentCount: 0);
        var result = _stateTransitionManager.TransitionCrewState(CrewStatus.Idle, CrewStateEvent.StartExecution, context);
        Assert.False(result.IsValid);
        Assert.Equal(CrewStatus.Idle, result.ToState);
        Assert.Contains("No available agents", result.ErrorMessage);
    }

    #endregion

    #region GetAllowedNextStates Tests

    [Fact]
    public void ShouldReturnCorrectStates_WhenGettingAllowedAgentNextStates()
    {
        Assert.Single(StateTransitionManager.GetAllowedNextStates(AgentStatus.Created));
        Assert.Equal(3, StateTransitionManager.GetAllowedNextStates(AgentStatus.Idle).Count);
        Assert.Equal(3, StateTransitionManager.GetAllowedNextStates(AgentStatus.Busy).Count);
        Assert.Empty(StateTransitionManager.GetAllowedNextStates(AgentStatus.Deactivated));
    }

    [Fact]
    public void ShouldReturnCorrectStates_WhenGettingAllowedCrewNextStates()
    {
        Assert.Single(StateTransitionManager.GetAllowedNextStates(CrewStatus.Created));
        Assert.Single(StateTransitionManager.GetAllowedNextStates(CrewStatus.Idle));
        Assert.Equal(4, StateTransitionManager.GetAllowedNextStates(CrewStatus.Executing).Count);
        Assert.Empty(StateTransitionManager.GetAllowedNextStates(CrewStatus.Completed));
        Assert.Empty(StateTransitionManager.GetAllowedNextStates(CrewStatus.Cancelled));
    }

    #endregion

    #region Logging Tests

    [Fact]
    public void ShouldLogInformation_WhenUsingSuccessfulTransition()
    {
        var context = new AgentContext(IsValid: true, HasRequiredResources: true, CanAcceptTask: true, CanRetry: true, CanRecover: true);
        _stateTransitionManager.TransitionAgentState(AgentStatus.Idle, AgentStateEvent.AssignTask, context);
        Assert.Contains(_logger.LoggedMessages, m => m.LogLevel == LogLevel.Information && m.Message.Contains("Agent state transition"));
    }

    [Fact]
    public void ShouldLogWarning_WhenUsingFailedTransition()
    {
        var context = new AgentContext(IsValid: true, HasRequiredResources: true, CanAcceptTask: true, CanRetry: true, CanRecover: true);
        _stateTransitionManager.TransitionAgentState(AgentStatus.Deactivated, AgentStateEvent.AssignTask, context);
        Assert.Contains(_logger.LoggedMessages, m => m.LogLevel == LogLevel.Warning && m.Message.Contains("Invalid agent state transition"));
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldMaintainValidState_WhenUsingMultipleSequentialTransitions()
    {
        var context = new AgentContext(IsValid: true, HasRequiredResources: true, CanAcceptTask: true, CanRetry: true, CanRecover: true);
        var state = AgentStatus.Created;
        var r1 = _stateTransitionManager.TransitionAgentState(state, AgentStateEvent.Initialize, context);
        Assert.True(r1.IsValid); state = r1.ToState;
        var r2 = _stateTransitionManager.TransitionAgentState(state, AgentStateEvent.AssignTask, context);
        Assert.True(r2.IsValid); state = r2.ToState;
        var r3 = _stateTransitionManager.TransitionAgentState(state, AgentStateEvent.CompleteTask, context);
        Assert.True(r3.IsValid); state = r3.ToState;
        var r4 = _stateTransitionManager.TransitionAgentState(state, AgentStateEvent.Deactivate, context);
        Assert.True(r4.IsValid);
        Assert.Equal(AgentStatus.Deactivated, r4.ToState);
    }

    #endregion
}

// Test double for ILogger
internal class TestLogger<T> : ILogger<T>
{
    public List<LoggedMessage> LoggedMessages { get; } = [];
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new TestDisposable();
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        LoggedMessages.Add(new LoggedMessage { LogLevel = logLevel, Message = formatter(state, exception), Exception = exception });
    }
    internal class LoggedMessage
    {
        public LogLevel LogLevel { get; init; }
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
    }
    private class TestDisposable : IDisposable { public void Dispose() { } }
}
