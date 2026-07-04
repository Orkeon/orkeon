using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>
/// Agent tool that issues a request and waits for a correlated reply.
/// <c>timeout_ms</c> is mandatory; <c>Forever</c> is forbidden (spec §9.3).
/// </summary>
[ToolContract("send_request",
    Name = "send_request",
    Description = "Send a request to a mailbox and wait for a correlated reply (timeout_ms required).",
    Category = "EventHub")]
public sealed class SendRequestTool : ToolBase<SendRequestRequest, SendRequestResponse>, ITool
{
    private readonly IEventHub _hub;

    /// <summary>Initializes a new instance of <see cref="SendRequestTool"/>.</summary>
    public SendRequestTool(IEventHub hub, ILogger<SendRequestTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(SendRequestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetMailbox))
            return "target_mailbox is required";
        if (request.TimeoutMs is null)
            return "timeout_ms is required for send_request (Forever is not allowed, spec §9.3)";
        if (request.TimeoutMs <= 0)
            return "timeout_ms must be strictly positive";
        return null;
    }

    /// <inheritdoc/>
    protected override Task<SendRequestResponse> ExecuteTypedAsync(
        SendRequestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SendRequestResponse> ExecuteTypedCoreAsync()
        {
            var mailbox = MailboxAddress.Parse(new Uri(request.TargetMailbox));
            var timeout = TimeSpan.FromMilliseconds(request.TimeoutMs!.Value);

            var responseNode = await _hub
                .SendAsync<object, JsonNode>(
                    mailbox,
                    EventHubToolHelpers.PayloadAsObject(request.Payload),
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);

            // No correlation id is exposed through the IEventHub.SendAsync surface; we surface a
            // generated one purely for downstream tracing. v1.0 has no use for it beyond the DTO.
            return new SendRequestResponse
            {
                CorrelationId = CorrelationId.NewId().AsString(),
                ResponsePayload = responseNode
            };
        }
    }
}
