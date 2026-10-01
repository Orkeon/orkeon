using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;

namespace Orkeon.Tools.EventHub;

/// <summary>Agent tool that fire-and-forgets a message to an addressed mailbox.</summary>
[ToolContract("post_message",
    Name = "post_message",
    Description = "Fire-and-forget a message to a mailbox (agent://, crew://, topic://, or client://).",
    Category = "EventHub")]
public sealed class PostMessageTool : ToolBase<PostMessageRequest, PostMessageResponse>
{
    private readonly IEventHub _hub;

    /// <summary>Initializes a new instance of <see cref="PostMessageTool"/>.</summary>
    public PostMessageTool(IEventHub hub, ILogger<PostMessageTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(PostMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetMailbox))
            return "target_mailbox is required";
        return null;
    }

    /// <inheritdoc/>
    protected override Task<PostMessageResponse> ExecuteTypedAsync(
        PostMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<PostMessageResponse> ExecuteTypedCoreAsync()
        {
            var mailbox = MailboxAddress.Parse(new Uri(request.TargetMailbox));
            var postedAt = DateTimeOffset.UtcNow;

            // The id is the hub's: the one the recipient reads on the message (GAP-11).
            var messageId = await _hub.PostAsync(
                    mailbox,
                    EventHubToolHelpers.PayloadAsObject(request.Payload),
                    new MailboxOptions { Metadata = request.Metadata },
                    cancellationToken)
                .ConfigureAwait(false);

            return new PostMessageResponse
            {
                MessageId = messageId.AsString(),
                PostedAt = postedAt.ToString("O")
            };
        }
    }
}
