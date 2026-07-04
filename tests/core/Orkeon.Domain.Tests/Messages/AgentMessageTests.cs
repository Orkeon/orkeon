using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.Messages;

/// <summary>
/// Tests for AgentMessage following Clean Architecture principles.
/// Tests the agent message record for inter-agent communication.
/// </summary>
public class AgentMessageTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    #region Constructor and Basic Property Tests

    [Fact]
    public void ShouldCreateValidMessage_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var messageType = MessageType.Task;
        var content = "Please complete the analysis task";

        // Act
        var message = new AgentMessage(fromAgent, toAgent, messageType, content);

        // Assert
        Assert.Equal(fromAgent, message.From);
        Assert.Equal(toAgent, message.To);
        Assert.Equal(messageType, message.Type);
        Assert.Equal(content, message.Content);
        Assert.Null(message.Metadata);
    }

    [Fact]
    public void ShouldCreateValidMessage_WhenConstructingWithAllParameters()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var messageType = MessageType.Question;
        var content = "What is the current status?";
        var metadata = new Dictionary<string, object>
        {
            { "priority", "high" },
            { "timeout", 30 },
            { "requiresResponse", true }
        };

        // Act
        var message = new AgentMessage(fromAgent, toAgent, messageType, content, metadata);

        // Assert
        Assert.Equal(fromAgent, message.From);
        Assert.Equal(toAgent, message.To);
        Assert.Equal(messageType, message.Type);
        Assert.Equal(content, message.Content);
        Assert.Equal(metadata, message.Metadata);
        Assert.Equal(3, message.Metadata!.Count);
    }

    [Fact]
    public void ShouldAcceptEmptyString_WhenConstructingWithEmptyContent()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();

        // Act
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Status, string.Empty);

        // Assert
        Assert.Equal(string.Empty, message.Content);
        Assert.Equal(MessageType.Status, message.Type);
    }

    [Fact]
    public void ShouldAcceptNull_WhenConstructingWithNullMetadata()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();

        // Act
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Response, "Response content", null);

        // Assert
        Assert.Equal("Response content", message.Content);
        Assert.Null(message.Metadata);
    }

    [Fact]
    public void ShouldAcceptEmptyDictionary_WhenConstructingWithEmptyMetadata()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var emptyMetadata = new Dictionary<string, object>();

        // Act
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Task content", emptyMetadata);

        // Assert
        Assert.Equal(emptyMetadata, message.Metadata);
        Assert.NotNull(message.Metadata);
        Assert.Empty(message.Metadata);
    }

    #endregion

    #region AgentId Specific Tests

    [Fact]
    public void ShouldAcceptSelfCommunication_WhenConstructingWithSameFromAndTo()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        var message = new AgentMessage(agentId, agentId, MessageType.Status, "Self-status update");

        // Assert
        Assert.Equal(agentId, message.From);
        Assert.Equal(agentId, message.To);
        Assert.True(message.From.Equals(message.To));
    }

    [Fact]
    public void ShouldCreateValidMessage_WhenConstructingWithDifferentAgentIds()
    {
        // Arrange
        var fromAgent = AgentId.From(Guid.NewGuid());
        var toAgent = AgentId.From(Guid.NewGuid());

        // Act
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Question, "Question content");

        // Assert
        Assert.NotEqual(fromAgent, toAgent);
        Assert.NotEqual(message.From, message.To);
        Assert.False(message.From.Equals(message.To));
    }

    [Fact]
    public void ShouldHaveValidGuidValues_WhenUsingAgentIds()
    {
        // Arrange & Act
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content");

        // Assert
        Assert.NotEqual<object>(Guid.Empty, message.From.Value);
        Assert.NotEqual<object>(Guid.Empty, message.To.Value);
        Assert.NotEqual(message.From.Value, message.To.Value);
    }

    [Theory]
    [InlineData(MessageType.Task)]
    [InlineData(MessageType.Question)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Status)]
    public void ShouldCreateValidMessages_WhenConstructingWithAllMessageTypes(MessageType messageType)
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var content = $"Message of type {messageType}";

        // Act
        var message = new AgentMessage(fromAgent, toAgent, messageType, content);

        // Assert
        Assert.Equal(messageType, message.Type);
        Assert.Equal(content, message.Content);
    }

    #endregion

    #region Record Equality and HashCode Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var fromAgent = AgentId.From(Guid.NewGuid());
        var toAgent = AgentId.From(Guid.NewGuid());
        var messageType = MessageType.Task;
        var content = TestContent;
        var metadata = new Dictionary<string, object> { { "key", "value" } };

        var message1 = new AgentMessage(fromAgent, toAgent, messageType, content, metadata);
        var message2 = new AgentMessage(fromAgent, toAgent, messageType, content, metadata);

        // Act & Assert
        Assert.Equal(message1, message2);
        Assert.True(message1.Equals(message2));
        Assert.True(message1 == message2);
        Assert.False(message1 != message2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentFrom()
    {
        // Arrange
        var fromAgent1 = AgentId.Create();
        var fromAgent2 = AgentId.Create();
        var toAgent = AgentId.Create();

        var message1 = new AgentMessage(fromAgent1, toAgent, MessageType.Task, "Content");
        var message2 = new AgentMessage(fromAgent2, toAgent, MessageType.Task, "Content");

        // Act & Assert
        Assert.NotEqual(message1, message2);
        Assert.False(message1.Equals(message2));
        Assert.False(message1 == message2);
        Assert.True(message1 != message2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentTo()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent1 = AgentId.Create();
        var toAgent2 = AgentId.Create();

        var message1 = new AgentMessage(fromAgent, toAgent1, MessageType.Task, "Content");
        var message2 = new AgentMessage(fromAgent, toAgent2, MessageType.Task, "Content");

        // Act & Assert
        Assert.NotEqual(message1, message2);
        Assert.False(message1.Equals(message2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentType()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();

        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content");
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Question, "Content");

        // Act & Assert
        Assert.NotEqual(message1, message2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentContent()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();

        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content 1");
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content 2");

        // Act & Assert
        Assert.NotEqual(message1, message2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentMetadata()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var metadata1 = new Dictionary<string, object> { { "key", "value1" } };
        var metadata2 = new Dictionary<string, object> { { "key", "value2" } };

        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content", metadata1);
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content", metadata2);

        // Act & Assert
        Assert.NotEqual(message1, message2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityUsingOneWithMetadataOneWithout()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var metadata = new Dictionary<string, object> { { "key", "value" } };

        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content", metadata);
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content");

        // Act & Assert
        Assert.NotEqual(message1, message2);
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var fromAgent = AgentId.From(Guid.NewGuid());
        var toAgent = AgentId.From(Guid.NewGuid());
        var metadata = new Dictionary<string, object> { { "key", "value" } };

        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content", metadata);
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content", metadata);

        // Act
        var hash1 = message1.GetHashCode();
        var hash2 = message2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();

        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content 1");
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content 2");

        // Act
        var hash1 = message1.GetHashCode();
        var hash2 = message2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Record Deconstruction Tests

    [Fact]
    public void ShouldExtractAllProperties_WhenUsingDeconstruct()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var messageType = MessageType.Question;
        var content = "Question content";
        var metadata = new Dictionary<string, object> { { "urgent", true } };

        var message = new AgentMessage(fromAgent, toAgent, messageType, content, metadata);

        // Act
        var (from, to, type, messageContent, messageMetadata) = message;

        // Assert
        Assert.Equal(fromAgent, from);
        Assert.Equal(toAgent, to);
        Assert.Equal(messageType, type);
        Assert.Equal(content, messageContent);
        Assert.Equal(metadata, messageMetadata);
    }

    [Fact]
    public void ShouldExtractNullMetadata_WhenUsingDeconstructWithNullMetadata()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Status, "Status update");

        // Act
        var (from, to, type, content, metadata) = message;

        // Assert
        Assert.Equal(fromAgent, from);
        Assert.Equal(toAgent, to);
        Assert.Equal(MessageType.Status, type);
        Assert.Equal("Status update", content);
        Assert.Null(metadata);
    }

    #endregion

    #region Record With Expression Tests

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedContent_WhenUsingWithModifyContent()
    {
        // Arrange
        var originalMessage = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Task,
            "Original content");

        // Act
        var modifiedMessage = originalMessage with { Content = "Modified content" };

        // Assert
        Assert.Equal("Original content", originalMessage.Content);
        Assert.Equal("Modified content", modifiedMessage.Content);
        Assert.Equal(originalMessage.From, modifiedMessage.From);
        Assert.Equal(originalMessage.To, modifiedMessage.To);
        Assert.Equal(originalMessage.Type, modifiedMessage.Type);
        Assert.NotEqual(originalMessage, modifiedMessage);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedType_WhenUsingWithModifyType()
    {
        // Arrange
        var originalMessage = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Task,
            "Content");

        // Act
        var modifiedMessage = originalMessage with { Type = MessageType.Question };

        // Assert
        Assert.Equal(MessageType.Task, originalMessage.Type);
        Assert.Equal(MessageType.Question, modifiedMessage.Type);
        Assert.Equal(originalMessage.Content, modifiedMessage.Content);
        Assert.NotEqual(originalMessage, modifiedMessage);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedAgents_WhenUsingWithModifyAgents()
    {
        // Arrange
        var originalFrom = AgentId.Create();
        var originalTo = AgentId.Create();
        var newFrom = AgentId.Create();
        var newTo = AgentId.Create();

        var originalMessage = new AgentMessage(originalFrom, originalTo, MessageType.Response, "Response");

        // Act
        var modifiedMessage = originalMessage with { From = newFrom, To = newTo };

        // Assert
        Assert.Equal(originalFrom, originalMessage.From);
        Assert.Equal(originalTo, originalMessage.To);
        Assert.Equal(newFrom, modifiedMessage.From);
        Assert.Equal(newTo, modifiedMessage.To);
        Assert.Equal(originalMessage.Content, modifiedMessage.Content);
        Assert.NotEqual(originalMessage, modifiedMessage);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithMetadata_WhenUsingWithAddMetadata()
    {
        // Arrange
        var originalMessage = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Status,
            "Status update");

        var metadata = new Dictionary<string, object>
        {
            { "timestamp", DateTime.UtcNow },
            { "priority", "high" }
        };

        // Act
        var modifiedMessage = originalMessage with { Metadata = metadata };

        // Assert
        Assert.Null(originalMessage.Metadata);
        Assert.Equal(metadata, modifiedMessage.Metadata);
        Assert.Equal(2, modifiedMessage.Metadata!.Count);
        Assert.NotEqual(originalMessage, modifiedMessage);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedMetadata_WhenUsingWithModifyMetadata()
    {
        // Arrange
        var originalMetadata = new Dictionary<string, object> { { "version", 1 } };
        var newMetadata = new Dictionary<string, object> { { "version", 2 }, { "updated", true } };

        var originalMessage = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Task,
            "Task content",
            originalMetadata);

        // Act
        var modifiedMessage = originalMessage with { Metadata = newMetadata };

        // Assert
        Assert.Equal(originalMetadata, originalMessage.Metadata);
        Assert.Equal(newMetadata, modifiedMessage.Metadata);
        Assert.Single(originalMessage.Metadata!);
        Assert.Equal(2, modifiedMessage.Metadata!.Count);
        Assert.NotEqual(originalMessage, modifiedMessage);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithoutMetadata_WhenUsingWithRemoveMetadata()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { { "key", "value" } };
        var originalMessage = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Question,
            "Question",
            metadata);

        // Act
        var modifiedMessage = originalMessage with { Metadata = null };

        // Assert
        Assert.Equal(metadata, originalMessage.Metadata);
        Assert.Null(modifiedMessage.Metadata);
        Assert.NotEqual(originalMessage, modifiedMessage);
    }

    #endregion

    #region Metadata Specific Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMetadataWithComplexObjects()
    {
        // Arrange
        var complexMetadata = new Dictionary<string, object>
        {
            { "string", "test" },
            { "int", 42 },
            { "bool", true },
            { "datetime", DateTime.UtcNow },
            { "array", Int123 },
            { "nested", new { Name = "Test", Count = 5 } },
            { "null", null! }
        };

        // Act
        var message = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Task,
            "Complex metadata test",
            complexMetadata);

        // Assert
        Assert.Equal(7, message.Metadata!.Count);
        Assert.Equal("test", message.Metadata["string"]);
        Assert.Equal(42, message.Metadata["int"]);
        Assert.True((bool)message.Metadata["bool"]);
        Assert.IsType<DateTime>(message.Metadata["datetime"]);
        Assert.IsType<int[]>(message.Metadata["array"]);
        Assert.Null(message.Metadata["null"]);
    }

    [Fact]
    public void ShouldAffectOriginalDictionary_WhenUsingMetadataModificationAfterCreation()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { { "initial", "value" } };
        var message = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Response,
            "Response with metadata",
            metadata);

        // Act
        metadata.Add("added", "after creation");

        // Assert
        Assert.Equal(2, message.Metadata!.Count);
        Assert.Contains("added", message.Metadata.Keys);
        Assert.Equal("after creation", message.Metadata["added"]);
    }

    [Fact]
    public void ShouldAllowModification_WhenUsingMetadataWithEmptyDictionary()
    {
        // Arrange
        var emptyMetadata = new Dictionary<string, object>();
        var message = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Status,
            "Status with empty metadata",
            emptyMetadata);

        // Act
        emptyMetadata.Add("newKey", "newValue");

        // Assert
        Assert.Single(message.Metadata!);
        Assert.Equal("newValue", message.Metadata!["newKey"]);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingAgentMessageInCollection()
    {
        // Arrange
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var agent3 = AgentId.Create();

        var messages = new List<AgentMessage>
        {
            new(agent1, agent2, MessageType.Task, "Task 1"),
            new(agent2, agent3, MessageType.Question, "Question 1"),
            new(agent3, agent1, MessageType.Response, "Response 1"),
            new(agent1, agent3, MessageType.Status, "Status 1"),
            new(agent2, agent1, MessageType.Task, "Task 2")
        };

        // Act
        var tasksFromAgent1 = messages.Where(m => m.From.Equals(agent1) && m.Type == MessageType.Task).ToList();
        var messagesToAgent3 = messages.Where(m => m.To.Equals(agent3)).ToList();
        var allQuestions = messages.Where(m => m.Type == MessageType.Question).ToList();

        // Assert
        Assert.Equal(5, messages.Count);
        Assert.Single(tasksFromAgent1); // Only one task from agent1: "Task 1"
        Assert.Equal(2, messagesToAgent3.Count);
        Assert.Single(allQuestions);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingAgentMessageInHashSet()
    {
        // Arrange
        var fromAgent = AgentId.From(Guid.NewGuid());
        var toAgent = AgentId.From(Guid.NewGuid());
        var message1 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content");
        var message2 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Content"); // Same content
        var message3 = new AgentMessage(fromAgent, toAgent, MessageType.Task, "Different content");

        var hashSet = new HashSet<AgentMessage>
        {
            // Act
            message1,
            message2, // Should be treated as duplicate
            message3
        };

        // Assert
        Assert.Equal(2, hashSet.Count); // message1 and message2 are equal, so only 2 unique items
        Assert.Contains(message1, hashSet);
        Assert.Contains(message3, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingAgentMessageInDictionary()
    {
        // Arrange
        var message1 = new AgentMessage(AgentId.Create(), AgentId.Create(), MessageType.Task, "Task 1");
        var message2 = new AgentMessage(AgentId.Create(), AgentId.Create(), MessageType.Question, "Question 1");

        var dictionary = new Dictionary<AgentMessage, string>
        {
            { message1, "Processing task" },
            { message2, "Awaiting response" }
        };

        // Act & Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Processing task", dictionary[message1]);
        Assert.Equal("Awaiting response", dictionary[message2]);
        Assert.True(dictionary.ContainsKey(message1));
        Assert.True(dictionary.ContainsKey(message2));
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingAgentMessageWithVeryLongContent()
    {
        // Arrange
        var longContent = new string('A', 10000);

        // Act
        var message = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Response,
            longContent);

        // Assert
        Assert.Equal(10000, message.Content.Length);
        Assert.Equal(longContent, message.Content);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingAgentMessageWithSpecialCharacters()
    {
        // Arrange
        var specialContent = "Content with special chars: !@#$%^&*()[]{}|\\:;\"'<>,.?/~`±§";

        // Act
        var message = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Task,
            specialContent);

        // Assert
        Assert.Equal(specialContent, message.Content);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingAgentMessageWithUnicodeContent()
    {
        // Arrange
        var unicodeContent = "Message with émojis 🚀🎉 and ütf-8 çharacters: 你好世界";

        // Act
        var message = new AgentMessage(
            AgentId.Create(),
            AgentId.Create(),
            MessageType.Question,
            unicodeContent);

        // Assert
        Assert.Equal(unicodeContent, message.Content);
    }

    [Fact]
    public void ShouldMaintainIntegrity_WhenUsingAgentMessageUsingConversationFlow()
    {
        // Arrange
        var analyst = AgentId.Create();
        var researcher = AgentId.Create();
        var manager = AgentId.Create();

        // Act - Simulate a conversation flow
        var initialTask = new AgentMessage(manager, analyst, MessageType.Task, "Analyze market trends");
        var question = new AgentMessage(analyst, researcher, MessageType.Question, "What data do you have?");
        var response = new AgentMessage(researcher, analyst, MessageType.Response, "Here is the latest data...");
        var status = new AgentMessage(analyst, manager, MessageType.Status, "Analysis in progress");

        // Assert - Verify conversation integrity
        Assert.Equal(manager, initialTask.From);
        Assert.Equal(analyst, initialTask.To);
        Assert.Equal(analyst, question.From);
        Assert.Equal(researcher, question.To);
        Assert.Equal(researcher, response.From);
        Assert.Equal(analyst, response.To);
        Assert.Equal(analyst, status.From);
        Assert.Equal(manager, status.To);

        // Verify message types flow logically
        Assert.Equal(MessageType.Task, initialTask.Type);
        Assert.Equal(MessageType.Question, question.Type);
        Assert.Equal(MessageType.Response, response.Type);
        Assert.Equal(MessageType.Status, status.Type);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingAgentMessageToString()
    {
        // Arrange
        var fromAgent = AgentId.Create();
        var toAgent = AgentId.Create();
        var message = new AgentMessage(fromAgent, toAgent, MessageType.Task, TestContent);

        // Act
        var stringRepresentation = message.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("AgentMessage", stringRepresentation);
    }

    #endregion
}
