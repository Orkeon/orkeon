using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Exceptions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentCli.Bridge;
using Orkeon.Interop.AgentCli.Engine;
using Orkeon.Interop.AgentCli.Process;

namespace Orkeon.Interop.AgentCli.Sessions;

/// <summary>
/// One agent CLI process and the conversation it holds (plan 4.4). Orkeon keeps the history: each call hands
/// the whole conversation, and the session writes only what the CLI has not seen — the tool results it waits
/// for, then the new user messages. A call that does not extend what was delivered is not this session's: the
/// caller closes it and seeds a new one. A call runs one model message: it ends on the assistant message,
/// never on the bridge's <c>tools/call</c>, which is what keeps Orkeon's loop and the CLI from waiting on each
/// other (plan 3).
/// </summary>
internal sealed partial class AgentCliSession : IAsyncDisposable
{
    private readonly IAgentCliProcess _process;
    private readonly IAgentCliEngine _engine;
    private readonly AgentCliOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly List<AgentCliMessageKey> _delivered = [];
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private AgentCliBridge? _bridge;
    private int _busy;
    private int _closed;
    private int _interrupts;

    public AgentCliSession(IAgentCliProcess process, IAgentCliEngine engine, AgentCliOptions options, string fingerprint, ILogger logger, TimeProvider? time = null)
    {
        _process = process;
        _engine = engine;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        Fingerprint = fingerprint;
    }

    /// <summary>The model, system prompt and first message this session was opened for.</summary>
    public string Fingerprint { get; }

    /// <summary>
    /// True once the CLI holds a conversation Orkeon does not (an interrupted turn, its own compaction, a
    /// failure): no call may continue it.
    /// </summary>
    public bool Diverged { get; private set; }

    /// <summary>True once the process has been ended.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    /// <summary>True while a call runs on the session.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>When the last call ended, for the eviction of idle sessions.</summary>
    public DateTimeOffset LastUsed { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The tool use ids the CLI waits on.</summary>
    public IReadOnlyCollection<string> PendingToolCalls => _pending;

    /// <summary>Takes the session for one call; false when another call holds it.</summary>
    public bool TryAcquire() => !IsClosed && Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    /// <summary>Gives the session back after a call.</summary>
    public void Release()
    {
        LastUsed = DateTimeOffset.UtcNow;
        Volatile.Write(ref _busy, 0);
    }

    /// <summary>Gives the session back untouched, when the caller that took it has no call for it.</summary>
    public void Abandon() => Volatile.Write(ref _busy, 0);

    /// <summary>
    /// Whether the call whose history reads <paramref name="keys"/> continues this session: it starts with what
    /// was delivered, then answers every pending tool call and nothing else of the CLI's, then adds what is new.
    /// </summary>
    public bool Continues(IReadOnlyList<AgentCliMessageKey> keys)
    {
        if (Diverged || IsClosed || _delivered.Count == 0 || keys.Count <= _delivered.Count)
            return false;

        for (var index = 0; index < _delivered.Count; index++)
        {
            if (!Same(_delivered[index], keys[index]))
                return false;
        }

        var answered = new HashSet<string>(StringComparer.Ordinal);
        var userSeen = false;
        foreach (var key in keys.Skip(_delivered.Count))
        {
            switch (key.Role)
            {
                case "tool":
                    // A result after a new user message, or for a call the CLI did not make, is not this session's.
                    if (userSeen || !_pending.Contains(key.ToolIds) || !answered.Add(key.ToolIds))
                        return false;
                    break;
                case "assistant":
                    return false;
                default:
                    userSeen = true;
                    break;
            }
        }

        return answered.Count == _pending.Count;
    }

    private static bool Same(AgentCliMessageKey delivered, AgentCliMessageKey sent) =>
        delivered.Role == sent.Role
        && string.Equals(delivered.ToolIds, sent.ToolIds, StringComparison.Ordinal)
        && string.Equals(delivered.Text.Trim(), sent.Text.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Runs one model message for <paramref name="history"/>: the seed on a new session, else the tail after what
    /// was delivered (<see cref="Continues"/> said yes). Returns the message, or the failure the CLI reported.
    /// The call runs under <paramref name="bounds"/>: an elapsed bound interrupts the turn, leaves the session
    /// diverged as a cancellation does, and returns the failure the HTTP providers report for it.
    /// </summary>
    public async Task<AgentCliTurn> RunAsync(
        IReadOnlyList<LlmMessage> history, IReadOnlyList<AgentCliMessageKey> keys, IReadOnlyList<ToolSchema> tools,
        AgentCliCallBounds bounds, CancellationToken caller)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(bounds.TimeoutSeconds), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        var cancellationToken = linked.Token;
        try
        {
            var bridge = await BridgeAsync(tools, cancellationToken).ConfigureAwait(false);
            await bridge.SetToolsAsync(tools, cancellationToken).ConfigureAwait(false);

            if (_delivered.Count == 0)
            {
                await _process.WriteLineAsync(_engine.UserLine(AgentCliHistory.Seed(history)), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var userLines = new List<string>();
                foreach (var message in history.Skip(_delivered.Count))
                {
                    switch (AgentCliHistory.RoleOf(message))
                    {
                        case "tool":
                            await bridge.ResolveAsync(message.ToolCallId ?? "", message.Content ?? "", IsRefusal(message.Content), cancellationToken).ConfigureAwait(false);
                            _pending.Remove(message.ToolCallId ?? "");
                            break;
                        case "system":
                            userLines.Add(AgentCliHistory.SystemNotePrefix + message.Content);
                            break;
                        default:
                            userLines.Add(message.Content ?? "");
                            break;
                    }
                }

                // A user message after tool results is written once the calls are answered (plan 4.4, probe 22).
                foreach (var line in userLines)
                    await _process.WriteLineAsync(_engine.UserLine(line), cancellationToken).ConfigureAwait(false);
            }

            _delivered.Clear();
            _delivered.AddRange(keys);

            var turn = await ReadMessageAsync(bounds, cancellationToken).ConfigureAwait(false);
            if (turn.Error is not null)
            {
                Diverged = true;
                if (turn.ErrorType is AgentCliTurn.StreamIdleErrorType)
                    await TryInterruptAsync().ConfigureAwait(false);
                return turn;
            }

            _delivered.Add(new AgentCliMessageKey("assistant", turn.Text, string.Join(',', turn.ToolCalls.Select(call => call.Id))));
            foreach (var call in turn.ToolCalls)
                _pending.Add(call.Id);
            bridge.Expect(turn.ToolCalls.Select(call => (call.Id, call.Name)));
            return turn;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !caller.IsCancellationRequested)
        {
            // Llm:TimeoutSeconds elapsed: closed as a cancellation is, and reported as the HTTP providers report it.
            Diverged = true;
            await TryInterruptAsync().ConfigureAwait(false);
            return AgentCliTurn.Failed(
                $"The agent CLI did not answer within Llm:TimeoutSeconds = {bounds.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)} s: "
                + "the turn was interrupted and its process closed, so this is a failed call, not an empty answer. A long answer, "
                + "or a model that thinks before it writes, needs longer - raise Llm:TimeoutSeconds (600 s is a safe value).",
                null, AgentCliTurn.TimeoutErrorType);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            // The CLI keeps lines Orkeon never received (plan 4.8): the session cannot be continued.
            Diverged = true;
            await TryInterruptAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            Diverged = true;
            throw;
        }
    }

    /// <summary>
    /// A tool result Orkeon refused or that failed, returned to the model as an error (probe 7). The two loops
    /// mark it in the text alone: <c>act</c> writes <c>DENIED: ...</c>, the crew's chat loop <c>Error: ...</c>
    /// (the permission gate, the Guardian, an unknown tool, a tool that threw).
    /// </summary>
    private static bool IsRefusal(string? content)
    {
        var text = content?.TrimStart();
        return text is not null
            && (text.StartsWith("DENIED", StringComparison.Ordinal) || text.StartsWith("Error:", StringComparison.Ordinal));
    }

    /// <summary>How long the CLI has to start the relay and reach the host's socket.</summary>
    internal static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(60);

    private async Task<AgentCliBridge> BridgeAsync(IReadOnlyList<ToolSchema> tools, CancellationToken cancellationToken)
    {
        if (_bridge is { } bridge)
            return bridge;

        IAgentCliLineChannel channel;
        try
        {
            channel = await _process.Bridge.WaitAsync(BridgeTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (System.TimeoutException)
        {
            // The CLI waits on its input whether or not its MCP server started: without a bound, a relay
            // that cannot start (no dotnet to run it, a binary of another platform) hangs the call.
            var tail = _process.StandardErrorTail;
            throw new InvalidOperationException(
                $"The agent CLI did not connect to Orkeon's tool bridge within {BridgeTimeout.TotalSeconds:0} seconds: its relay did not start"
                + (string.IsNullOrWhiteSpace(tail) ? "." : $": {tail}"));
        }

        _bridge = new AgentCliBridge(channel, _options.MaxToolResultCharacters, _logger);
        await _bridge.SetToolsAsync(tools, cancellationToken).ConfigureAwait(false);
        return _bridge;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best effort: the session is closed after an interrupt whatever the CLI does with it.")]
    private async Task TryInterruptAsync()
    {
        try
        {
            var id = "orkeon-interrupt-" + Interlocked.Increment(ref _interrupts).ToString(CultureInfo.InvariantCulture);
            await _process.WriteLineAsync(_engine.InterruptLine(id), CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            // The process is killed with the session.
        }
    }

    /// <summary>Reads the stream to the end of one assistant message that calls tools, or to the end of the turn.</summary>
    private async Task<AgentCliTurn> ReadMessageAsync(AgentCliCallBounds bounds, CancellationToken cancellationToken)
    {
        var idle = bounds.StreamIdleSeconds is { } idleSeconds ? TimeSpan.FromSeconds(idleSeconds) : (TimeSpan?)null;
        var text = new StringBuilder();
        var toolCalls = new List<AgentCliToolCall>();
        var blocks = new JsonArray();
        JsonObject? usage = null;
        string? stopReason = null;
        string? model = null;
        string? messageId = null;

        while (true)
        {
            string line;
            try
            {
                var read = _process.Incoming.ReadAsync(cancellationToken);
                // Llm:StreamIdleSeconds bounds the silence between two lines; the read left pending is the
                // session's, which the caller closes with it.
                line = idle is { } silence && !read.IsCompleted
                    ? await read.AsTask().WaitAsync(silence, _time, cancellationToken).ConfigureAwait(false)
                    : await read.ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                return AgentCliTurn.Failed(EndedMessage(), null);
            }
            catch (System.TimeoutException)
            {
                return AgentCliTurn.Failed(
                    $"The agent CLI wrote nothing to its stream for Llm:StreamIdleSeconds = {bounds.StreamIdleSeconds?.ToString(CultureInfo.InvariantCulture)} s, "
                    + "so this is a failed call, not an empty answer: the turn was interrupted and its process closed. A model that "
                    + "thinks before it writes may stay silent for a while - raise Llm:StreamIdleSeconds, or leave it unset to bound "
                    + "the call by Llm:TimeoutSeconds alone.",
                    null, AgentCliTurn.StreamIdleErrorType);
            }

            if (_engine.ReadEvent(line) is not { } ev)
                continue;

            switch (ev.Kind)
            {
                case AgentCliEventKind.Init:
                    model ??= ev.Model;
                    if (_options.RequireLogin && ev.ApiKeySource is { Length: > 0 } source && !string.Equals(source, "none", StringComparison.Ordinal))
                        return AgentCliTurn.Failed(
                            $"The agent CLI runs on '{source}' rather than its login: sign in with 'claude /login', remove that credential from the CLI's settings, or set Llm:AgentCli:RequireLogin to false.", 401);
                    break;

                case AgentCliEventKind.AssistantBlock:
                    messageId = ev.MessageId ?? messageId;
                    model = ev.Model ?? model;
                    // The usage of an assistant line is the start of the message's (probe 3): message_delta updates it.
                    usage ??= ev.Usage;
                    foreach (var block in ev.Blocks ?? [])
                    {
                        if (block is not JsonObject item)
                            continue;
                        blocks.Add(item.DeepClone());
                        if (ReadBlock(item, text, toolCalls) is { } malformed)
                            return AgentCliTurn.Failed(malformed, null);
                    }
                    break;

                case AgentCliEventKind.MessageDelta:
                    // The counts the delta carries replace the start's; a count it leaves out keeps the start's.
                    usage = MergeUsage(usage, ev.Usage);
                    stopReason = ev.StopReason ?? stopReason;
                    break;

                case AgentCliEventKind.MessageStop:
                    if (toolCalls.Count > 0)
                        return new AgentCliTurn(text.ToString(), toolCalls, usage, stopReason ?? "tool_use", model, messageId, null, null, null);
                    break;

                case AgentCliEventKind.CompactBoundary:
                    // The CLI summarised the conversation with its own prompt (probes 29, 30): Orkeon's history is no
                    // longer what it holds. This call ends normally; the next one seeds a new session.
                    Diverged = true;
                    LogCompacted();
                    break;

                case AgentCliEventKind.RateLimit when string.Equals(ev.RateLimitStatus, "rejected", StringComparison.Ordinal):
                    Diverged = true;
                    var wait = ev.ResetsAt is { } resets ? resets - DateTimeOffset.UtcNow : TimeSpan.Zero;
                    throw new LlmRateLimitException(
                        $"The agent CLI's subscription limit ({ev.RateLimitType ?? "unknown"}) is reached"
                        + (ev.ResetsAt is { } at ? $"; it resets at {at:u}." : "."),
                        wait > TimeSpan.Zero ? wait : TimeSpan.Zero);

                case AgentCliEventKind.Result:
                    if (ev.IsError)
                        return AgentCliTurn.Failed(
                            $"The agent CLI's turn failed ({ev.Subtype}{(ev.ApiErrorStatus is { } status ? $", HTTP {status}" : "")}): {ev.Text}",
                            ev.ApiErrorStatus);
                    var answer = text.Length > 0 ? text.ToString() : ev.Text ?? "";
                    return new AgentCliTurn(answer, [], usage ?? ev.Usage, stopReason ?? "end_turn", model, messageId, null, null, ev.NotionalCostUsd);
            }
        }
    }

    /// <summary>
    /// Reads one content block into the answer; returns why the stream is refused, or null. A block whose
    /// fields are not strings is the CLI's to explain, never an exception out of the call.
    /// </summary>
    private string? ReadBlock(JsonObject block, StringBuilder text, List<AgentCliToolCall> toolCalls)
    {
        var type = StringOf(block, "type");
        if (type == "text" && StringOf(block, "text") is { } fragment)
        {
            text.Append(fragment);
        }
        else if (type == "tool_use")
        {
            if (StringOf(block, "id") is not { Length: > 0 } id || StringOf(block, "name") is not { Length: > 0 } name)
                return "The agent CLI wrote a tool_use block without a string id and name: its stream is not one the agent-cli provider reads.";
            if (name.StartsWith(_engine.ToolPrefix, StringComparison.Ordinal))
                name = name[_engine.ToolPrefix.Length..];
            toolCalls.Add(new AgentCliToolCall(id, name, block["input"]?.DeepClone() as JsonObject ?? []));
        }

        return null;
    }

    private static string? StringOf(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The message's usage: the start's counts, each one the delta carries replacing it.</summary>
    internal static JsonObject? MergeUsage(JsonObject? start, JsonObject? delta)
    {
        if (delta is null || start is null)
            return delta ?? start;
        var merged = (JsonObject)start.DeepClone();
        foreach (var (name, value) in delta)
            merged[name] = value?.DeepClone();
        return merged;
    }

    private string EndedMessage()
    {
        var tail = _process.StandardErrorTail;
        return $"The agent CLI ended (exit code {_process.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"})"
            + (string.IsNullOrWhiteSpace(tail) ? "." : $": {tail}");
    }

    /// <summary>Ends the process; idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;
        Diverged = true;
        try
        {
            if (_bridge is not null)
                await _bridge.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _process.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 30, Level = LogLevel.Warning,
        Message = "The agent CLI compacted the conversation itself: its history is no longer Orkeon's, and the next call starts a new session seeded with Orkeon's history. Raise Llm:AgentCli:AutocompactTokens, or let Orkeon compact sooner.")]
    private partial void LogCompacted();
}

/// <summary>One tool call of an assistant message, its name without the bridge's prefix.</summary>
internal sealed record AgentCliToolCall(string Id, string Name, JsonObject Input);

/// <summary>What one call read: an assistant message, or the failure the CLI reported.</summary>
internal sealed record AgentCliTurn(
    string Text,
    IReadOnlyList<AgentCliToolCall> ToolCalls,
    JsonObject? Usage,
    string? StopReason,
    string? Model,
    string? MessageId,
    string? Error,
    int? ApiErrorStatus,
    decimal? NotionalCostUsd,
    string? ErrorType = null)
{
    /// <summary>The <c>error_type</c> of an elapsed <c>Llm:TimeoutSeconds</c>, as the HTTP providers write it.</summary>
    public const string TimeoutErrorType = "TaskCanceledException";

    /// <summary>The <c>error_type</c> of an elapsed <c>Llm:StreamIdleSeconds</c>, as the HTTP providers write it.</summary>
    public const string StreamIdleErrorType = "StreamIdleTimeout";

    public static AgentCliTurn Failed(string error, int? status, string? errorType = null) =>
        new("", [], null, null, null, null, error, status, null, errorType);
}

/// <summary>
/// The bounds of one call: <c>Llm:TimeoutSeconds</c> over the whole call, <c>Llm:StreamIdleSeconds</c> (when set)
/// over the silence between two lines of the CLI's stream.
/// </summary>
internal readonly record struct AgentCliCallBounds(int TimeoutSeconds, int? StreamIdleSeconds);
