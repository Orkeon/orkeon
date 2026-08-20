using System.Collections.Concurrent;
using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;

namespace Orkeon.Infrastructure.EventHub.Middleware;

/// <summary>
/// Refuses a message a recipient has already consumed (HUB-04, spec §12).
/// <para>
/// **It only guards point-to-point delivery.** A message addressed to a mailbox has exactly one
/// legitimate reader, so "already processed" is a well-posed question. A topic message
/// legitimately reaches every subscriber, and deduplicating it by identifier would starve all
/// but the first — a bug that would look like a feature. Topic traffic therefore passes
/// untouched, deliberately.
/// </para>
/// <para>
/// **It does not survive the process.** rc.2's hub is in-memory — five dictionaries, a
/// correlation holding a <c>TaskCompletionSource</c> — so a durable ledger behind a volatile hub
/// would guard against a scenario the rest of the subsystem cannot cross anyway. A restart
/// clears the record and a replayed message would be processed again. That is a stated limit,
/// not an oversight; the port absorbs the change of mind the day a persistent hub exists.
/// </para>
/// </summary>
public sealed class IdempotencyEventHubMiddleware : IEventHubMiddleware
{
    /// <summary>How many identifiers are remembered before the oldest are forgotten.</summary>
    public const int DefaultCapacity = 10_000;

    private readonly int _capacity;
    private readonly ConcurrentDictionary<MessageId, byte> _seen = new();

    // The insertion order the dictionary cannot give us. Eviction is FIFO rather than LRU: a
    // duplicate arrives close behind its original or not at all, so recency of *use* carries no
    // information here, and a queue costs one enqueue per message instead of a reordering.
    private readonly ConcurrentQueue<MessageId> _order = new();

    /// <summary>Builds the stage with a bounded memory of consumed messages.</summary>
    /// <param name="capacity">
    /// How many identifiers to remember. Bounded on purpose: an unbounded set behind a
    /// long-running hub is a leak that only shows up in production.
    /// </param>
    public IdempotencyEventHubMiddleware(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <inheritdoc />
    public Task<Message> OnPublishAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        // Publishing the same message twice is the caller's business; this stage answers
        // "did the recipient already see it", which is a receive-side question.
        return nextHandler(message);
    }

    /// <inheritdoc />
    public Task<Message> OnReceiveAsync(Message message, Func<Message, Task<Message>> nextHandler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(nextHandler);

        if (message.TargetMailbox is null)
            return nextHandler(message);

        if (!_seen.TryAdd(message.Id, 0))
            throw new DuplicateMessageException(message.Id);

        _order.Enqueue(message.Id);
        Evict();

        return nextHandler(message);
    }

    private void Evict()
    {
        while (_order.Count > _capacity && _order.TryDequeue(out var oldest))
            _seen.TryRemove(oldest, out _);
    }
}
