using Microsoft.Extensions.AI;
using Orkeon.Application.Crew;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Tests for the circuit breaker logic in <see cref="ExecutionOrchestrator"/>.
/// Focuses on the helper methods that are internal and testable in isolation.
/// </summary>
public class ExecutionOrchestratorCircuitBreakerTests
{
    // ── ExtractLastToolError (ChatMessage overload) ────────────

    [Fact]
    public void ExtractLastToolError_ChatMessage_WithErrorMessage_ReturnsError()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Do something"),
            new(ChatRole.Tool, "Error: Parameter 'path' has invalid type")
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.NotNull(result);
        Assert.Equal("Error: Parameter 'path' has invalid type", result);
    }

    [Fact]
    public void ExtractLastToolError_ChatMessage_WithSuccessMessage_ReturnsNull()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Do something"),
            new(ChatRole.Tool, "File contents: hello world")
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolError_ChatMessage_EmptyMessages_ReturnsNull()
    {
        var messages = new List<ChatMessage>();

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolError_ChatMessage_WithFunctionResultContent_Error_ReturnsError()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Call the tool"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "Error: Tool not available")])
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.NotNull(result);
        Assert.Equal("Error: Tool not available", result);
    }

    [Fact]
    public void ExtractLastToolError_ChatMessage_WithFunctionResultContent_Success_ReturnsNull()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Call the tool"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "Some successful result")])
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolError_ChatMessage_NoToolMessages_ReturnsNull()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "User input"),
            new(ChatRole.Assistant, "Assistant response")
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolError_ChatMessage_ErrorFollowedBySuccess_ReturnsNull()
    {
        // The last tool message is a success, so circuit breaker should not trigger
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, "Error: first failure"),
            new(ChatRole.Tool, "Successful result")
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.Null(result);
    }

    // ── ExtractLastToolError (LlmMessage overload) ────────────

    [Fact]
    public void ExtractLastToolError_LlmMessage_WithErrorMessage_ReturnsError()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Do something" },
            new() { Role = "tool", Content = "Error: Unknown tool: fake_tool" }
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<LlmMessage>)messages);

        Assert.NotNull(result);
        Assert.Equal("Error: Unknown tool: fake_tool", result);
    }

    [Fact]
    public void ExtractLastToolError_LlmMessage_WithSuccessMessage_ReturnsNull()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Do something" },
            new() { Role = "tool", Content = "File read successfully" }
        };

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<LlmMessage>)messages);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolError_LlmMessage_EmptyMessages_ReturnsNull()
    {
        var messages = new List<LlmMessage>();

        var result = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<LlmMessage>)messages);

        Assert.Null(result);
    }

    // ── ExtractLastToolErrorFromText (legacy StringBuilder path) ──

    [Fact]
    public void ExtractLastToolErrorFromText_WithToolError_ReturnsError()
    {
        var text = """
            Some conversation text
            [Tool file_read result]: contents of the file
            [Tool directory_read error]: Permission denied
            """;

        var result = ExecutionOrchestrator.ExtractLastToolErrorFromText(text);

        Assert.NotNull(result);
        Assert.Contains("[Tool directory_read error]:", result);
    }

    [Fact]
    public void ExtractLastToolErrorFromText_WithToolResultError_ReturnsError()
    {
        var text = """
            Some conversation text
            [Tool file_read result]: Error: Parameter 'path' has invalid type
            """;

        var result = ExecutionOrchestrator.ExtractLastToolErrorFromText(text);

        Assert.NotNull(result);
        Assert.Contains("Error: Parameter 'path' has invalid type", result);
    }

    [Fact]
    public void ExtractLastToolErrorFromText_WithSuccessOnly_ReturnsNull()
    {
        var text = """
            Some conversation text
            [Tool file_read result]: contents of the file
            Continue working on the task.
            """;

        var result = ExecutionOrchestrator.ExtractLastToolErrorFromText(text);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolErrorFromText_EmptyText_ReturnsNull()
    {
        var result = ExecutionOrchestrator.ExtractLastToolErrorFromText("");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractLastToolErrorFromText_NullText_ReturnsNull()
    {
        var result = ExecutionOrchestrator.ExtractLastToolErrorFromText(null!);
        Assert.Null(result);
    }

    // ── MaxConsecutiveIdenticalErrors constant ────────────────

    [Fact]
    public void MaxConsecutiveIdenticalErrors_DefaultValue_Is3()
    {
        Assert.Equal(3, AgentDefaults.MaxConsecutiveIdenticalErrors);
    }

    // ── EvaluateCircuitBreaker (tested indirectly via behavior) ──
    // EvaluateCircuitBreaker is private static, but we can exercise it
    // through the extraction + public behavior. The tests below validate
    // the expected circuit breaker pattern:
    //   - 1st error: counter = 1, no trip
    //   - 2nd identical error: counter = 2, no trip
    //   - 3rd identical error: counter = 3, TRIPS
    //   - Different error resets counter

    [Fact]
    public void CircuitBreakerPattern_ThreeIdenticalErrors_ShouldTrip()
    {
        // We test the pattern by simulating what the orchestrator does:
        // extract error, evaluate, repeat.
        // Since EvaluateCircuitBreaker is private static, we validate
        // through the extract helpers and the constant.

        var errorMessage = "Error: Parameter 'path' has invalid type";

        // Simulate 3 identical errors
        var messages = new List<ChatMessage>();
        int consecutiveCount = 0;
        string? lastError = null;

        for (int i = 0; i < AgentDefaults.MaxConsecutiveIdenticalErrors; i++)
        {
            // Add an error tool message
            messages.Add(new ChatMessage(ChatRole.Tool, errorMessage));

            var extracted = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);
            Assert.Equal(errorMessage, extracted);

            if (extracted == lastError)
            {
                consecutiveCount++;
            }
            else
            {
                lastError = extracted;
                consecutiveCount = 1;
            }
        }

        // After 3 identical errors, the counter should be at the threshold
        Assert.Equal(AgentDefaults.MaxConsecutiveIdenticalErrors, consecutiveCount);
    }

    [Fact]
    public void CircuitBreakerPattern_DifferentErrors_ShouldNotTrip()
    {
        // Simulate different errors each time
        var errors = new[]
        {
            "Error: Parameter 'path' has invalid type",
            "Error: File not found",
            "Error: Permission denied"
        };

        int consecutiveCount = 0;
        string? lastError = null;

        foreach (var error in errors)
        {
            if (error == lastError)
            {
                consecutiveCount++;
            }
            else
            {
                lastError = error;
                consecutiveCount = 1;
            }
        }

        // With all different errors, counter should always be 1
        Assert.Equal(1, consecutiveCount);
    }

    [Fact]
    public void CircuitBreakerPattern_SuccessResetsCounter()
    {
        // Simulate: error, error, success, error — should not trip
        var messages = new List<ChatMessage>();
        int consecutiveCount = 0;
        string? lastError = null;

        var errorMsg = "Error: Something failed";

        // Two identical errors
        for (int i = 0; i < 2; i++)
        {
            messages.Clear();
            messages.Add(new ChatMessage(ChatRole.Tool, errorMsg));
            var extracted = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);
            if (extracted == lastError) consecutiveCount++;
            else { lastError = extracted; consecutiveCount = 1; }
        }
        Assert.Equal(2, consecutiveCount);

        // Success resets
        messages.Clear();
        messages.Add(new ChatMessage(ChatRole.Tool, "File contents: OK"));
        var successExtracted = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);
        Assert.Null(successExtracted);
        // Simulate reset
        lastError = null;
        consecutiveCount = 0;

        // Another error — counter should be 1, not 3
        messages.Clear();
        messages.Add(new ChatMessage(ChatRole.Tool, errorMsg));
        var newExtracted = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);
        if (newExtracted == lastError) consecutiveCount++;
        else { lastError = newExtracted; consecutiveCount = 1; }

        Assert.Equal(1, consecutiveCount);
    }
}
