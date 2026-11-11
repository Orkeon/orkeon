using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Web.Firefox.BiDi;

/// <summary>
/// A WebDriver BiDi connection: numbers each command, hands its answer back to the caller that sent it,
/// and turns an error answer into a <see cref="BiDiException"/>. An event the session subscribed to
/// goes to <see cref="EventReceived"/>. Commands may be sent concurrently.
/// </summary>
internal sealed partial class BiDiConnection : IAsyncDisposable
{
    private readonly IBiDiTransport _transport;
    private readonly TimeSpan _commandTimeout;
    private readonly ILogger? _logger;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _receiveLoop;
    private long _lastId;
    private volatile bool _closed;
    private int _disposed;

    /// <summary>Opens the connection on <paramref name="transport"/>; <paramref name="clock"/> times the command timeout (tests move it by hand).</summary>
    public BiDiConnection(IBiDiTransport transport, TimeSpan commandTimeout, ILogger? logger = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _commandTimeout = commandTimeout;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>
    /// Called with the method and the parameters of each event, on the receive loop: it must return at
    /// once and never wait for a command of this connection.
    /// </summary>
    public Action<string, JsonObject>? EventReceived { get; set; }

    /// <summary><c>true</c> once the browser closed the channel or the connection was disposed.</summary>
    public bool IsClosed => _closed;

    /// <summary>Sends <paramref name="method"/> and returns its <c>result</c>.</summary>
    /// <exception cref="BiDiException">The browser answered an error, did not answer in time, or the connection is closed.</exception>
    public async Task<JsonObject> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        if (_closed)
            throw new BiDiException(BiDiException.ConnectionClosed, $"'{method}' was not sent.");

        var id = Interlocked.Increment(ref _lastId);
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? [] };
            // The loop may have ended between the check above and the registration: nothing would answer.
            if (_closed)
                throw new BiDiException(BiDiException.ConnectionClosed, $"'{method}' was not sent.");
            try
            {
                await _transport.SendAsync(message.ToJsonString(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not BiDiException)
            {
                throw new BiDiException(BiDiException.ConnectionClosed, $"'{method}' could not be sent.", ex);
            }

            try
            {
                return await completion.Task.WaitAsync(_commandTimeout, _clock, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new BiDiException(
                    BiDiException.Timeout, $"'{method}' got no answer within {_commandTimeout.TotalSeconds:0.#} s.", ex);
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _closed = true;
        await _closing.CancelAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
        await _receiveLoop.ConfigureAwait(false);
        _closing.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        Exception? failure = null;
        try
        {
            while (await _transport.ReceiveAsync(_closing.Token).ConfigureAwait(false) is { } message)
                Dispatch(message);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Disposed: the pending commands end below.
        }
#pragma warning disable CA1031 // Whatever broke the channel, every pending command must end
        catch (Exception ex)
#pragma warning restore CA1031
        {
            failure = ex;
        }

        _closed = true;
        foreach (var id in _pending.Keys)
        {
            _pending.TryRemove(id, out var completion);
            completion?.TrySetException(new BiDiException(
                BiDiException.ConnectionClosed, "the browser closed the connection before answering.", failure));
        }
    }

    private void Dispatch(string message)
    {
        JsonObject? envelope;
        try
        {
            envelope = JsonNode.Parse(message) as JsonObject;
        }
        catch (JsonException ex)
        {
            if (_logger is not null) LogUnreadableMessage(_logger, ex);
            return;
        }

        if (envelope is null)
            return;

        // An event carries no id; an error may carry a null one: neither answers a command.
        if (envelope["id"] is not JsonValue idValue || !idValue.TryGetValue<long>(out var id))
        {
            if (EventReceived is { } handler
                && string.Equals(envelope["type"]?.GetValue<string>(), "event", StringComparison.Ordinal)
                && envelope["method"] is JsonValue method && method.TryGetValue<string>(out var name)
                && envelope["params"] is JsonObject parameters)
            {
                handler(name, parameters);
            }

            return;
        }

        if (!_pending.TryRemove(id, out var completion))
            return;

        if (string.Equals(envelope["type"]?.GetValue<string>(), "error", StringComparison.Ordinal))
        {
            completion.TrySetException(new BiDiException(
                envelope["error"]?.GetValue<string>() ?? "unknown error",
                envelope["message"]?.GetValue<string>() ?? string.Empty));
            return;
        }

        var result = envelope["result"] as JsonObject;
        if (result is not null) envelope.Remove("result");
        completion.TrySetResult(result ?? []);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A WebDriver BiDi message that is not JSON was dropped.")]
    private static partial void LogUnreadableMessage(ILogger logger, Exception exception);
}
