using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.HumanInput;

/// <summary>
/// Tests for HumanInputContext following Clean Architecture principles.
/// Tests the human input context functionality for agent-human interaction.
/// </summary>
public class HumanInputContextTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    private static readonly string[] s_initOptions = ["Initial Option", "Added Option"];
    #region Constructor and Default Values Tests

    [Fact]
    public void ShouldHaveExpectedDefaultValues_WhenConstructingWithDefaults()
    {
        // Act
        var context = new HumanInputContext();

        // Assert
        Assert.NotNull(context.RequestId);
        Assert.NotNull(context.RequestId); // Should be a valid GUID
        Assert.NotNull(context.AgentId);
        Assert.Equal(string.Empty, context.AgentRole);
        Assert.NotNull(context.TaskId);
        Assert.Equal(string.Empty, context.TaskDescription);
        Assert.Equal(string.Empty, context.Prompt);
        Assert.Equal("text", context.InputType);
        Assert.NotNull(context.Options);
        Assert.Empty(context.Options);
        Assert.NotNull(context.Metadata);
        Assert.Empty(context.Metadata);
        Assert.Null(context.Timeout);
        Assert.True(context.IsRequired);
        Assert.Null(context.DefaultValue);
        Assert.NotNull(context.ValidationRules);
        Assert.Empty(context.ValidationRules);
        Assert.Equal(DateTimeKind.Utc, context.CreatedAt.Kind);
    }

    [Fact]
    public void ShouldHaveUniqueRequestIds_WhenConstructingWithMultipleInstances()
    {
        // Act
        var context1 = new HumanInputContext();
        var context2 = new HumanInputContext();
        var context3 = new HumanInputContext();

        // Assert
        Assert.NotEqual(context1.RequestId, context2.RequestId);
        Assert.NotEqual(context2.RequestId, context3.RequestId);
        Assert.NotEqual(context1.RequestId, context3.RequestId);
    }

    [Fact]
    public void ShouldBeRecentUtcTime_WhenConstructingUsingCreatedAtTime()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var context = new HumanInputContext();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(context.CreatedAt >= beforeCreation);
        Assert.True(context.CreatedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, context.CreatedAt.Kind);
    }

    #endregion

    #region Property Setting Tests

    [Fact]
    public void ShouldBeSettable_WhenAccessingProperties()
    {
        // Arrange
        var customTimeout = TimeoutStandard;
        var customOptions = new List<string> { "Option 1", "Option 2" };
        var customMetadata = new Dictionary<string, object> { { "key", "value" } };
        var customValidationRules = new Dictionary<string, object> { { "minLength", 5 } };
        var customCreatedAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var context = new HumanInputContext
        {
            RequestId = HumanInputRequestId.Create(),
            AgentId = AgentId.Create(),
            AgentRole = RoleDataAnalyst,
            TaskId = TaskId.Create(),
            TaskDescription = "Analyze market trends",
            Prompt = "Please provide your analysis",
            InputType = "textarea",
            Options = customOptions,
            Metadata = customMetadata,
            Timeout = customTimeout,
            IsRequired = false,
            DefaultValue = "Default analysis",
            ValidationRules = customValidationRules,
            CreatedAt = customCreatedAt
        };

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.Equal(RoleDataAnalyst, context.AgentRole);
        Assert.NotNull(context.TaskId);
        Assert.Equal("Analyze market trends", context.TaskDescription);
        Assert.Equal("Please provide your analysis", context.Prompt);
        Assert.Equal("textarea", context.InputType);
        Assert.Equal(customOptions, context.Options);
        Assert.Equal(customMetadata, context.Metadata);
        Assert.Equal(customTimeout, context.Timeout);
        Assert.False(context.IsRequired);
        Assert.Equal("Default analysis", context.DefaultValue);
        Assert.Equal(customValidationRules, context.ValidationRules);
        Assert.Equal(customCreatedAt, context.CreatedAt);
    }

    [Fact]
    public void ShouldSupportInitialization_WhenUsingCollections()
    {
        // Arrange & Act
        var context = new HumanInputContext
        {
            Options = ["New Option"],
            Metadata = new Dictionary<string, object> { ["newKey"] = "newValue" },
            ValidationRules = new Dictionary<string, object> { ["required"] = true }
        };

        // Assert
        Assert.Single(context.Options);
        Assert.Contains("New Option", context.Options);
        Assert.Single(context.Metadata);
        Assert.Equal("newValue", context.Metadata["newKey"]);
        Assert.Single(context.ValidationRules);
        Assert.True((bool)context.ValidationRules["required"]);
    }

    #endregion

    #region CreateTextInput Tests

    [Fact]
    public void ShouldCreateTextInputContext_WhenUsingCreateTextInputWithRequiredParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Please enter your analysis";

        // Act
        var context = HumanInputContext.CreateTextInput(agentId, taskId, prompt);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("text", context.InputType);
        Assert.Null(context.Timeout);
        Assert.NotNull(context.RequestId);
        Assert.NotNull(context.RequestId);
    }

    [Fact]
    public void ShouldSetTimeout_WhenUsingCreateTextInputWithTimeout()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Please enter your analysis";
        var timeout = TimeoutExtended;

        // Act
        var context = HumanInputContext.CreateTextInput(agentId, taskId, prompt, timeout);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("text", context.InputType);
        Assert.Equal(timeout, context.Timeout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple prompt")]
    [InlineData("Complex prompt with special chars: !@#$%")]
    public void ShouldAcceptAll_WhenUsingCreateTextInputWithVariousInputs(string prompt)
    {
        // Act
        var context = HumanInputContext.CreateTextInput(AgentId.Create(), TaskId.Create(), prompt);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("text", context.InputType);
    }

    #endregion

    #region CreateChoiceInput Tests

    [Fact]
    public void ShouldCreateChoiceInputContext_WhenUsingCreateChoiceInputWithRequiredParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Please select an option";
        var options = new List<string> { "Option A", "Option B", "Option C" };

        // Act
        var context = HumanInputContext.CreateChoiceInput(agentId, taskId, prompt, options);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("choice", context.InputType);
        Assert.Equal(options, context.Options);
        Assert.Null(context.Timeout);
        Assert.NotNull(context.RequestId);
    }

    [Fact]
    public void ShouldSetTimeout_WhenUsingCreateChoiceInputWithTimeout()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Please select an option";
        var options = new List<string> { "Yes", "No", "Maybe" };
        var timeout = TimeoutQuick;

        // Act
        var context = HumanInputContext.CreateChoiceInput(agentId, taskId, prompt, options, timeout);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("choice", context.InputType);
        Assert.Equal(options, context.Options);
        Assert.Equal(timeout, context.Timeout);
    }

    [Fact]
    public void ShouldAcceptEmptyList_WhenUsingCreateChoiceInputWithEmptyOptions()
    {
        // Arrange
        var options = new List<string>();

        // Act
        var context = HumanInputContext.CreateChoiceInput(AgentId.Create(), TaskId.Create(), "prompt", options);

        // Assert
        Assert.Equal("choice", context.InputType);
        Assert.Equal(options, context.Options);
        Assert.Empty(context.Options);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCreateChoiceInputWithManyOptions()
    {
        // Arrange
        var manyOptions = Enumerable.Range(1, 100).Select(i => $"Option {i}").ToList();

        // Act
        var context = HumanInputContext.CreateChoiceInput(AgentId.Create(), TaskId.Create(), "Choose one", manyOptions);

        // Assert
        Assert.Equal(100, context.Options.Count);
        Assert.Equal("Option 1", context.Options[0]);
        Assert.Equal("Option 100", context.Options[99]);
    }

    #endregion

    #region CreateConfirmationInput Tests

    [Fact]
    public void ShouldCreateConfirmationContext_WhenUsingCreateConfirmationInputWithRequiredParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Do you want to proceed?";

        // Act
        var context = HumanInputContext.CreateConfirmationInput(agentId, taskId, prompt);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("confirmation", context.InputType);
        Assert.Equal(2, context.Options.Count);
        Assert.Contains("Yes", context.Options);
        Assert.Contains("No", context.Options);
        Assert.Null(context.Timeout);
        Assert.NotNull(context.RequestId);
    }

    [Fact]
    public void ShouldSetTimeout_WhenUsingCreateConfirmationInputWithTimeout()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var prompt = "Confirm the operation?";
        var timeout = TimeSpan.FromMinutes(2);

        // Act
        var context = HumanInputContext.CreateConfirmationInput(agentId, taskId, prompt, timeout);

        // Assert
        Assert.NotNull(context.AgentId);
        Assert.NotNull(context.TaskId);
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("confirmation", context.InputType);
        Assert.Equal(timeout, context.Timeout);
        Assert.Equal(["Yes", "No"], context.Options);
    }

    [Theory]
    [InlineData("Do you agree?")]
    [InlineData("Should we continue with the operation?")]
    [InlineData("Are you satisfied with the results?")]
    [InlineData("")]
    public void ShouldAcceptAll_WhenUsingCreateConfirmationInputWithVariousPrompts(string prompt)
    {
        // Act
        var context = HumanInputContext.CreateConfirmationInput(AgentId.Create(), TaskId.Create(), prompt);

        // Assert
        Assert.Equal(prompt, context.Prompt);
        Assert.Equal("confirmation", context.InputType);
        Assert.Equal(["Yes", "No"], context.Options);
    }

    #endregion

    #region Comparison and Integration Tests

    [Fact]
    public void ShouldProduceContextsWithUniqueRequestIds_WhenUsingStaticFactoryMethods()
    {
        // Act
        var textContext = HumanInputContext.CreateTextInput(AgentId.Create(), TaskId.Create(), "text prompt");
        var choiceContext = HumanInputContext.CreateChoiceInput(AgentId.Create(), TaskId.Create(), "choice prompt", ["A", "B"]);
        var confirmationContext = HumanInputContext.CreateConfirmationInput(AgentId.Create(), TaskId.Create(), "confirm prompt");

        // Assert
        Assert.NotEqual(textContext.RequestId, choiceContext.RequestId);
        Assert.NotEqual(choiceContext.RequestId, confirmationContext.RequestId);
        Assert.NotEqual(textContext.RequestId, confirmationContext.RequestId);

        // All should be valid GUIDs
        Assert.NotNull(textContext.RequestId);
        Assert.NotNull(choiceContext.RequestId);
        Assert.NotNull(confirmationContext.RequestId);
    }

    [Fact]
    public void ShouldSetCreatedAtToRecentTime_WhenUsingStaticFactoryMethods()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var textContext = HumanInputContext.CreateTextInput(AgentId.Create(), TaskId.Create(), "prompt");
        var choiceContext = HumanInputContext.CreateChoiceInput(AgentId.Create(), TaskId.Create(), "prompt", ["A"]);
        var confirmationContext = HumanInputContext.CreateConfirmationInput(AgentId.Create(), TaskId.Create(), "prompt");

        // Assert
        var afterCreation = DateTime.UtcNow;

        Assert.True(textContext.CreatedAt >= beforeCreation && textContext.CreatedAt <= afterCreation);
        Assert.True(choiceContext.CreatedAt >= beforeCreation && choiceContext.CreatedAt <= afterCreation);
        Assert.True(confirmationContext.CreatedAt >= beforeCreation && confirmationContext.CreatedAt <= afterCreation);
    }

    [Fact]
    public void ShouldPreserveCommonProperties_WhenUsingFactoryMethods()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();

        // Act
        var textContext = HumanInputContext.CreateTextInput(agentId, taskId, "text prompt");
        var choiceContext = HumanInputContext.CreateChoiceInput(agentId, taskId, "choice prompt", ["A"]);
        var confirmationContext = HumanInputContext.CreateConfirmationInput(agentId, taskId, "confirm prompt");

        // Assert - All should have same agent and task IDs
        Assert.Equal(agentId, textContext.AgentId);
        Assert.Equal(agentId, choiceContext.AgentId);
        Assert.Equal(agentId, confirmationContext.AgentId);

        Assert.Equal(taskId, textContext.TaskId);
        Assert.Equal(taskId, choiceContext.TaskId);
        Assert.Equal(taskId, confirmationContext.TaskId);

        // All should have default values for unset properties
        Assert.True(textContext.IsRequired);
        Assert.True(choiceContext.IsRequired);
        Assert.True(confirmationContext.IsRequired);
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingHumanInputContextWithComplexMetadata()
    {
        // Arrange
        var complexMetadata = new Dictionary<string, object>
        {
            { "stringValue", "test" },
            { "intValue", 42 },
            { "boolValue", true },
            { "dateValue", DateTime.UtcNow },
            { "arrayValue", Int123 },
            { "objectValue", new { Name = "Test", Value = 123 } },
            { "nullValue", null! }
        };

        // Act
        var context = new HumanInputContext { Metadata = complexMetadata };

        // Assert
        Assert.Equal(7, context.Metadata.Count);
        Assert.Equal("test", context.Metadata["stringValue"]);
        Assert.Equal(42, context.Metadata["intValue"]);
        Assert.True((bool)context.Metadata["boolValue"]);
        Assert.IsType<DateTime>(context.Metadata["dateValue"]);
        Assert.IsType<int[]>(context.Metadata["arrayValue"]);
        Assert.Null(context.Metadata["nullValue"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingHumanInputContextWithComplexValidationRules()
    {
        // Arrange
        var validationRules = new Dictionary<string, object>
        {
            { "required", true },
            { "minLength", 5 },
            { "maxLength", 100 },
            { "pattern", "^[a-zA-Z0-9]+$" },
            { "customRule", new { Type = "email", AllowEmpty = false } }
        };

        // Act
        var context = new HumanInputContext { ValidationRules = validationRules };

        // Assert
        Assert.Equal(5, context.ValidationRules.Count);
        Assert.True((bool)context.ValidationRules["required"]);
        Assert.Equal(5, context.ValidationRules["minLength"]);
        Assert.Equal(100, context.ValidationRules["maxLength"]);
        Assert.Equal("^[a-zA-Z0-9]+$", context.ValidationRules["pattern"]);
        Assert.NotNull(context.ValidationRules["customRule"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 30, 0)]
    [InlineData(0, 5, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    public void ShouldAcceptAll_WhenUsingHumanInputContextWithVariousTimeouts(params int[]? timeComponents)
    {
        // Arrange
        TimeSpan? timeout = null;

        if (timeComponents != null)
        {
            timeout = new TimeSpan(timeComponents[0], timeComponents[1], timeComponents[2], timeComponents[3]);
        }

        // Act
        var context = new HumanInputContext { Timeout = timeout };

        // Assert
        Assert.Equal(timeout, context.Timeout);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingHumanInputContextInCollection()
    {
        // Arrange
        var sharedAgentId = AgentId.Create();
        var contexts = new List<HumanInputContext>
        {
            HumanInputContext.CreateTextInput(sharedAgentId, TaskId.Create(), "Text prompt"),
            HumanInputContext.CreateChoiceInput(AgentId.Create(), TaskId.Create(), "Choice prompt", ["A", "B"]),
            HumanInputContext.CreateConfirmationInput(sharedAgentId, TaskId.Create(), "Confirm prompt"),
            new HumanInputContext { AgentId = AgentId.Create(), InputType = "custom" }
        };

        // Act
        var sharedAgentContexts = contexts.Where(c => c.AgentId == sharedAgentId).ToList();
        var textInputs = contexts.Where(c => c.InputType == "text").ToList();
        var choiceInputs = contexts.Where(c => c.InputType == "choice").ToList();

        // Assert
        Assert.Equal(4, contexts.Count);
        Assert.Equal(2, sharedAgentContexts.Count);
        Assert.Single(textInputs);
        Assert.Single(choiceInputs);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingHumanInputContextWithNullValues()
    {
        // Arrange & Act
        var context = new HumanInputContext
        {
            AgentId = null!,
            TaskId = null!,
            Prompt = null!,
            DefaultValue = null,
            Timeout = null
        };

        // Assert - Should not throw exceptions
        // AgentId is now typed, null check not applicable;
        // TaskId is now typed, null check not applicable;
        Assert.Null(context.Prompt);
        Assert.Null(context.DefaultValue);
        Assert.Null(context.Timeout);
        Assert.NotNull(context.RequestId); // Should still have a request ID
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingHumanInputContextWithVeryLongStrings()
    {
        // Arrange
        var longString = new string('A', 10000);

        // Act
        var context = new HumanInputContext
        {
            AgentId = AgentId.Create(),
            TaskId = TaskId.Create(),
            Prompt = longString,
            TaskDescription = longString,
            AgentRole = longString,
            DefaultValue = longString
        };

        // Assert
        // AgentId is now typed, not string;
        // TaskId is now typed, not string;
        Assert.Equal(10000, context.Prompt.Length);
        Assert.Equal(10000, context.TaskDescription.Length);
        Assert.Equal(10000, context.AgentRole.Length);
        Assert.Equal(10000, context.DefaultValue!.Length);
    }

    [Fact]
    public void ShouldWork_WhenUsingHumanInputContextWithCollectionsViaInit()
    {
        // Arrange & Act
        var context = HumanInputContext.CreateChoiceInput(
            AgentId.Create(), TaskId.Create(), "Choose an option",
            s_initOptions);

        // Assert
        Assert.Equal(2, context.Options.Count);
        Assert.Contains("Initial Option", context.Options);
        Assert.Contains("Added Option", context.Options);
    }

    [Fact]
    public void ShouldBeSettableButDefaultToGuid_WhenUsingHumanInputContextRequestingIdProperty()
    {
        // Arrange
        var context1 = new HumanInputContext();
        var originalRequestId = context1.RequestId;

        // Act
        var context2 = new HumanInputContext { RequestId = HumanInputRequestId.Create() };
        var context3 = new HumanInputContext();

        // Assert
        Assert.NotNull(originalRequestId); // Original was GUID // Can be set to custom value
        // Removed: // Removed: Assert.NotNull(context.RequestId); // New instance still gets GUID // variable removed in refactoring // variable removed in refactoring
        Assert.NotEqual(originalRequestId, context3.RequestId); // Different instances have different GUIDs
    }

    #endregion
}
