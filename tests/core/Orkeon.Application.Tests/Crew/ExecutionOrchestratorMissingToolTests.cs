using Microsoft.Extensions.AI;
using Orkeon.Application.Crew;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Regression tests for experiment 07 issue #10: when a tool referenced by an assistant's
/// tool_calls is not found in the registry, the orchestrator must still emit a tool message
/// carrying the matching tool_call_id, otherwise OpenAI-compat strict providers (DeepSeek)
/// reject the next LLM request with HTTP 400 "An assistant message with 'tool_calls' must
/// be followed by tool messages responding to each 'tool_call_id'".
/// </summary>
public class ExecutionOrchestratorMissingToolTests
{
    [Fact]
    public void BuildMissingToolMessage_UsesToolRole()
    {
        var message = ExecutionOrchestrator.BuildMissingToolMessage("call-42", "grep");

        Assert.Equal(ChatRole.Tool, message.Role);
    }

    [Fact]
    public void BuildMissingToolMessage_WrapsFunctionResultContentWithCallId()
    {
        var message = ExecutionOrchestrator.BuildMissingToolMessage("call-42", "grep");

        var result = Assert.IsType<FunctionResultContent>(Assert.Single(message.Contents));
        Assert.Equal("call-42", result.CallId);
    }

    [Fact]
    public void BuildMissingToolMessage_IncludesToolNameInErrorPayload()
    {
        var message = ExecutionOrchestrator.BuildMissingToolMessage("call-42", "grep");

        var result = Assert.IsType<FunctionResultContent>(message.Contents[0]);
        Assert.Contains("grep", result.Result?.ToString() ?? string.Empty);
        Assert.Contains("not found", result.Result?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractLastToolError_RecognizesMissingToolMessage()
    {
        // Round-trip: a message produced by BuildMissingToolMessage must be visible
        // to the circuit-breaker, otherwise repeated missing-tool calls go undetected.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Use the grep tool"),
            ExecutionOrchestrator.BuildMissingToolMessage("call-1", "grep")
        };

        var error = ExecutionOrchestrator.ExtractLastToolError((IReadOnlyList<ChatMessage>)messages);

        Assert.NotNull(error);
        Assert.Contains("grep", error);
    }
}
