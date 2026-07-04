using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>Agent tool that replies to a previously issued <c>send_request</c> by correlation id.</summary>
[ToolContract("reply_to",
    Name = "reply_to",
    Description = "Reply to a pending request identified by correlation_id.",
    Category = "EventHub")]
public sealed class ReplyToTool : ToolBase<ReplyToRequest, ReplyToResponse>, ITool
{
    private readonly IEventHub _hub;

    /// <summary>Initializes a new instance of <see cref="ReplyToTool"/>.</summary>
    public ReplyToTool(IEventHub hub, ILogger<ReplyToTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(ReplyToRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
            return "correlation_id is required";
        return null;
    }

    /// <inheritdoc/>
    protected override Task<ReplyToResponse> ExecuteTypedAsync(
        ReplyToRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<ReplyToResponse> ExecuteTypedCoreAsync()
        {
            var correlation = CorrelationId.From(request.CorrelationId);
            var repliedAt = DateTimeOffset.UtcNow;

            await _hub.ReplyAsync(
                    correlation,
                    EventHubToolHelpers.PayloadAsObject(request.Payload),
                    cancellationToken)
                .ConfigureAwait(false);

            return new ReplyToResponse
            {
                RepliedAt = repliedAt.ToString("O")
            };
        }
    }
}
