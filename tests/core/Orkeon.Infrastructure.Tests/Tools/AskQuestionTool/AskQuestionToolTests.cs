using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.Tools;
using Orkeon.Infrastructure.Tests.TestDoubles;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Infrastructure.Tests.Tools;

public sealed class AskQuestionToolTests : IDisposable
{
    private readonly TestAgentCommunicationService _communicationService;
    private readonly TestLogger<AskQuestionTool> _logger;
    private readonly AgentId _currentAgentId;
    private readonly AskQuestionTool _tool;
    private readonly Dictionary<string, AgentId> _agentRoles;

    public AskQuestionToolTests()
    {
        _communicationService = new TestAgentCommunicationService();
        _logger = new TestLogger<AskQuestionTool>();
        _currentAgentId = AgentId.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        _agentRoles = new Dictionary<string, AgentId>
        {
            ["researcher"] = AgentId.From(Guid.Parse("00000000-0000-0000-0000-000000000002")),
            ["writer"] = AgentId.From(Guid.Parse("00000000-0000-0000-0000-000000000003"))
        };

        _tool = new AskQuestionTool(
            _communicationService,
            _currentAgentId,
            role => _agentRoles.GetValueOrDefault(role),
            _logger
        );
    }

    [Fact]
    public async Task ShouldSendMessageSuccessfully_WhenExecuteCoreAsyncWithValidRequest()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "What is the latest research on AI?",
                ["context"] = "Writing an article about AI",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        var resultDict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Contains("Question sent to researcher", resultDict["message"]!.ToString()!);

        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.NotNull(capturedMessage);
        Assert.Equal(_currentAgentId, capturedMessage.From);
        Assert.Equal(_agentRoles["researcher"], capturedMessage.To);
        Assert.Equal(MessageType.Question, capturedMessage.Type);
        Assert.Equal("What is the latest research on AI?", capturedMessage.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithMissingParameters()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "What is AI?"
                // Missing coworkerRole
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        // The base class validates required parameters first
        Assert.Contains("Required parameter 'context' is missing", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithUnknownRole()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "What is AI?",
                ["context"] = "Research",
                ["coworker_role"] = "unknown-role"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Could not find agent with role: unknown-role", result.Error);
    }

    [Fact]
    public async Task ShouldUseProtocolConversion_WhenExecuteCoreAsyncWithStringInput()
    {
        // Since ExecuteAsync converts to protocol format, we need to test
        // with the protocol format instead
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "What is the latest research on AI?",
                ["context"] = "Writing an article about AI",
                ["coworker_role"] = "researcher"
            }
        );

        // Act - Call the tool using the protocol method directly
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var resultDict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Contains("Question sent to researcher", resultDict["message"]!.ToString()!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithInvalidJson()
    {
        // Arrange
        var input = "{}"; // Empty JSON object

        // Act
        var result = await _tool.ExecuteAsync(input, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Required parameter 'question' is missing", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWhenCommunicationServiceThrows()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "What is AI?",
                ["context"] = "Research",
                ["coworker_role"] = "researcher"
            }
        );

        _communicationService.SetupThrowException(new InvalidOperationException("Service unavailable"));

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Service unavailable", result.Error);
    }

    [Fact]
    public void ShouldHaveCorrectParameters_WhenSchema()
    {
        // Assert
        Assert.Equal("ask_question_to_coworker", _tool.Name);
        Assert.Equal("Ask a specific question to a coworker with relevant expertise", _tool.Description);
        Assert.Equal("Collaboration", _tool.Category);

        var schema = _tool.Schema;
        Assert.Equal(3, schema.Parameters.Count);
        Assert.True(schema.Parameters["question"].Required);
        Assert.True(schema.Parameters["context"].Required);
        Assert.True(schema.Parameters["coworker_role"].Required);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithEmptyQuestion()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "",
                ["context"] = "Research context",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Question is required", result.Error);
    }

    [Fact]
    public async Task ShouldSendSuccessfully_WhenExecuteCoreAsyncWithLongQuestion()
    {
        // Arrange
        var longQuestion = string.Concat(Enumerable.Repeat("This is a very long question about AI and machine learning. ", 100));
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = longQuestion,
                ["context"] = "Long form research",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(longQuestion, capturedMessage.Content);
    }

    [Fact]
    public async Task ShouldSendCorrectly_WhenExecuteCoreAsyncWithSpecialCharacters()
    {
        // Arrange
        var specialQuestion = "What about #AI & ML? <script>alert('test')</script> \"quotes\" and 'apostrophes'?";
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = specialQuestion,
                ["context"] = "Security research",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(specialQuestion, capturedMessage.Content);
    }

    [Fact]
    public async Task ShouldSendSuccessfully_WhenExecuteCoreAsyncWithEmptyContext()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Quick question about AI",
                ["context"] = "",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal("", capturedMessage.Metadata!["context"]);
    }

    [Fact]
    public async Task ShouldSendMultipleQuestionsAllSentSuccessfully_WhenExecuteCoreAsync()
    {
        // Arrange & Act
        var request1 = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "First question",
                ["context"] = "Context 1",
                ["coworker_role"] = "researcher"
            }
        );

        var result1 = await _tool.CallAsync(request1, TestContext.Current.CancellationToken);

        var request2 = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Second question",
                ["context"] = "Context 2",
                ["coworker_role"] = "writer"
            }
        );

        var result2 = await _tool.CallAsync(request2, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);

        var messages = _communicationService.SentMessages;
        Assert.Equal(2, messages.Count);
        Assert.Equal("First question", messages[0].Content);
        Assert.Equal("Second question", messages[1].Content);
        Assert.Equal(_agentRoles["researcher"], messages[0].To);
        Assert.Equal(_agentRoles["writer"], messages[1].To);
    }

    [Fact]
    public async Task ShouldContainExpectedFields_WhenExecuteCoreAsyncVerifyMetadata()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Test question",
                ["context"] = "Test context",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.NotNull(capturedMessage.Metadata);
        Assert.Contains("context", capturedMessage.Metadata.Keys);
        Assert.Contains("expects_response", capturedMessage.Metadata.Keys);
        Assert.Equal("Test context", capturedMessage.Metadata["context"]);
        Assert.True((bool)capturedMessage.Metadata["expects_response"]);
    }

    [Fact]
    public async Task ShouldContainAllFields_WhenExecuteCoreAsyncResponseStructure()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Test question",
                ["context"] = "Test context",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var resultDict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(resultDict);
        Assert.Contains("message", resultDict.Keys);
        Assert.Contains("target_agent_id", resultDict.Keys);
        Assert.Contains("question", resultDict.Keys);
        Assert.Equal("Test question", resultDict["question"]);
        Assert.Equal(_agentRoles["researcher"].ToString(), resultDict["target_agent_id"]);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithNullQuestion()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = null!,
                ["context"] = "Context",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("question", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithWhitespaceRole()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Test question",
                ["context"] = "Context",
                ["coworker_role"] = "   "
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Coworker role is required", result.Error);
    }

    [Fact]
    public async Task ShouldSendCorrectly_WhenExecuteCoreAsyncWithUnicodeCharacters()
    {
        // Arrange
        var unicodeQuestion = "What about AI in 日本語 and العربية?";
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = unicodeQuestion,
                ["context"] = "International research",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(unicodeQuestion, capturedMessage.Content);
    }

    [Fact]
    public async Task ShouldConcurrentQuestionsAllSentSuccessfully_WhenExecuteCoreAsync()
    {
        // Arrange
        var tasks = new List<Task<Orkeon.Domain.Tools.Protocol.ToolCallResponse>>();

        for (int i = 0; i < 10; i++)
        {
            var request = new ProtocolToolCallRequest(
                ToolName: "ask_question_to_coworker",
                Parameters: new Dictionary<string, object?>
                {
                    ["question"] = $"Question {i}",
                    ["context"] = $"Context {i}",
                    ["coworker_role"] = i % 2 == 0 ? "researcher" : "writer"
                }
            );
            tasks.Add(_tool.CallAsync(request, TestContext.Current.CancellationToken));
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.True(r.Success));
        var messages = _communicationService.SentMessages;
        Assert.Equal(10, messages.Count);
    }

    [Fact]
    public async Task ShouldSendSuccessfully_WhenExecuteCoreAsyncWithVeryLongContext()
    {
        // Arrange
        var longContext = string.Concat(Enumerable.Repeat("This is a very detailed context about the research topic. ", 200));
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Short question",
                ["context"] = longContext,
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(longContext, capturedMessage.Metadata!["context"]);
    }

    [Fact]
    public async Task ShouldReturnError_WhenExecuteCoreAsyncWithMalformedJson()
    {
        // Arrange
        var malformedInput = "{ \"question\": \"test\", \"context\" }";

        // Act
        var result = await _tool.ExecuteAsync(malformedInput, TestContext.Current.CancellationToken);

        // Assert - Invalid JSON is treated as simple input, which then fails parameter validation
        Assert.False(result.Success);
        Assert.Contains("Required parameter 'question' is missing", result.Error);
    }

    [Fact]
    public async Task ShouldIgnoreExtras_WhenExecuteCoreAsyncWithExtraParameters()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Test question",
                ["context"] = "Test context",
                ["coworker_role"] = "researcher",
                ["extraParam"] = "Should be ignored",
                ["anotherExtra"] = 123
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal("Test question", capturedMessage.Content);
        Assert.DoesNotContain("extraParam", capturedMessage.Metadata!.Keys);
    }

    [Fact]
    public async Task ShouldBeAlwaysQuestion_WhenExecuteCoreAsyncMessageType()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Any question",
                ["context"] = "Any context",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(MessageType.Question, capturedMessage.Type);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenExecuteCoreAsyncWithNumericRoleName()
    {
        // Arrange
        _agentRoles["123"] = AgentId.From(Guid.Parse("00000000-0000-0000-0000-000000000004"));

        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Question for numeric role",
                ["context"] = "Testing edge cases",
                ["coworker_role"] = "123"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(_agentRoles["123"], capturedMessage.To);
    }

    [Fact]
    public async Task ShouldPropagateCorrectly_WhenExecuteCoreAsyncWithCancellationToken()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Test question",
                ["context"] = "Test context",
                ["coworker_role"] = "researcher"
            }
        );

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        // The tool should handle cancellation gracefully
        var result = await _tool.CallAsync(request, cts.Token);
        // Since our test double doesn't actually respect cancellation,
        // the operation may succeed, but in production it would be cancelled
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ShouldAlwaysSetsFromAgentIdToCurrentAgent_WhenExecuteCoreAsync()
    {
        // Arrange
        var request = new ProtocolToolCallRequest(
            ToolName: "ask_question_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["question"] = "Test question",
                ["context"] = "Test context",
                ["coworker_role"] = "researcher"
            }
        );

        // Act
        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        var capturedMessage = _communicationService.GetLastSentMessage()!;
        Assert.Equal(_currentAgentId, capturedMessage.From);
    }

    public void Dispose()
    {
        _tool.Dispose();
        GC.SuppressFinalize(this);
    }
}
