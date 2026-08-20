using Orkeon.Application.EventHub;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// Runs the <see cref="IEventHubMiddleware"/> chain on both hub paths (HUB-01). The port
/// shipped in v1.0 with the contract only — its own doc comment says the runner "arrives in
/// v1.2 alongside ACL and OTel" — so until now nothing logged, instrumented, authorized,
/// deduplicated or validated a hub message.
/// <para>
/// Order is the registration order, and it is not cosmetic: the spec's §12 sequence puts
/// logging first so it sees everything the ACL later rejects, and validation last because it
/// is the most expensive. On the receive path the chain runs in reverse, so a middleware
/// wraps a message symmetrically on the way in and on the way out.
/// </para>
/// <para>
/// <b>A middleware short-circuits by throwing</b>, which is the contract the port documents:
/// <c>AclMiddleware</c> rejecting a message is an exception, not a null return. The pipeline
/// does not catch — a refusal must reach the caller, unlike hook dispatch where swallowing is
/// the correct behaviour.
/// </para>
/// </summary>
internal sealed class EventHubMiddlewarePipeline
{
    private readonly IReadOnlyList<IEventHubMiddleware> _middlewares;

    /// <summary>Builds the pipeline over the middlewares in their registration order.</summary>
    public EventHubMiddlewarePipeline(IEnumerable<IEventHubMiddleware> middlewares)
    {
        ArgumentNullException.ThrowIfNull(middlewares);
        _middlewares = [.. middlewares];
    }

    /// <summary>Whether anything is registered — an empty pipeline is a pure pass-through.</summary>
    public bool IsEmpty => _middlewares.Count == 0;

    /// <summary>Runs the publish path, outermost middleware first.</summary>
    public Task<Message> OnPublishAsync(Message message, CancellationToken ct) =>
        Run(message, ct, reverse: false, static (m, msg, next, token) => m.OnPublishAsync(msg, next, token));

    /// <summary>Runs the receive path, in reverse so wrapping is symmetric.</summary>
    public Task<Message> OnReceiveAsync(Message message, CancellationToken ct) =>
        Run(message, ct, reverse: true, static (m, msg, next, token) => m.OnReceiveAsync(msg, next, token));

    private Task<Message> Run(
        Message message,
        CancellationToken ct,
        bool reverse,
        Func<IEventHubMiddleware, Message, Func<Message, Task<Message>>, CancellationToken, Task<Message>> invoke)
    {
        if (_middlewares.Count == 0)
            return Task.FromResult(message);

        // Fold from the inside out: the terminal handler returns the message untouched,
        // and each middleware wraps what has been built so far.
        Func<Message, Task<Message>> next = Task.FromResult;
        for (var i = 0; i < _middlewares.Count; i++)
        {
            var middleware = _middlewares[reverse ? i : _middlewares.Count - 1 - i];
            var inner = next;
            next = msg => invoke(middleware, msg, inner, ct);
        }

        return next(message);
    }
}
