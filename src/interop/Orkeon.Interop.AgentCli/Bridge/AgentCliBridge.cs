using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentCli.Process;

namespace Orkeon.Interop.AgentCli.Bridge;

/// <summary>
/// The MCP server the agent CLI sees through its relay (ADR-020, plan 4.3). It lists the tools of the
/// current call and holds every <c>tools/call</c> until Orkeon's loop has run the tool: it never runs one
/// itself. A call is matched to the <c>tool_use</c> block it answers by <c>_meta["claudecode/toolUseId"]</c>,
/// and a result may come before its call or after it — the provider ends <c>ChatAsync</c> on the assistant
/// message, and the CLI sends the call around that moment, before or after (probe 21).
/// </summary>
internal sealed partial class AgentCliBridge : IAsyncDisposable
{
    private const string ToolUseIdMeta = "claudecode/toolUseId";

    private readonly IAgentCliLineChannel _channel;
    private readonly int _maxResultCharacters;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    // Under _gate: the calls received and not answered, by tool use id; the results that came first; the
    // tool use ids the session expects calls for, with their tool names, for a call without the id.
    private readonly Dictionary<string, JsonNode> _waiting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Text, bool IsError)> _stashed = new(StringComparer.Ordinal);
    private readonly List<(string Id, string Name)> _expected = [];
    private IReadOnlyList<ToolSchema> _tools = [];
    private string _toolsKey = "";
    private bool _listed;

    public AgentCliBridge(IAgentCliLineChannel channel, int maxResultCharacters, ILogger logger)
    {
        _channel = channel;
        _maxResultCharacters = Math.Max(1_000, maxResultCharacters);
        _logger = logger;
        _loop = Task.Run(ServeAsync);
    }

    /// <summary>The tool use ids whose call has arrived and waits for its result.</summary>
    public IReadOnlyCollection<string> WaitingCalls
    {
        get
        {
            lock (_gate)
                return [.. _waiting.Keys];
        }
    }

    /// <summary>
    /// Sets the tools <c>tools/list</c> answers with — the current call's, already filtered by its modes. When
    /// they differ from the list the CLI has read, it is told (<c>notifications/tools/list_changed</c>, probe 13):
    /// the new list holds from its next turn.
    /// </summary>
    public async ValueTask SetToolsAsync(IReadOnlyList<ToolSchema> tools, CancellationToken cancellationToken)
    {
        var key = string.Join('\u001f', tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        bool notify;
        lock (_gate)
        {
            if (string.Equals(key, _toolsKey, StringComparison.Ordinal))
                return;
            _tools = tools;
            _toolsKey = key;
            notify = _listed;
        }

        if (notify)
            await _channel.WriteLineAsync(
                new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/tools/list_changed" }.ToJsonString(),
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records the tool use blocks the assistant message just asked for: their calls are on their way.</summary>
    public void Expect(IEnumerable<(string Id, string Name)> calls)
    {
        lock (_gate)
            _expected.AddRange(calls);
    }

    /// <summary>
    /// Hands the CLI the result of <paramref name="toolUseId"/>: answers its call when it has arrived, else keeps
    /// the result for the call to come. A result longer than <see cref="AgentCliOptions.MaxToolResultCharacters"/>
    /// is cut, with a note.
    /// </summary>
    public async ValueTask ResolveAsync(string toolUseId, string text, bool isError, CancellationToken cancellationToken)
    {
        var capped = Cap(text);
        JsonNode? requestId;
        lock (_gate)
        {
            _expected.RemoveAll(expected => string.Equals(expected.Id, toolUseId, StringComparison.Ordinal));
            if (!_waiting.Remove(toolUseId, out requestId))
            {
                _stashed[toolUseId] = (capped, isError);
                return;
            }
        }

        await AnswerCallAsync(requestId, capped, isError, cancellationToken).ConfigureAwait(false);
    }

    private string Cap(string text)
    {
        if (text.Length <= _maxResultCharacters)
            return text;
        return string.Concat(
            text.AsSpan(0, _maxResultCharacters),
            string.Create(CultureInfo.InvariantCulture,
                $"\n[Orkeon cut this result: {_maxResultCharacters} of its {text.Length} characters are shown.]"));
    }

    private async Task ServeAsync()
    {
        try
        {
            await foreach (var line in _channel.Incoming.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                await HandleAsync(line).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The session ended.
        }
        catch (InvalidOperationException ex)
        {
            LogBridgeClosed(ex.Message);
        }
    }

    private async Task HandleAsync(string line)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null || message["method"] is not JsonValue methodValue || !methodValue.TryGetValue<string>(out var method))
            return;

        var id = message["id"];
        var parameters = message["params"] as JsonObject;

        if (id is null)
        {
            if (method == "notifications/cancelled")
                DropCancelled(parameters);
            return;
        }

        switch (method)
        {
            case "initialize":
                await AnswerAsync(id, new JsonObject
                {
                    ["protocolVersion"] = parameters?["protocolVersion"]?.DeepClone() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
                    ["serverInfo"] = new JsonObject { ["name"] = "orkeon", ["version"] = "1" },
                }).ConfigureAwait(false);
                break;
            case "tools/list":
                await AnswerAsync(id, ListTools()).ConfigureAwait(false);
                break;
            case "tools/call":
                await ReceiveCallAsync(id, parameters).ConfigureAwait(false);
                break;
            case "ping":
                await AnswerAsync(id, new JsonObject()).ConfigureAwait(false);
                break;
            default:
                await WriteAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" },
                }).ConfigureAwait(false);
                break;
        }
    }

    private JsonObject ListTools()
    {
        IReadOnlyList<ToolSchema> tools;
        lock (_gate)
        {
            tools = _tools;
            _listed = true;
        }

        var list = new JsonArray();
        foreach (var tool in tools)
        {
            list.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonSerializer.SerializeToNode(tool.ToInputJsonSchema()),
            });
        }

        return new JsonObject { ["tools"] = list };
    }

    private async Task ReceiveCallAsync(JsonNode requestId, JsonObject? parameters)
    {
        var name = parameters?["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var called) ? called : "";
        var toolUseId = parameters?["_meta"]?[ToolUseIdMeta] is JsonValue meta && meta.TryGetValue<string>(out var metaId) ? metaId : null;

        (string Text, bool IsError) result;
        lock (_gate)
        {
            // Without the id, the oldest expected call of that tool: the CLI sends calls in block order.
            if (toolUseId is null)
            {
                var match = _expected.FindIndex(expected => string.Equals(expected.Name, name, StringComparison.Ordinal));
                if (match >= 0)
                {
                    toolUseId = _expected[match].Id;
                    _expected.RemoveAt(match);
                }
            }

            if (toolUseId is null)
            {
                LogUnmatchedCall(name);
                toolUseId = "unmatched-" + requestId.ToJsonString();
            }

            if (!_stashed.Remove(toolUseId, out result))
            {
                _waiting[toolUseId] = requestId.DeepClone();
                return;
            }
        }

        await AnswerCallAsync(requestId, result.Text, result.IsError, CancellationToken.None).ConfigureAwait(false);
    }

    private void DropCancelled(JsonObject? parameters)
    {
        var cancelled = parameters?["requestId"]?.ToJsonString();
        if (cancelled is null)
            return;
        lock (_gate)
        {
            foreach (var (toolUseId, requestId) in _waiting.ToList())
            {
                if (string.Equals(requestId.ToJsonString(), cancelled, StringComparison.Ordinal))
                    _waiting.Remove(toolUseId);
            }
        }
    }

    private Task AnswerCallAsync(JsonNode requestId, string text, bool isError, CancellationToken cancellationToken) =>
        AnswerAsync(requestId, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        }, cancellationToken);

    private Task AnswerAsync(JsonNode id, JsonObject result, CancellationToken cancellationToken = default) =>
        WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }, cancellationToken);

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _channel.WriteLineAsync(message.ToJsonString(), linked.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _stop.Dispose();
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "The agent CLI called tool '{Tool}' for no tool use block of its message; the call is left unanswered.")]
    private partial void LogUnmatchedCall(string tool);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug, Message = "The agent CLI's bridge closed: {Reason}")]
    private partial void LogBridgeClosed(string reason);
}
