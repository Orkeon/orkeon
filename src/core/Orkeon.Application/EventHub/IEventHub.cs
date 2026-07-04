using Orkeon.Domain.Common;

namespace Orkeon.Application.EventHub;

/// <summary>
/// Application port for the unified inter-agent / inter-crew messaging primitive.
/// See <c>docs/architecture/event-hub-and-crew-lifecycle.md</c> §3.1.
/// </summary>
public interface IEventHub
{
    /// <summary>Broadcast 1→N, topic-based, anonymous. Optional crew scope via <see cref="PublishOptions.TargetCrewId"/>.</summary>
    System.Threading.Tasks.Task PublishAsync(
        string topic,
        object payload,
        PublishOptions? options,
        CancellationToken ct);

    /// <summary>Fire-and-forget 1→1 to an addressed mailbox.</summary>
    System.Threading.Tasks.Task PostAsync(
        MailboxAddress recipient,
        object payload,
        CancellationToken ct);

    /// <summary>Request-response 1→1. Timeout is mandatory — <c>ForeverWaitTimeout</c> is not allowed here.</summary>
    Task<TResponse> SendAsync<TRequest, TResponse>(
        MailboxAddress recipient,
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct);

    /// <summary>Replies to a previously issued <c>Send</c>, correlated by <paramref name="correlation"/>.</summary>
    System.Threading.Tasks.Task ReplyAsync(
        CorrelationId correlation,
        object payload,
        CancellationToken ct);

    /// <summary>
    /// Long-lived subscription stream filtered by topic and the caller's crew scope.
    /// Completes (without exception) when <paramref name="ct"/> is cancelled.
    /// </summary>
    IAsyncEnumerable<Message> SubscribeAsync(
        string topic,
        CancellationToken ct);

    /// <summary>Short-term wait on a topic / mailbox / reply correlation, bounded by <paramref name="timeout"/>.</summary>
    Task<Message> WaitForAsync(
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        CancellationToken ct);

    /// <summary>Retrieves the last retained payload for <paramref name="key"/>, optionally scoped to <paramref name="crewScope"/>.</summary>
    Task<Message?> GetLastValueAsync(
        string key,
        CrewId? crewScope,
        CancellationToken ct);
}
