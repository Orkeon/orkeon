using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput.Events;
using Orkeon.Domain.HumanInput.ValueObjects;
using Orkeon.Domain.SharedKernel.Events;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Events;

/// <summary>
/// Tests for HumanInputRequestedEvent following Clean Architecture principles.
/// Tests the business rules and validation logic of the HumanInputRequestedEvent.
/// </summary>
public class HumanInputRequestedEventTests
{
    [Fact]
    public void ShouldCreateEvent_WhenConstructingWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Please provide your feedback on the analysis";
        var inputType = "text";
        var timeout = TimeoutStandard;
        var context = HumanInputEventContext.Create(
            analysisType: "market_research",
            priority: "high");

        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = agentId,
            TaskId = taskId,
            Prompt = prompt,
            InputType = inputType,
            Timeout = timeout,
            Context = context
        };

        // Assert
        Assert.Equal(agentId, humanInputEvent.AgentId);
        Assert.Equal(taskId, humanInputEvent.TaskId);
        Assert.Equal(prompt, humanInputEvent.Prompt);
        Assert.Equal(inputType, humanInputEvent.InputType);
        Assert.Equal(timeout, humanInputEvent.Timeout);
        Assert.Equal(context, humanInputEvent.Context);
        Assert.Equal("HumanInputRequestedEvent", humanInputEvent.EventName);
        Assert.NotEqual(Guid.Empty, humanInputEvent.Id);
    }

    [Fact]
    public void ShouldCreateEventWithDefaults_WhenConstructingWithMinimalParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Please provide input";

        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = agentId,
            TaskId = taskId,
            Prompt = prompt
        };

        // Assert
        Assert.Equal(agentId, humanInputEvent.AgentId);
        Assert.Equal(taskId, humanInputEvent.TaskId);
        Assert.Equal(prompt, humanInputEvent.Prompt);
        Assert.Equal("text", humanInputEvent.InputType); // Default value
        Assert.Null(humanInputEvent.Timeout); // Default null
        Assert.NotNull(humanInputEvent.Context);
        Assert.Equal(HumanInputEventContext.Empty, humanInputEvent.Context);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("number")]
    [InlineData("boolean")]
    [InlineData("choice")]
    [InlineData("file")]
    [InlineData("")]
    [InlineData("custom-input-type")]
    public void ShouldAcceptAll_WhenConstructingWithVariousInputTypes(string inputType)
    {
        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt",
            InputType = inputType
        };

        // Assert
        Assert.Equal(inputType, humanInputEvent.InputType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple prompt")]
    [InlineData("Please provide detailed feedback on the following analysis:\n\n1. Market trends\n2. Customer preferences\n3. Competitive landscape")]
    [InlineData("Prompt with special characters: !@#$%^&*(){}[]|\\:;\"'<>,.?/~`")]
    public void ShouldAcceptAll_WhenConstructingWithVariousPrompts(string prompt)
    {
        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = prompt
        };

        // Assert
        Assert.Equal(prompt, humanInputEvent.Prompt);
    }

    [Fact]
    public void ShouldAcceptAll_WhenConstructingWithVariousTimeouts()
    {
        // Arrange
        var timeouts = new TimeSpan?[]
        {
            null,
            TimeSpan.Zero,
            TimeoutQuick,
            TimeoutStandard,
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(1),
            TimeSpan.MaxValue,
            TimeSpan.FromMilliseconds(-1)
        };

        foreach (var timeout in timeouts)
        {
            // Act
            var humanInputEvent = new HumanInputRequestedEvent
            {
                AgentId = AgentId.Create(),
                TaskId = TaskId.Create(),
                Prompt = "prompt",
                Timeout = timeout
            };

            // Assert
            Assert.Equal(timeout, humanInputEvent.Timeout);
        }
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenConstructingWithStructuredContext()
    {
        // Arrange
        var context = HumanInputEventContext.Create(
            analysisType: "market_research",
            priority: "high",
            taskDescription: "Analyze market trends",
            agentRole: "analyst",
            notes: "Urgent request");

        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt",
            Context = context
        };

        // Assert
        Assert.Equal("market_research", humanInputEvent.Context.AnalysisType);
        Assert.Equal("high", humanInputEvent.Context.Priority);
        Assert.Equal("Analyze market trends", humanInputEvent.Context.TaskDescription);
        Assert.Equal("analyst", humanInputEvent.Context.AgentRole);
        Assert.Equal("Urgent request", humanInputEvent.Context.Notes);
    }

    [Fact]
    public void ShouldGenerateUniqueIds_WhenConstructing()
    {
        // Act
        var event1 = new HumanInputRequestedEvent { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "prompt1" };
        var event2 = new HumanInputRequestedEvent { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "prompt2" };

        // Assert
        Assert.NotEqual(event1.Id, event2.Id);
        Assert.NotEqual(Guid.Empty, event1.Id);
        Assert.NotEqual(Guid.Empty, event2.Id);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = TestPrompt;
        var inputType = "text";
        var timeout = TimeoutStandard;
        var context = HumanInputEventContext.Create(analysisType: "test");

        var event1 = new HumanInputRequestedEvent { AgentId = agentId, TaskId = taskId, Prompt = prompt, InputType = inputType, Timeout = timeout, Context = context };
        var event2 = new HumanInputRequestedEvent { AgentId = agentId, TaskId = taskId, Prompt = prompt, InputType = inputType, Timeout = timeout, Context = context };

        // Act & Assert
        Assert.NotEqual(event1, event2); // Different due to ID and timestamp
        Assert.Equal(event1.AgentId, event2.AgentId);
        Assert.Equal(event1.TaskId, event2.TaskId);
        Assert.Equal(event1.Prompt, event2.Prompt);
        Assert.Equal(event1.InputType, event2.InputType);
        Assert.Equal(event1.Timeout, event2.Timeout);
        Assert.Equal(event1.Context, event2.Context);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var context = HumanInputEventContext.Create(analysisType: "test");
        var event1 = new HumanInputRequestedEvent { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "prompt", InputType = "text", Timeout = TimeSpan.FromMinutes(1), Context = context };
        var event2 = new HumanInputRequestedEvent { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "prompt", InputType = "text", Timeout = TimeSpan.FromMinutes(1), Context = context };

        // Act
        var hash1 = event1.GetHashCode();
        var hash2 = event2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingContext()
    {
        // Arrange
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt"
        };

        // Assert - Context is an immutable record
        Assert.NotNull(humanInputEvent.Context);
        Assert.Equal(HumanInputEventContext.Empty, humanInputEvent.Context);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingHumanInputRequestedEventInCollection()
    {
        // Arrange
        var events = new List<HumanInputRequestedEvent>
        {
            new() { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "Prompt 1", InputType = "text" },
            new() { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "Prompt 2", InputType = "number" },
            new() { AgentId = AgentId.Create(), TaskId = TaskId.Create(), Prompt = "Prompt 3", InputType = "choice" }
        };

        // Act
        var textInputEvents = events.Where(e => e.InputType == "text").ToList();

        // Assert
        Assert.Single(textInputEvents);
        Assert.All(events, e => Assert.Equal("HumanInputRequestedEvent", e.EventName));
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingHumanInputRequestedEventUsingAsBaseType()
    {
        // Arrange
        DomainEvent humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = TestPrompt
        };

        // Act & Assert
        Assert.Equal("HumanInputRequestedEvent", humanInputEvent.EventName);
        Assert.Equal(1, humanInputEvent.Version);
        Assert.IsType<HumanInputRequestedEvent>(humanInputEvent);
    }

    [Fact]
    public void ShouldUseEmptyContext_WhenConstructingWithEmptyContext()
    {
        // Arrange
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt",
            Context = HumanInputEventContext.Empty
        };

        // Assert
        Assert.NotNull(humanInputEvent.Context);
        Assert.Equal(HumanInputEventContext.Empty, humanInputEvent.Context);
    }

    [Fact]
    public void ShouldIncludeEventName_WhenUsingHumanInputRequestedEventToString()
    {
        // Arrange
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt"
        };

        // Act
        var stringRepresentation = humanInputEvent.ToString();

        // Assert
        Assert.Contains("HumanInputRequestedEvent", stringRepresentation);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingHumanInputRequestedEventWithLongPrompt()
    {
        // Arrange
        var longPrompt = new string('x', 10000);

        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = longPrompt
        };

        // Assert
        Assert.Equal(10000, humanInputEvent.Prompt.Length);
        Assert.Equal(longPrompt, humanInputEvent.Prompt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingHumanInputRequestedEventWithMaxTimeout()
    {
        // Act
        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt",
            Timeout = TimeSpan.MaxValue
        };

        // Assert
        Assert.Equal(TimeSpan.MaxValue, humanInputEvent.Timeout);
    }

    [Fact]
    public void ShouldCreateContextWithFactory_WhenUsingHumanInputEventContext()
    {
        // Act
        var context = HumanInputEventContext.Create(
            analysisType: "sentiment",
            priority: "low");

        var humanInputEvent = new HumanInputRequestedEvent
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = "prompt",
            Context = context
        };

        // Assert
        Assert.Equal("sentiment", humanInputEvent.Context.AnalysisType);
        Assert.Equal("low", humanInputEvent.Context.Priority);
        Assert.Null(humanInputEvent.Context.TaskDescription);
        Assert.Null(humanInputEvent.Context.AgentRole);
        Assert.Null(humanInputEvent.Context.Notes);
    }
}
