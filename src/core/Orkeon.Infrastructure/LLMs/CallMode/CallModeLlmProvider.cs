using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.CallMode;

/// <summary>Decides the wire mode of every call of the provider it wraps.</summary>
internal sealed partial class CallModeLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly ILogger _logger;
    private readonly string _settingKey = Guid.NewGuid().ToString("N");
    private volatile int _lastWireMode = -1;
    private volatile bool _fellBack;
    private readonly ILlmCallObserver _observer;
    private readonly ITokenCounter _tokenCounter;

    public CallModeLlmProvider(ILlmProvider inner, LlmCallMode mode, ILogger logger)
        : this(inner, mode, logger, NullLlmCallObserver.Instance, new SimpleTokenCounter())
    {
    }

    /// <summary>The observed form: every chat call is announced, followed and closed on <paramref name="observer"/>.</summary>
    public CallModeLlmProvider(ILlmProvider inner, LlmCallMode mode, ILogger logger, ILlmCallObserver observer, ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(tokenCounter);
        _inner = inner;
        _logger = logger;
        _observer = observer;
        _tokenCounter = tokenCounter;
        Mode = mode;
    }

    public LlmCallMode Mode { get; }

    /// <summary>The provider under the decorator, for type inspection only.</summary>
    public ILlmProvider Inner => _inner;

    public LlmWireMode? LastWireMode => _lastWireMode < 0 ? null : (LlmWireMode)_lastWireMode;

    /// <summary>The decorator under the limiter and the meter of <paramref name="provider"/>, or null.</summary>
    public static CallModeLlmProvider? Of(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return MeteredLlmProvider.UnderMeter(provider) as CallModeLlmProvider;
    }

    public string Name => _inner.Name;

    public LlmConfig? BaseConfig => _inner.BaseConfig;

    public LlmProviderCapabilities Capabilities => _inner.Capabilities;

    public bool SupportsStreaming => (_inner as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    public bool StreamsNatively => StreamsOnTheWire;

    private bool InnerStreamsNatively =>
        _inner is IStreamingLlmProvider { SupportsStreaming: true, StreamsNatively: true };

    private bool StreamsOnTheWire => Mode != LlmCallMode.Buffer && InnerStreamsNatively;

    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        _inner.GenerateAsync(prompt, config, cancellationToken);

    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var call = new ObservedCall(this, messages);
        try
        {
            return call.Close(await ChatCoreAsync(messages, config, call, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            call.Finish();
        }
    }

    private async Task<LlmResponse> ChatCoreAsync(
        LlmMessage[] messages, LlmConfig? config, ObservedCall call, CancellationToken cancellationToken)
    {
        var wire = Decide();
        if (wire != LlmWireMode.Stream)
        {
            call.Wire(wire);
            return await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        }

        call.Wire(LlmWireMode.Stream);
        var started = false;
        try
        {
            await foreach (var ev in ((IStreamingLlmProvider)_inner).ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
            {
                started = true;
                call.Observe(ev);
                if (ev.Kind == LlmStreamEventKind.Completed && ev.FinalResponse is { } final)
                    return final;
            }
        }
        catch (Exception ex) when (!started && IsStreamingRefusal(ex))
        {
            if (Mode != LlmCallMode.Auto)
                throw Refused(ex);
            RecordFallback();
            call.Wire(LlmWireMode.BufferFallback);
            return await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        }

        throw new HttpRequestException($"The stream of '{_inner.Name}' ended without a final response.");
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!StreamsOnTheWire)
        {
            _lastWireMode = (int)LlmWireMode.Buffer;
            var response = await _inner.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(response.Content))
                yield return response.Content;
            else if (BufferedFallback.Refusal(response) is { } refusal)
                throw refusal;
            yield break;
        }

        _lastWireMode = (int)LlmWireMode.Stream;
        await foreach (var chunk in ((IStreamingLlmProvider)_inner).GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = new ObservedCall(this, messages);
        try
        {
            await foreach (var ev in ChatStreamingCoreAsync(messages, config, call, cancellationToken).ConfigureAwait(false))
            {
                call.Observe(ev);
                yield return ev;
            }
        }
        finally
        {
            call.Finish();
        }
    }

    private async IAsyncEnumerable<LlmStreamEvent> ChatStreamingCoreAsync(
        LlmMessage[] messages, LlmConfig? config, ObservedCall call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Mode == LlmCallMode.Buffer || _inner is not IStreamingLlmProvider streaming)
        {
            call.Wire(LlmWireMode.Buffer);
            await foreach (var ev in BufferedEvents(messages, config, cancellationToken).ConfigureAwait(false))
                yield return ev;
            yield break;
        }

        var wire = streaming.StreamsNatively ? Decide() : LlmWireMode.Buffer;
        if (wire == LlmWireMode.BufferFallback)
        {
            call.Wire(wire);
            await foreach (var ev in BufferedEvents(messages, config, cancellationToken).ConfigureAwait(false))
                yield return ev;
            yield break;
        }

        call.Wire(wire);
        var events = streaming.ChatStreamingAsync(messages, config, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var disposal = events.ConfigureAwait(false);
        var started = false;
        while (true)
        {
            bool more;
            var refused = false;
            Exception? refusal = null;
            try
            {
                more = await events.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (!started && wire == LlmWireMode.Stream && IsStreamingRefusal(ex))
            {
                more = false;
                refused = true;
                refusal = ex;
            }

            if (refused)
            {
                if (Mode != LlmCallMode.Auto)
                    throw Refused(refusal!);
                RecordFallback();
                call.Wire(LlmWireMode.BufferFallback);
                await foreach (var ev in BufferedEvents(messages, config, cancellationToken).ConfigureAwait(false))
                    yield return ev;
                yield break;
            }

            if (!more)
                yield break;
            started = true;
            yield return events.Current;
        }
    }

    private async IAsyncEnumerable<LlmStreamEvent> BufferedEvents(
        LlmMessage[] messages, LlmConfig? config, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await _inner.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
            yield return new LlmStreamEvent { Kind = LlmStreamEventKind.ContentDelta, Delta = response.Content };
        yield return new LlmStreamEvent { Kind = LlmStreamEventKind.Completed, FinalResponse = response };
    }

    /// <summary>Buffer, BufferFallback (Auto, already fallen back or no streaming declared) or Stream.</summary>
    private LlmWireMode Decide()
    {
        if (Mode == LlmCallMode.Buffer || _inner is not IStreamingLlmProvider { StreamsNatively: true } streaming)
            return LlmWireMode.Buffer;
        if (!streaming.SupportsStreaming)
        {
            if (Mode != LlmCallMode.Auto)
                return LlmWireMode.Buffer;
            RecordFallback();
            return LlmWireMode.BufferFallback;
        }

        return Mode == LlmCallMode.Auto && HasFallenBack() ? LlmWireMode.BufferFallback : LlmWireMode.Stream;
    }

    private bool HasFallenBack() => LlmCallModeScope.IsOpen ? LlmCallModeScope.HasFallenBack(_settingKey) : _fellBack;

    private void RecordFallback()
    {
        if (HasFallenBack())
            return;
        if (LlmCallModeScope.IsOpen)
            LlmCallModeScope.RecordFallback(_settingKey);
        else
            _fellBack = true;
        LogFallback(_inner.Name);
    }

    private InvalidOperationException Refused(Exception cause) =>
        new($"The provider '{_inner.Name}' refused streaming. Set the call mode of this setting to Auto or Buffer.", cause);

    /// <summary>A clear refusal of streaming: 404, 405, 415, 501, or 400/422 naming the stream.</summary>
    internal static bool IsStreamingRefusal(Exception ex) =>
        ex is HttpRequestException { StatusCode: { } status } http
        && ((int)status is 404 or 405 or 415 or 501
            || ((int)status is 400 or 422 && http.Message.Contains("stream", StringComparison.OrdinalIgnoreCase)));

    /// <summary>One observed chat call: announces itself when its wire mode is known, follows the deltas, closes once.</summary>
    private sealed class ObservedCall(CallModeLlmProvider owner, LlmMessage[] messages)
    {
        private readonly string _id = Guid.NewGuid().ToString("N");
        private int _receivedTokens;
        private bool _announced;
        private bool _closed;
        private bool _fellBack;
        private LlmCallMode _mode = LlmCallMode.Buffer;
        private LlmResponse? _response;

        public void Wire(LlmWireMode wire)
        {
            owner._lastWireMode = (int)wire;
            _mode = wire == LlmWireMode.Stream ? LlmCallMode.Stream : LlmCallMode.Buffer;
            _fellBack |= wire == LlmWireMode.BufferFallback;
            if (_announced)
                return;
            _announced = true;
            var prompt = messages.Sum(m => owner._tokenCounter.CountTokens(m.Content ?? string.Empty));
            owner._observer.Started(_id, _mode, prompt);
        }

        public void Observe(LlmStreamEvent ev)
        {
            if (ev.Kind == LlmStreamEventKind.Completed && ev.FinalResponse is { } final)
            {
                _response = final;
                return;
            }

            if (ev.Kind != LlmStreamEventKind.ContentDelta || string.IsNullOrEmpty(ev.Delta) || _mode != LlmCallMode.Stream)
                return;
            _receivedTokens += owner._tokenCounter.CountTokens(ev.Delta);
            owner._observer.Progress(_id, _receivedTokens);
        }

        public LlmResponse Close(LlmResponse response)
        {
            _response = response;
            return response;
        }

        public void Finish()
        {
            if (!_announced || _closed)
                return;
            _closed = true;
            owner._observer.Finished(_id, _mode, _fellBack, _response?.PromptTokens, _response?.CompletionTokens);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider {Provider} refused streaming: the call falls back to Buffer for the rest of the run")]
    private partial void LogFallback(string provider);
}
