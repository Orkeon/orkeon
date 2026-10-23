using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Common;
using AppParsedToolCall = Orkeon.Application.Interfaces.LLM.ParsedToolCall;
using IAppToolCallParser = Orkeon.Application.Interfaces.LLM.IToolCallParser;
using DomainToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
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

    /// <summary>Enqueues one response whose raw body carries several OpenAI-shaped tool calls, in order.</summary>
    public void EnqueueOpenAiToolCalls(params (string CallId, string ToolName)[] calls) =>
        _responses.Enqueue(new LlmResponse
        {
            Content = "",
            RawResponseBody = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            tool_calls = calls.Select(c => new
                            {
                                id = c.CallId,
                                type = "function",
                                function = new { name = c.ToolName, arguments = "{}" },
                            }).ToArray(),
                        },
                    },
                },
            }),
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
internal sealed class SpyTool : IBaseTool
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

/// <summary>A tool that returns a given <see cref="ToolCallResponse"/> as is (RT-10).</summary>
internal sealed class StubResponseTool : IBaseTool
{
    private readonly ToolCallResponse _response;

    public StubResponseTool(string name, ToolCallResponse response)
    {
        Name = name;
        _response = response;
    }

    public string Name { get; }

    public string Description => $"Stub tool {Name}";

    public ToolSchema Schema => new(Name, Description, new Dictionary<string, ParameterSchema>());

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ToolCallResponse> CallAsync(DomainToolCallRequest request, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_response);

    public bool ValidateInput(string input) => true;
}

/// <summary>
/// Measures how many gated tool calls are inside their body at once (RT-07). The first
/// <c>expected</c> callers meet at a barrier; a barrier nobody joins in time is broken after a
/// generous guard, so a sequential loop fails its assertion instead of hanging.
/// </summary>
internal sealed class ConcurrencyProbe
{
    private readonly object _lock = new();
    private readonly int _expected;
    private readonly TimeSpan _guard;
    private readonly TaskCompletionSource _allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _events = [];
    private int _current;
    private int _max;
    private bool _broken;

    public ConcurrencyProbe(int expected, TimeSpan? guard = null)
    {
        _expected = expected;
        _guard = guard ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Completes once <c>expected</c> calls were inside the barrier together.</summary>
    public System.Threading.Tasks.Task AllEntered => _allEntered.Task;

    /// <summary>The highest number of calls that were inside the barrier at the same time.</summary>
    public int Max { get { lock (_lock) return _max; } }

    /// <summary>The start/end events in the order they happened.</summary>
    public List<string> Events() { lock (_lock) return [.. _events]; }

    public void Log(string text) { lock (_lock) _events.Add(text); }

    public async System.Threading.Tasks.Task EnterAsync()
    {
        lock (_lock)
        {
            _current++;
            _max = Math.Max(_max, _current);
            if (_max >= _expected) _allEntered.TrySetResult();
        }

        if (_broken) return;
        var done = await System.Threading.Tasks.Task.WhenAny(_allEntered.Task, System.Threading.Tasks.Task.Delay(_guard)).ConfigureAwait(false);
        if (done != _allEntered.Task) _broken = true;
    }

    public void Leave() { lock (_lock) _current--; }
}

/// <summary>
/// An <see cref="IToolApprovalChannel"/> double: records the requests in arrival order and how many
/// were in flight at the same time. It does not serialise anything itself.
/// </summary>
internal sealed class RecordingApprovalChannel : IToolApprovalChannel
{
    private readonly object _lock = new();
    private int _current;
    private int _max;

    public List<string> Requested { get; } = [];

    public int MaxConcurrent { get { lock (_lock) return _max; } }

    public async System.Threading.Tasks.Task<ToolApprovalDecision> RequestAsync(
        ToolApprovalRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _current++;
            _max = Math.Max(_max, _current);
            Requested.Add(request.ToolName);
        }

        await System.Threading.Tasks.Task.Delay(50, cancellationToken).ConfigureAwait(false);
        lock (_lock) _current--;
        return ToolApprovalDecision.AllowOnce;
    }
}

/// <summary>
/// A tool whose body can be held (a gate the test releases), joined to a <see cref="ConcurrencyProbe"/>
/// barrier and preceded by an approval. Its result text is its own name.
/// </summary>
internal sealed class GatedTool : IBaseTool
{
    private readonly ToolAccess _access;
    private readonly ConcurrencyProbe? _probe;
    private readonly bool _joinsBarrier;
    private readonly IToolApprovalChannel? _approval;

    public GatedTool(
        string name, ToolAccess access, ConcurrencyProbe? probe = null, bool joinsBarrier = true,
        IToolApprovalChannel? approval = null, TaskCompletionSource<bool>? hold = null)
    {
        Name = name;
        _access = access;
        _probe = probe;
        _joinsBarrier = joinsBarrier;
        _approval = approval;
        Hold = hold;
    }

    public string Name { get; }

    public string Description => $"Gated tool {Name}";

    public ToolAccess Access => _access;

    public TaskCompletionSource<bool>? Hold { get; }

    public ToolSchema Schema => new(
        Name,
        Description,
        new Dictionary<string, ParameterSchema> { ["input"] = new("string", "Input parameter", false) });

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async Task<ToolCallResponse> CallAsync(DomainToolCallRequest request, CancellationToken cancellationToken = default)
    {
        _probe?.Log($"start:{Name}");
        if (_approval is not null) await _approval.RequestAsync(
                new ToolApprovalRequest { ToolName = Name, Arguments = System.Collections.Immutable.ImmutableDictionary<string, object?>.Empty, Mode = "default", Reason = "test" },
                cancellationToken).ConfigureAwait(false);
        var joined = _probe is not null && _joinsBarrier;
        if (joined) await _probe!.EnterAsync().ConfigureAwait(false);
        if (Hold is not null) await Hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        _probe?.Log($"end:{Name}");
        if (joined) _probe!.Leave();
        return new ToolCallResponse(Success: true, Result: Name, Error: null);
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

    public List<LlmConfig?> ReceivedConfigs { get; } = [];

    public void Enqueue(string response) => _responses.Enqueue(response);

    public Task<string> ChatAsync(string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceivedPrompts.Add(message);
        ReceivedConfigs.Add(config);
        return System.Threading.Tasks.Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : FallbackResponse);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(true);
}
