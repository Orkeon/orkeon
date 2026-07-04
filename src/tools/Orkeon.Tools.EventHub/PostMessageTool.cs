using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>Agent tool that fire-and-forgets a message to an addressed mailbox.</summary>
[ToolContract("post_message",
    Name = "post_message",
    Description = "Fire-and-forget a message to a mailbox (agent://, crew://, or topic://).",
    Category = "EventHub")]
public sealed class PostMessageTool : ToolBase<PostMessageRequest, PostMessageResponse>, ITool
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

            await _hub.PostAsync(
                    mailbox,
                    EventHubToolHelpers.PayloadAsObject(request.Payload),
                    cancellationToken)
                .ConfigureAwait(false);

            return new PostMessageResponse
            {
                MessageId = MessageId.NewId().AsString(),
                PostedAt = postedAt.ToString("O")
            };
        }
    }
}
