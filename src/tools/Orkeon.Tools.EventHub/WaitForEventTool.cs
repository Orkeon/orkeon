using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>
/// Agent tool that waits for the next message on a topic.
/// Requires exactly one of <c>timeout_ms</c> / <c>wait_forever:true</c>.
/// </summary>
[ToolContract("wait_for_event",
    Name = "wait_for_event",
    Description = "Wait for the next event on a topic. Requires exactly one of timeout_ms / wait_forever.",
    Category = "EventHub")]
public sealed class WaitForEventTool : ToolBase<WaitForEventRequest, WaitForEventResponse>, ITool
{
    private readonly IEventHub _hub;

    /// <summary>Initializes a new instance of <see cref="WaitForEventTool"/>.</summary>
    public WaitForEventTool(IEventHub hub, ILogger<WaitForEventTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(WaitForEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Topic))
            return "topic is required";

        var hasTimeout = request.TimeoutMs is not null;
        if (hasTimeout && request.WaitForever)
            return "Exactly one of timeout_ms / wait_forever must be set, not both.";
        if (!hasTimeout && !request.WaitForever)
            return "Exactly one of timeout_ms / wait_forever must be set.";
        if (hasTimeout && request.TimeoutMs <= 0)
            return "timeout_ms must be strictly positive.";
        return null;
    }

    /// <inheritdoc/>
    protected override Task<WaitForEventResponse> ExecuteTypedAsync(
        WaitForEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<WaitForEventResponse> ExecuteTypedCoreAsync()
        {
            WaitTimeout timeout = request.WaitForever
                ? ForeverWaitTimeout.Instance
                : FiniteWaitTimeout.Of(TimeSpan.FromMilliseconds(request.TimeoutMs!.Value));

            var descriptor = new WaitOnTopic(request.Topic, request.MetadataMatch);

            var message = await _hub
                .WaitForAsync(descriptor, timeout, cancellationToken)
                .ConfigureAwait(false);

            if (EventHubToolHelpers.IsWaitTimedOut(message))
                return new WaitForEventResponse { Message = null, TimedOut = true };

            return new WaitForEventResponse
            {
                Message = EventHubToolHelpers.ToEnvelope(message),
                TimedOut = false
            };
        }
    }
}
