using Microsoft.Extensions.Logging;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.EventHub.Dtos;
using ITool = Orkeon.Domain.Common.ITool;

namespace Orkeon.Tools.EventHub;

/// <summary>
/// Agent tool that publishes an event on a topic. Rejects any attempt to publish on a topic
/// starting with <c>_system.</c> (reserved for hub-emitted messages, spec §5.1 / §9.2).
/// </summary>
[ToolContract("publish_event",
    Name = "publish_event",
    Description = "Publish an event on a topic (broadcast 1→N). Optional crew scope and metadata.",
    Category = "EventHub")]
public sealed class PublishEventTool : ToolBase<PublishEventRequest, PublishEventResponse>, ITool
{
    private readonly IEventHub _hub;

    /// <summary>Initializes a new instance of <see cref="PublishEventTool"/>.</summary>
    public PublishEventTool(IEventHub hub, ILogger<PublishEventTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    /// <inheritdoc/>
    protected override string? ValidateTypedRequest(PublishEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Topic))
            return "topic is required";
        if (request.RetainAsLastValue && string.IsNullOrWhiteSpace(request.LastValueKey))
            return "last_value_key is required when retain_as_last_value is true";
        return null;
    }

    /// <inheritdoc/>
    protected override Task<PublishEventResponse> ExecuteTypedAsync(
        PublishEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Topic.StartsWith(EventHubToolHelpers.SystemTopicPrefix, StringComparison.Ordinal))
            throw new ReservedTopicException(request.Topic);

        CrewId? targetCrewId = null;
        if (!string.IsNullOrWhiteSpace(request.TargetCrewId))
        {
            try { targetCrewId = CrewId.Parse(request.TargetCrewId); }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new ArgumentException($"target_crew_id '{request.TargetCrewId}' is not a valid CrewId.", nameof(request));
            }
        }

        var options = new PublishOptions
        {
            TargetCrewId = targetCrewId,
            Metadata = request.Metadata,
            RetainAsLastValue = request.RetainAsLastValue,
            LastValueKey = request.LastValueKey
        };

        return ExecuteCoreAsync(request, options, cancellationToken);
    }

    private async Task<PublishEventResponse> ExecuteCoreAsync(
        PublishEventRequest request, PublishOptions options, CancellationToken cancellationToken)
    {
        var eventId = MessageId.NewId();
        var publishedAt = DateTimeOffset.UtcNow;
        await _hub.PublishAsync(
                request.Topic,
                EventHubToolHelpers.PayloadAsObject(request.Payload),
                options,
                cancellationToken)
            .ConfigureAwait(false);

        return new PublishEventResponse
        {
            EventId = eventId.AsString(),
            PublishedAt = publishedAt.ToString("O")
        };
    }
}
