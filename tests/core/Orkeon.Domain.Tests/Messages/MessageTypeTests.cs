using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Domain.Tests.Messages;

/// <summary>
/// Tests for MessageType following Clean Architecture principles.
/// Tests the message type enumeration for agent communication.
/// </summary>
public class MessageTypeTests
{
    #region Enum Values Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingMessageType()
    {
        // Act & Assert - Verify all expected enum values exist
        var expectedValues = new[]
        {
            MessageType.Task,
            MessageType.Question,
            MessageType.Response,
            MessageType.Status
        };

        // Verify each expected value exists
        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<MessageType>(expectedValue));
        }

        // Verify we have exactly the expected number of values
        var allValues = Enum.GetValues<MessageType>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Theory]
    [InlineData(MessageType.Task)]
    [InlineData(MessageType.Question)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Status)]
    public void ShouldBeValid_WhenUsingMessageTypeWithAllValues(MessageType messageType)
    {
        // Act & Assert
        Assert.True(Enum.IsDefined<MessageType>(messageType));
    }

    [Fact]
    public void ShouldBeTask_WhenUsingMessageTypeWithDefaultValue()
    {
        // Act
        var defaultValue = default(MessageType);

        // Assert
        Assert.Equal(MessageType.Task, defaultValue);
    }

    #endregion

    #region String Conversion Tests

    [Fact]
    public void ShouldReturnExpectedStrings_WhenCallingToStringWithAllValues()
    {
        // Act & Assert
        Assert.Equal("Task", MessageType.Task.ToString());
        Assert.Equal("Question", MessageType.Question.ToString());
        Assert.Equal("Response", MessageType.Response.ToString());
        Assert.Equal("Status", MessageType.Status.ToString());
    }

    [Theory]
    [InlineData("Task", MessageType.Task)]
    [InlineData("Question", MessageType.Question)]
    [InlineData("Response", MessageType.Response)]
    [InlineData("Status", MessageType.Status)]
    public void ShouldReturnCorrectEnumValue_WhenParsingWithValidStrings(string input, MessageType expected)
    {
        // Act
        var result = Enum.Parse<MessageType>(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("task", MessageType.Task)]
    [InlineData("QUESTION", MessageType.Question)]
    [InlineData("response", MessageType.Response)]
    [InlineData("STATUS", MessageType.Status)]
    public void ShouldReturnCorrectEnumValue_WhenParsingCaseInsensitive(string input, MessageType expected)
    {
        // Act
        var result = Enum.Parse<MessageType>(input, ignoreCase: true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("InvalidType")]
    [InlineData("")]
    [InlineData("TaskMessage")]
    [InlineData("Request")]
    public void ShouldThrowArgumentException_WhenParsingWithInvalidStrings(string invalidInput)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Enum.Parse<MessageType>(invalidInput));
    }

    [Theory]
    [InlineData("Task", true, MessageType.Task)]
    [InlineData("Question", true, MessageType.Question)]
    [InlineData("InvalidType", false, default(MessageType))]
    [InlineData("", false, default(MessageType))]
    public void ShouldReturnExpectedResults_WhenUsingTryParseWithVariousInputs(
        string input, bool expectedSuccess, MessageType expectedValue)
    {
        // Act
        var success = Enum.TryParse<MessageType>(input, out var result);

        // Assert
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedValue, result);
        }
    }

    #endregion

    #region Numeric Value Tests

    [Fact]
    public void ShouldBeConsistent_WhenUsingMessageTypeUsingNumericValues()
    {
        // Act & Assert - Verify the numeric values are as expected
        Assert.Equal(0, (int)MessageType.Task);
        Assert.Equal(1, (int)MessageType.Question);
        Assert.Equal(2, (int)MessageType.Response);
        Assert.Equal(3, (int)MessageType.Status);
    }

    [Theory]
    [InlineData(0, MessageType.Task)]
    [InlineData(1, MessageType.Question)]
    [InlineData(2, MessageType.Response)]
    [InlineData(3, MessageType.Status)]
    public void ShouldReturnCorrectEnum_WhenUsingCastFromIntWithValidValues(int value, MessageType expected)
    {
        // Act
        var result = (MessageType)value;

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public void ShouldNotThrowButNotBeDefined_WhenUsingCastFromIntWithInvalidValues(int invalidValue)
    {
        // Act
        var result = (MessageType)invalidValue;

        // Assert
        Assert.False(Enum.IsDefined<MessageType>(result));
    }

    #endregion

    #region Collection and Iteration Tests

    [Fact]
    public void ShouldReturnAllEnumValues_WhenGettingValues()
    {
        // Act
        var values = Enum.GetValues<MessageType>();

        // Assert
        Assert.Equal(4, values.Length);
        Assert.Contains(MessageType.Task, values);
        Assert.Contains(MessageType.Question, values);
        Assert.Contains(MessageType.Response, values);
        Assert.Contains(MessageType.Status, values);
    }

    [Fact]
    public void ShouldReturnAllEnumNames_WhenGettingNames()
    {
        // Act
        var names = Enum.GetNames<MessageType>();

        // Assert
        Assert.Equal(4, names.Length);
        Assert.Contains("Task", names);
        Assert.Contains("Question", names);
        Assert.Contains("Response", names);
        Assert.Contains("Status", names);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingMessageTypeInCollection()
    {
        // Arrange
        var messageTypes = new List<MessageType>
        {
            MessageType.Task,
            MessageType.Question,
            MessageType.Response,
            MessageType.Task, // Duplicate
            MessageType.Status
        };

        // Act
        var taskMessages = messageTypes.Where(t => t == MessageType.Task).ToList();
        var uniqueTypes = messageTypes.Distinct().ToList();

        // Assert
        Assert.Equal(5, messageTypes.Count);
        Assert.Equal(2, taskMessages.Count);
        Assert.Equal(4, uniqueTypes.Count);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingMessageTypeInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<MessageType>
        {
            // Act
            MessageType.Task,
            MessageType.Question,
            MessageType.Task, // Duplicate
            MessageType.Status
        };

        // Assert
        Assert.Equal(3, hashSet.Count);
        Assert.Contains(MessageType.Task, hashSet);
        Assert.Contains(MessageType.Question, hashSet);
        Assert.Contains(MessageType.Status, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingMessageTypeInDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<MessageType, string>
        {
            { MessageType.Task, "Task assignment message" },
            { MessageType.Question, "Question from agent" },
            { MessageType.Response, "Response to question" },
            { MessageType.Status, "Status update" }
        };

        // Act & Assert
        Assert.Equal(4, dictionary.Count);
        Assert.Equal("Task assignment message", dictionary[MessageType.Task]);
        Assert.Equal("Question from agent", dictionary[MessageType.Question]);
        Assert.Equal("Response to question", dictionary[MessageType.Response]);
        Assert.Equal("Status update", dictionary[MessageType.Status]);
        Assert.True(dictionary.ContainsKey(MessageType.Task));
    }

    #endregion

    #region Comparison and Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var type1 = MessageType.Task;
        var type2 = MessageType.Task;

        // Act & Assert
        Assert.True(type1.Equals(type2));
        Assert.True(type1 == type2);
        Assert.False(type1 != type2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var type1 = MessageType.Task;
        var type2 = MessageType.Question;

        // Act & Assert
        Assert.False(type1.Equals(type2));
        Assert.False(type1 == type2);
        Assert.True(type1 != type2);
    }

    [Fact]
    public void ShouldCompareByNumericValue_WhenComparing()
    {
        // Act & Assert
        Assert.True(MessageType.Task.CompareTo(MessageType.Question) < 0);
        Assert.True(MessageType.Response.CompareTo(MessageType.Task) > 0);
        Assert.Equal(0, MessageType.Status.CompareTo(MessageType.Status));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var type1 = MessageType.Task;
        var type2 = MessageType.Task;

        // Act
        var hash1 = type1.GetHashCode();
        var hash2 = type2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var type1 = MessageType.Task;
        var type2 = MessageType.Question;

        // Act
        var hash1 = type1.GetHashCode();
        var hash2 = type2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Switch Statement and Pattern Matching Tests

    [Theory]
    [InlineData(MessageType.Task, "task")]
    [InlineData(MessageType.Question, "question")]
    [InlineData(MessageType.Response, "response")]
    [InlineData(MessageType.Status, "status")]
    public void ShouldHandleCorrectly_WhenSwitchingStatementWithAllValues(MessageType messageType, string expected)
    {
        // Act
        var result = messageType switch
        {
            MessageType.Task => "task",
            MessageType.Question => "question",
            MessageType.Response => "response",
            MessageType.Status => "status",
            _ => "unknown"
        };

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldNotHaveUnhandledCases_WhenSwitchingExpressionWithAllCases()
    {
        // Arrange
        var allValues = Enum.GetValues<MessageType>();

        // Act & Assert - Verify all cases are handled
        foreach (var messageType in allValues)
        {
            var result = messageType switch
            {
                MessageType.Task => "handled",
                MessageType.Question => "handled",
                MessageType.Response => "handled",
                MessageType.Status => "handled",
                _ => "unhandled"
            };

            Assert.Equal("handled", result);
        }
    }

    #endregion

    #region Business Logic and Semantic Tests

    [Fact]
    public void ShouldMakeSense_WhenUsingMessageTypeUsingSemanticGrouping()
    {
        // Arrange - Group types by their semantic meaning
        var communicationTypes = new[] { MessageType.Question, MessageType.Response };
        var workflowTypes = new[] { MessageType.Task, MessageType.Status };

        // Act - Verify groupings make sense
        var allTypes = communicationTypes.Concat(workflowTypes).ToList();

        // Assert
        Assert.Equal(4, allTypes.Count);
        Assert.Equal(Enum.GetValues<MessageType>().Length, allTypes.Count);

        // Verify no duplicates in grouping
        Assert.Equal(allTypes.Count, allTypes.Distinct().Count());
    }

    [Theory]
    [InlineData(MessageType.Question, true)]
    [InlineData(MessageType.Response, true)]
    [InlineData(MessageType.Task, false)]
    [InlineData(MessageType.Status, false)]
    public void ShouldIdentifyInteractiveTypes_WhenUsingIsInteractiveMessage(MessageType messageType, bool expectedIsInteractive)
    {
        // Act - Define what constitutes an "interactive" message
        var isInteractive = messageType switch
        {
            MessageType.Question or MessageType.Response => true,
            _ => false
        };

        // Assert
        Assert.Equal(expectedIsInteractive, isInteractive);
    }

    [Theory]
    [InlineData(MessageType.Task, true)]
    [InlineData(MessageType.Status, true)]
    [InlineData(MessageType.Question, false)]
    [InlineData(MessageType.Response, false)]
    public void ShouldIdentifyWorkflowTypes_WhenUsingIsWorkflowMessage(MessageType messageType, bool expectedIsWorkflow)
    {
        // Act - Define what constitutes a "workflow" message
        var isWorkflow = messageType switch
        {
            MessageType.Task or MessageType.Status => true,
            _ => false
        };

        // Assert
        Assert.Equal(expectedIsWorkflow, isWorkflow);
    }

    [Theory]
    [InlineData(MessageType.Question, MessageType.Response)]
    [InlineData(MessageType.Task, MessageType.Status)]
    public void ShouldReturnLogicalCorrespondence_WhenUsingGetCorrespondingMessageType(MessageType input, MessageType expectedCorresponding)
    {
        // Act - Define logical correspondence between message types
        var corresponding = input switch
        {
            MessageType.Question => MessageType.Response,
            MessageType.Response => MessageType.Question,
            MessageType.Task => MessageType.Status,
            MessageType.Status => MessageType.Task,
            _ => input
        };

        // Assert
        Assert.Equal(expectedCorresponding, corresponding);
    }

    #endregion

    #region Edge Cases and Invalid Operations

    [Fact]
    public void ShouldStillHaveValue_WhenUsingMessageTypeWithInvalidCast()
    {
        // Act
        var invalidEnum = (MessageType)999;

        // Assert
        Assert.Equal(999, (int)invalidEnum);
        Assert.False(Enum.IsDefined<MessageType>(invalidEnum));

        // ToString should still work
        Assert.Equal("999", invalidEnum.ToString());
    }

    [Fact]
    public void ShouldBeUniqueIntegers_WhenUsingMessageTypeWithAllValues()
    {
        // Act
        var values = Enum.GetValues<MessageType>();
        var intValues = values.Select(v => (int)v).ToList();

        // Assert
        Assert.Equal(values.Length, intValues.Distinct().Count());

        // Should be sequential starting from 0
        var expectedValues = Enumerable.Range(0, values.Length).ToList();
        Assert.Equal(expectedValues, intValues.OrderBy(x => x).ToList());
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMessageTypeWithNullableEnum()
    {
        // Arrange
        MessageType? nullableEnum = null;
        MessageType? nonNullEnum = MessageType.Task;

        // Act & Assert
        Assert.Null(nullableEnum);
        Assert.NotNull(nonNullEnum);
        Assert.Equal(MessageType.Task, nonNullEnum.Value);

        // Default value handling
        var defaultValue = nullableEnum ?? MessageType.Status;
        Assert.Equal(MessageType.Status, defaultValue);
    }

    #endregion
}
