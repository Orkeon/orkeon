using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.EventHub.Middleware;

/// <summary>
/// Structured logging of hub messages (HUB-02, spec §12 stage 1). Registered **outermost on
/// the publish path** on purpose: it must see what the ACL later rejects, which is precisely
/// the moment an operator needs a line in the log. On the *receive* path the chain runs in
/// reverse, so this stage sits innermost there — a message a receive stage refuses (an
/// idempotency duplicate) is dropped before this line would fire; the hub's own
/// <c>LogDuplicateDropped</c> covers that case at Debug level.
/// <para>
/// It never swallows: a middleware further in that refuses lets the exception through, and
/// this one logs the refusal on the way out before re-throwing.
/// </para>
/// </summary>
internal sealed partial class LoggingEventHubMiddleware : IEventHubMiddleware
{
    private readonly ILogger<LoggingEventHubMiddleware> _logger;

    /// <summary>Creates the stage over the host's logger.</summary>
    public LoggingEventHubMiddleware(ILogger<LoggingEventHubMiddleware> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct) =>
        RunAsync(message, nextHandler, "publish");

    /// <inheritdoc />
    public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct) =>
        RunAsync(message, nextHandler, "receive");

    private async Task<Message> RunAsync(Message message, Func<Message, Task<Message>> nextHandler, string path)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await nextHandler(message).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var id = message.Id.ToString();
                var elapsed = Elapsed(started);
                LogHandled(path, message.Topic, id, elapsed);
            }

            return result;
        }
        catch (Exception ex)
        {
            // Not a catch-and-continue: the message is logged as rejected and the decision
            // travels on to the caller untouched.
            var rejectedId = message.Id.ToString();
            var rejectedElapsed = Elapsed(started);
            LogRejected(ex, path, message.Topic, rejectedId, rejectedElapsed);
            throw;
        }
    }

    private static double Elapsed(long started) =>
        Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    [LoggerMessage(EventId = 9410, Level = LogLevel.Debug,
        Message = "EventHub {Path} '{Topic}' ({MessageId}) in {ElapsedMs:F1}ms")]
    private partial void LogHandled(string path, string topic, string messageId, double elapsedMs);

    [LoggerMessage(EventId = 9411, Level = LogLevel.Warning,
        Message = "EventHub {Path} '{Topic}' ({MessageId}) rejected after {ElapsedMs:F1}ms")]
    private partial void LogRejected(Exception ex, string path, string topic, string messageId, double elapsedMs);
}

/// <summary>
/// OpenTelemetry spans for hub messaging (HUB-02, spec §12 stage 2 and §14). OTel is already
/// mounted across the codebase — seven <c>ActivitySource</c>s, an exporter, a resource — so
/// this stage opens the hub's own source and sets the four attributes the spec names. It
/// wires nothing.
/// </summary>
internal sealed class TelemetryEventHubMiddleware : IEventHubMiddleware
{
    /// <inheritdoc />
    public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct) =>
        RunAsync(message, nextHandler, "publish");

    /// <inheritdoc />
    public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct) =>
        RunAsync(message, nextHandler, "receive");

    private static async Task<Message> RunAsync(Message message, Func<Message, Task<Message>> nextHandler, string pattern)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        using var activity = OrkeonDiagnostics.EventHubSource.StartActivity(
            $"eventhub.{pattern}", ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("event.topic", message.Topic);
            activity.SetTag("event.pattern", pattern);
            activity.SetTag("crew.source", message.SourceCrewId.ToString());
            if (message.TargetCrewId is { } target)
                activity.SetTag("crew.target", target.ToString());
        }

        try
        {
            return await nextHandler(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A refused message is a failed span, not a missing one — that is the whole
            // value of instrumenting the reject path, and it holds for every publish-side
            // refusal. A receive-side refusal such as an idempotency duplicate never reaches
            // this stage, because the receive chain runs in reverse and observability sits
            // innermost there.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
