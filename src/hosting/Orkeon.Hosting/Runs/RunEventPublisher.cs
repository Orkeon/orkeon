using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Runs;

namespace Orkeon.Hosting.Runs;

/// <summary>
/// The one sink the observers of a run write to: it stamps each event once (sequence, time, run identity) and fans
/// it out to every destination, so the stdout stream and the history hold the same lines. A destination that fails
/// is logged and skipped: observing a run must not change its outcome, and one broken sink must not silence the others.
/// <para>
/// A publisher serves one run. The run identity is the explicit <c>runId</c> when given, else the ambient
/// <see cref="IRunScope"/> (the id the checkpoint session and the A2A and API task carry, FWK-01); an event the emitter
/// gave no correlation id carries it too.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The semaphore is never waited on through a handle, so it holds no native resource; the publisher lives as long as its run.")]
internal sealed partial class RunEventPublisher : IRunEventSink
{
    private const int EarlyCapacity = 128;

    private readonly List<IRunEventSink> _sinks;
    private readonly TimeProvider _time;
    private readonly IRunScope? _runScope;
    private readonly string? _runId;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RunEvent>? _early = [];
    private int _sequence;
    private int _droppedEarly;

    /// <summary>Builds the publisher over its first destinations.</summary>
    /// <param name="sinks">The destinations, in write order.</param>
    /// <param name="time">The clock that stamps events.</param>
    /// <param name="runScope">The ambient run identity; null when the events belong to no ambient run.</param>
    /// <param name="runId">The run this publisher serves when no ambient scope names it (a host that mints its own id).</param>
    /// <param name="logger">Where a failing destination is reported.</param>
    public RunEventPublisher(
        IEnumerable<IRunEventSink> sinks,
        TimeProvider time,
        IRunScope? runScope = null,
        string? runId = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        _sinks = [.. sinks];
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _runScope = runScope;
        _runId = runId;
        _logger = logger;
    }

    /// <summary>How many early events were not kept for a late destination because the buffer was full.</summary>
    internal int DroppedEarly => _droppedEarly;

    /// <summary>How many early events are kept for a late destination.</summary>
    internal int EarlyCount => _early?.Count ?? 0;

    /// <summary>The run identity this publisher stamps now, or null when none is known.</summary>
    public string? CurrentRunId => _runId ?? _runScope?.Current?.ExecutionId.ToString();

    /// <inheritdoc />
    public async System.Threading.Tasks.Task WriteAsync(RunEvent runEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var runId = runEvent.RunId ?? CurrentRunId;
            var stamped = runEvent with
            {
                Seq = ++_sequence,
                Timestamp = _time.GetUtcNow(),
                RunId = runId,
                CorrelationId = runEvent.CorrelationId ?? runId,
            };

            if (_early is not null)
            {
                if (_early.Count < EarlyCapacity)
                    _early.Add(stamped);
                else
                    _droppedEarly++;
            }

            foreach (var sink in _sinks)
                await DeliverAsync(sink, stamped, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adds a destination that became known after the first events were written (the history, whose settings are
    /// read when the host is built, after <c>run.started</c>): the events so far are replayed to it first, in order.
    /// </summary>
    public async System.Threading.Tasks.Task AttachAsync(IRunEventSink sink, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var past in _early ?? [])
                await DeliverAsync(sink, past, ct).ConfigureAwait(false);

            if (_droppedEarly > 0 && _logger is not null)
                LogEarlyDropped(_logger, _droppedEarly, EarlyCapacity);

            _sinks.Add(sink);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops keeping the first events for late destinations: every destination is attached from now on.</summary>
    public void CompleteConfiguration()
    {
        _gate.Wait();
        try
        {
            _early = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A destination that fails must not break the run it observes, nor the other destinations.")]
    private async System.Threading.Tasks.Task DeliverAsync(IRunEventSink sink, RunEvent runEvent, CancellationToken ct)
    {
        try
        {
            await sink.WriteAsync(runEvent, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_logger is not null)
                LogSinkFailed(_logger, ex, runEvent.Kind, runEvent.Seq);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dropped} events of the run were written before the history could join and are not in it (the buffer keeps the first {Capacity}); the stream they went to has them.")]
    private static partial void LogEarlyDropped(ILogger logger, int dropped, int capacity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A run event sink failed on {Kind} (seq {Seq}); the run goes on.")]
    private static partial void LogSinkFailed(ILogger logger, Exception exception, string kind, int seq);
}
