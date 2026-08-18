using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Common;
using AppParsedToolCall = Orkeon.Application.Interfaces.LLM.ParsedToolCall;
using IAppToolCallParser = Orkeon.Application.Interfaces.LLM.IToolCallParser;
using DomainToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written doubles shared by the execution-loop test suites
/// (SONAR-14 T2: NativeToolCallingAgentLoop, LegacyTextAgentLoop,
/// ChatOptionsComposer, ChatToolDispatcher).
/// </summary>
internal sealed class SpyExecutionLogger : ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Messages.Add($"[{logLevel}] {formatter(state, exception)}");
}

/// <summary>
/// A scripted <see cref="ILlmProvider"/> (full provider): dequeues one
/// <see cref="LlmResponse"/> per <see cref="ChatAsync"/> call and records what it received.
/// </summary>
internal sealed class ScriptedFullLlmProvider : ILlmProvider
{
    private readonly Queue<LlmResponse> _responses = new();

    public string Name => "scripted-full";

    public List<LlmMessage[]> ReceivedTurns { get; } = [];

    public List<LlmConfig?> ReceivedConfigs { get; } = [];

    public void Enqueue(LlmResponse response) => _responses.Enqueue(response);

    public void EnqueueText(string content) =>
        _responses.Enqueue(new LlmResponse { Content = content });

    /// <summary>Enqueues a response whose raw body carries OpenAI-shaped tool calls.</summary>
    public void EnqueueOpenAiToolCall(string callId, string toolName, string argumentsJson) =>
        _responses.Enqueue(new LlmResponse
        {
            Content = "",
            RawResponseBody =
                $$$"""{"choices":[{"message":{"tool_calls":[{"id":"{{{callId}}}","type":"function","function":{"name":"{{{toolName}}}","arguments":"{{{argumentsJson.Replace("\"", "\\\"", StringComparison.Ordinal)}}}"}}]}}]}""",
        });

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The native loop only uses ChatAsync.");

    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedTurns.Add(messages);
        ReceivedConfigs.Add(config);

        return System.Threading.Tasks.Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new LlmResponse { Content = "default scripted answer" });
    }
}

/// <summary>
/// Parses the OpenAI-shaped raw body produced by
/// <see cref="ScriptedFullLlmProvider.EnqueueOpenAiToolCall"/>. Deliberately minimal:
/// the loop under test only needs the parsed list, not a faithful provider dialect.
/// </summary>
internal sealed class OpenAiShapedToolCallParser : IAppToolCallParser
{
    public IReadOnlyList<AppParsedToolCall> ParseToolCalls(JsonElement responseBody)
    {
        var calls = new List<AppParsedToolCall>();
        if (!responseBody.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return calls;
        if (!choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("tool_calls", out var toolCalls))
            return calls;

        foreach (var tc in toolCalls.EnumerateArray())
        {
            var id = tc.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
            var function = tc.GetProperty("function");
            var name = function.GetProperty("name").GetString() ?? "";
            var args = new Dictionary<string, object?>();
            var argsJson = function.GetProperty("arguments").GetString();
            if (!string.IsNullOrWhiteSpace(argsJson))
            {
                using var doc = JsonDocument.Parse(argsJson);
                foreach (var property in doc.RootElement.EnumerateObject())
                    args[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
            }

            calls.Add(new AppParsedToolCall(id, name, args));
        }

        return calls;
    }

    public object FormatToolResult(AppParsedToolCall toolCall, string result, bool success) =>
        new { toolCall.Id, result, success };

    public object FormatAssistantToolCallMessage(JsonElement responseBody) => responseBody.GetRawText();
}

/// <summary>A strategy double whose native support and parser are supplied by the test.</summary>
internal sealed class FakeToolCallingStrategy : IToolCallingStrategy
{
    public FakeToolCallingStrategy(IAppToolCallParser parser, bool supportsNative = true)
    {
        Parser = parser;
        SupportsNativeToolCalling = supportsNative;
    }

    public IToolSchemaFormatter Formatter { get; } = new NullToolSchemaFormatter();

    public IAppToolCallParser Parser { get; }

    public bool SupportsNativeToolCalling { get; }

    private sealed class NullToolSchemaFormatter : IToolSchemaFormatter
    {
        public Dictionary<string, object> FormatToolsForPayload(
            IReadOnlyList<ToolSchema> tools, ToolCallMode mode = ToolCallMode.Auto) => [];
    }
}

/// <summary>
/// A recording tool whose outcome the test scripts: success text, failure text, or a thrown
/// exception. Records every <see cref="ToolCallRequest"/> it receives.
/// </summary>
internal sealed class SpyTool : ITool
{
    private readonly string _result;
    private readonly bool _succeed;
    private readonly Exception? _throw;

    public SpyTool(string name, string result = "ok", bool succeed = true, Exception? exceptionToThrow = null)
    {
        Name = name;
        _result = result;
        _succeed = succeed;
        _throw = exceptionToThrow;
    }

    public string Name { get; }

    public string Description => $"Spy tool {Name}";

    public ToolSchema Schema => new(
        Name,
        Description,
        new Dictionary<string, ParameterSchema>
        {
            ["input"] = new("string", "Input parameter", true),
        });

    public List<DomainToolCallRequest> Calls { get; } = [];

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_throw is not null) throw _throw;
        return System.Threading.Tasks.Task.FromResult(new ToolResult { Success = _succeed, Output = _result });
    }

    public Task<ToolCallResponse> CallAsync(DomainToolCallRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        if (_throw is not null) throw _throw;
        return System.Threading.Tasks.Task.FromResult(new ToolCallResponse(
            Success: _succeed,
            Result: _succeed ? _result : null,
            Error: _succeed ? null : _result));
    }

    public bool ValidateInput(string input) => true;
}

/// <summary>
/// A scripted <see cref="IBasicLlmProvider"/>: returns the queued responses in order
/// (falling back to a fixed text once the queue is dry) and records every prompt.
/// </summary>
internal sealed class ScriptedBasicLlmProvider : IBasicLlmProvider
{
    private readonly Queue<string> _responses = new();

    public string Name => "scripted-basic";

    public string FallbackResponse { get; set; } = "final answer";

    public List<string> ReceivedPrompts { get; } = [];

    public void Enqueue(string response) => _responses.Enqueue(response);

    public Task<string> ChatAsync(string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedPrompts.Add(message);
        return System.Threading.Tasks.Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : FallbackResponse);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(true);
}
