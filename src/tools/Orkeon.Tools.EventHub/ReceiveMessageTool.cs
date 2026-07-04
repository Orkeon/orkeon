using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>
/// Agent tool that pulls the next message from a mailbox (default: caller's own mailbox).
/// Requires exactly one of <c>timeout_ms</c> / <c>wait_forever:true</c>.
/// </summary>
[ToolContract("receive_message",
    Name = "receive_message",
    Description = "Pull the next message from a mailbox (default: current agent). Requires exactly one of timeout_ms / wait_forever.",
    Category = "EventHub")]
public sealed class ReceiveMessageTool : ToolBase<ReceiveMessageRequest, ReceiveMessageResponse>, ITool
{
    private readonly IEventHub _hub;
    private readonly IEventHubCallerContext _callerContext;

    /// <summary>Initializes a new instance of <see cref="ReceiveMessageTool"/>.</summary>
    public ReceiveMessageTool(
        IEventHub hub,
        IEventHubCallerContext callerContext,
        ILogger<ReceiveMessageTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(callerContext);
        _hub = hub;
        _callerContext = callerContext;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(ReceiveMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
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
    protected override Task<ReceiveMessageResponse> ExecuteTypedAsync(
        ReceiveMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<ReceiveMessageResponse> ExecuteTypedCoreAsync()
        {
            var mailbox = ResolveMailbox(request);
            WaitTimeout timeout = request.WaitForever
                ? ForeverWaitTimeout.Instance
                : FiniteWaitTimeout.Of(TimeSpan.FromMilliseconds(request.TimeoutMs!.Value));

            var message = await _hub
                .WaitForAsync(new WaitOnMailbox(mailbox), timeout, cancellationToken)
                .ConfigureAwait(false);

            if (EventHubToolHelpers.IsWaitTimedOut(message))
                return new ReceiveMessageResponse { Message = null, TimedOut = true };

            return new ReceiveMessageResponse
            {
                Message = EventHubToolHelpers.ToEnvelope(message),
                TimedOut = false
            };
        }
    }

    private MailboxAddress ResolveMailbox(ReceiveMessageRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Mailbox))
            return MailboxAddress.Parse(new Uri(request.Mailbox));

        var caller = _callerContext.Current;
        if (caller.AgentId is null)
            throw new InvalidOperationException(
                "receive_message has no explicit mailbox and the caller context exposes no AgentId. " +
                "Either pass mailbox=… or push a non-system caller via IEventHubCallerContext.Push().");

        var uri = new Uri($"agent://{caller.CrewId}/{caller.AgentId}");
        return MailboxAddress.Parse(uri);
    }
}
