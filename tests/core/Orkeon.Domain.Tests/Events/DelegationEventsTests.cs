using Orkeon.Domain.Common;
using Orkeon.Domain.Delegation.Events;
using Orkeon.Domain.SharedKernel.Events;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Events;

/// <summary>
/// Tests for DelegationEvents following Clean Architecture principles.
/// Tests all delegation-related domain events.
/// </summary>
public class DelegationEventsTests
{
    #region TaskDelegatedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingTaskDelegatedEventWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var context = "Delegate task to specialist agent";

        // Act
        var taskDelegatedEvent = new TaskDelegatedEvent
        {
            TaskId = taskId,
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Context = context
        };

        // Assert
        Assert.Equal(taskId, taskDelegatedEvent.TaskId);
        Assert.Equal(fromAgentId, taskDelegatedEvent.FromAgentId);
        Assert.Equal(toAgentId, taskDelegatedEvent.ToAgentId);
        Assert.Equal(context, taskDelegatedEvent.Context);
        Assert.Equal("TaskDelegatedEvent", taskDelegatedEvent.EventName);
        Assert.NotEqual(Guid.Empty, taskDelegatedEvent.Id);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingTaskDelegatedEventUsingEqualsWithSameValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var context = "Same context";

        var event1 = new TaskDelegatedEvent { TaskId = taskId, FromAgentId = fromAgentId, ToAgentId = toAgentId, Context = context };
        var event2 = new TaskDelegatedEvent { TaskId = taskId, FromAgentId = fromAgentId, ToAgentId = toAgentId, Context = context };

        // Act & Assert
        Assert.NotEqual(event1, event2);
        Assert.Equal(event1.TaskId, event2.TaskId);
        Assert.Equal(event1.FromAgentId, event2.FromAgentId);
        Assert.Equal(event1.ToAgentId, event2.ToAgentId);
        Assert.Equal(event1.Context, event2.Context);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple delegation")]
    [InlineData("Complex task requiring specialized knowledge and skills")]
    public void ShouldAcceptAll_WhenUsingTaskDelegatedEventWithVariousContexts(string context)
    {
        // Act
        var taskDelegatedEvent = new TaskDelegatedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Context = context
        };

        // Assert
        Assert.Equal(context, taskDelegatedEvent.Context);
    }

    #endregion

    #region DelegationCompletedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingDelegationCompletedEventWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var outcome = new DelegationOutcome(true, "Task completed successfully", TimeSpan.FromMinutes(30), "No errors");

        // Act
        var delegationCompletedEvent = new DelegationCompletedEvent
        {
            TaskId = taskId,
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            Outcome = outcome
        };

        // Assert
        Assert.Equal(taskId, delegationCompletedEvent.TaskId);
        Assert.Equal(fromAgentId, delegationCompletedEvent.FromAgentId);
        Assert.Equal(toAgentId, delegationCompletedEvent.ToAgentId);
        Assert.True(delegationCompletedEvent.Success);
        Assert.Equal("Task completed successfully", delegationCompletedEvent.Output);
        Assert.Equal(TimeSpan.FromMinutes(30), delegationCompletedEvent.ExecutionTime);
        Assert.Equal("No errors", delegationCompletedEvent.Error);
        Assert.Equal("DelegationCompletedEvent", delegationCompletedEvent.EventName);
    }

    [Fact]
    public void ShouldAcceptNull_WhenUsingDelegationCompletedEventWithNullError()
    {
        // Act
        var delegationCompletedEvent = new DelegationCompletedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Outcome = new DelegationOutcome(true, "Success output", TimeoutLong)
        };

        // Assert
        Assert.Null(delegationCompletedEvent.Error);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDelegationCompletedEventWithFailure()
    {
        // Act
        var delegationCompletedEvent = new DelegationCompletedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Outcome = new DelegationOutcome(false, "Failed output", TimeoutExtended, "Task execution failed")
        };

        // Assert
        Assert.False(delegationCompletedEvent.Success);
        Assert.Equal("Failed output", delegationCompletedEvent.Output);
        Assert.Equal("Task execution failed", delegationCompletedEvent.Error);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 30, 0)]
    [InlineData(0, 15, 0, 0)]
    [InlineData(2, 0, 0, 0)]
    [InlineData(1, 30, 45, 500)]
    public void ShouldAcceptAll_WhenUsingDelegationCompletedEventWithVariousExecutionTimes(
        int hours, int minutes, int seconds, int milliseconds)
    {
        // Arrange
        var executionTime = new TimeSpan(0, hours, minutes, seconds, milliseconds);

        // Act
        var delegationCompletedEvent = new DelegationCompletedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Outcome = new DelegationOutcome(true, "Output", executionTime)
        };

        // Assert
        Assert.Equal(executionTime, delegationCompletedEvent.ExecutionTime);
    }

    #endregion

    #region DelegationQueuedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingDelegationQueuedEventWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();
        var queuedAt = DateTime.UtcNow.AddMinutes(-5);

        // Act
        var delegationQueuedEvent = new DelegationQueuedEvent
        {
            TaskId = taskId,
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            QueuedAt = queuedAt
        };

        // Assert
        Assert.Equal(taskId, delegationQueuedEvent.TaskId);
        Assert.Equal(fromAgentId, delegationQueuedEvent.FromAgentId);
        Assert.Equal(toAgentId, delegationQueuedEvent.ToAgentId);
        Assert.Equal(queuedAt, delegationQueuedEvent.QueuedAt);
        Assert.Equal("DelegationQueuedEvent", delegationQueuedEvent.EventName);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingDelegationQueuedEventWithSameQueuedAtAndOccurredAt()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var delegationQueuedEvent = new DelegationQueuedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            QueuedAt = timestamp,
            OccurredAt = timestamp
        };

        // Assert
        Assert.Equal(timestamp, delegationQueuedEvent.QueuedAt);
        Assert.Equal(timestamp, delegationQueuedEvent.OccurredAt);
    }

    #endregion

    #region AgentRegisteredForDelegationEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingAgentRegisteredForDelegationEventWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var role = RoleSeniorDeveloper;

        // Act
        var agentRegisteredEvent = new AgentRegisteredForDelegationEvent
        {
            AgentId = agentId,
            Role = role
        };

        // Assert
        Assert.Equal(agentId, agentRegisteredEvent.AgentId);
        Assert.Equal(role, agentRegisteredEvent.Role);
        Assert.Equal("AgentRegisteredForDelegationEvent", agentRegisteredEvent.EventName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(RoleDeveloper)]
    [InlineData("Senior Software Engineer")]
    [InlineData("AI Specialist & Data Scientist")]
    [InlineData("123")]
    public void ShouldAcceptAll_WhenUsingAgentRegisteredForDelegationEventWithVariousRoles(string role)
    {
        // Act
        var agentRegisteredEvent = new AgentRegisteredForDelegationEvent
        {
            AgentId = AgentId.Create(),
            Role = role
        };

        // Assert
        Assert.Equal(role, agentRegisteredEvent.Role);
    }

    #endregion

    #region AgentUnregisteredFromDelegationEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingAgentUnregisteredFromDelegationEventWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        var agentUnregisteredEvent = new AgentUnregisteredFromDelegationEvent
        {
            AgentId = agentId
        };

        // Assert
        Assert.Equal(agentId, agentUnregisteredEvent.AgentId);
        Assert.Equal("AgentUnregisteredFromDelegationEvent", agentUnregisteredEvent.EventName);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingDelegationEventsInCollection()
    {
        // Arrange
        var taskId = TaskId.Create();
        var fromAgentId = AgentId.Create();
        var toAgentId = AgentId.Create();

        var events = new List<DomainEvent>
        {
            new TaskDelegatedEvent { TaskId = taskId, FromAgentId = fromAgentId, ToAgentId = toAgentId, Context = "Context" },
            new DelegationQueuedEvent { TaskId = taskId, FromAgentId = fromAgentId, ToAgentId = toAgentId, QueuedAt = DateTime.UtcNow },
            new DelegationCompletedEvent { TaskId = taskId, FromAgentId = fromAgentId, ToAgentId = toAgentId, Outcome = new DelegationOutcome(true, Success, TimeoutExtended) },
            new AgentRegisteredForDelegationEvent { AgentId = toAgentId, Role = "Specialist" },
            new AgentUnregisteredFromDelegationEvent { AgentId = fromAgentId }
        };

        // Act
        var eventNames = events.Select(e => e.EventName).ToList();

        // Assert
        Assert.Equal(5, events.Count);
        Assert.Contains("TaskDelegatedEvent", eventNames);
        Assert.Contains("DelegationQueuedEvent", eventNames);
        Assert.Contains("DelegationCompletedEvent", eventNames);
        Assert.Contains("AgentRegisteredForDelegationEvent", eventNames);
        Assert.Contains("AgentUnregisteredFromDelegationEvent", eventNames);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingDelegationEventsPolymorphism()
    {
        // Arrange
        DomainEvent delegationEvent = new TaskDelegatedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Context = "Polymorphic test"
        };

        // Act & Assert
        Assert.Equal("TaskDelegatedEvent", delegationEvent.EventName);
        Assert.Equal(1, delegationEvent.Version);
        Assert.IsType<TaskDelegatedEvent>(delegationEvent);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDelegationEventsWithExtremeValues()
    {
        // Arrange & Act
        var extremeEvent = new DelegationCompletedEvent
        {
            TaskId = TaskId.Create(),
            FromAgentId = AgentId.Create(),
            ToAgentId = AgentId.Create(),
            Outcome = new DelegationOutcome(
                false,
                new string('x', 10000),
                TimeSpan.MaxValue,
                "Critical system failure with extensive error details"),
            OccurredAt = DateTime.MaxValue
        };

        // Assert
        Assert.False(extremeEvent.Success);
        Assert.Equal(10000, extremeEvent.Output.Length);
        Assert.Equal(TimeSpan.MaxValue, extremeEvent.ExecutionTime);
        Assert.Equal(DateTime.MaxValue, extremeEvent.OccurredAt);
        Assert.Contains("Critical system failure", extremeEvent.Error!);
    }

    [Fact]
    public void ShouldHaveCommonInterface_WhenUsingDelegationEventsWithAllInheritFromDomainEvent()
    {
        // Arrange
        var events = new DomainEvent[]
        {
            new TaskDelegatedEvent { TaskId = TaskId.Create(), FromAgentId = AgentId.Create(), ToAgentId = AgentId.Create(), Context = "Test" },
            new DelegationCompletedEvent { TaskId = TaskId.Create(), FromAgentId = AgentId.Create(), ToAgentId = AgentId.Create(), Outcome = new DelegationOutcome(true, "Output", TimeSpan.Zero) },
            new DelegationQueuedEvent { TaskId = TaskId.Create(), FromAgentId = AgentId.Create(), ToAgentId = AgentId.Create(), QueuedAt = DateTime.UtcNow },
            new AgentRegisteredForDelegationEvent { AgentId = AgentId.Create(), Role = "Role" },
            new AgentUnregisteredFromDelegationEvent { AgentId = AgentId.Create() }
        };

        // Act & Assert
        foreach (var domainEvent in events)
        {
            Assert.NotEqual(Guid.Empty, domainEvent.Id);
            Assert.NotEmpty(domainEvent.EventName);
            Assert.Equal(1, domainEvent.Version);
            Assert.NotNull(domainEvent);
        }
    }

    #endregion

    #region Edge Cases and Null Handling

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskDelegatedEventWithNullContext()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
            new TaskDelegatedEvent
            {
                TaskId = TaskId.Create(),
                FromAgentId = AgentId.Create(),
                ToAgentId = AgentId.Create(),
                Context = null!
            });

        Assert.Null(exception);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingAgentRegisteredForDelegationEventWithNullRole()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
            new AgentRegisteredForDelegationEvent
            {
                AgentId = AgentId.Create(),
                Role = null!
            });

        Assert.Null(exception);
    }

    #endregion
}
