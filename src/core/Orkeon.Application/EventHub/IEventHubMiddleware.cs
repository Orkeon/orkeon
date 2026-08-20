namespace Orkeon.Application.EventHub;

/// <summary>
/// Middleware port allowing cross-cutting concerns (logging, telemetry, ACL, idempotency, validation)
/// to be plugged into both the publish and the receive paths.
/// </summary>
/// <remarks>
/// The runner is <c>EventHubMiddlewarePipeline</c>; the five stages it composes — logging,
/// telemetry, ACL, idempotency, validation — all ship.
/// </remarks>
public interface IEventHubMiddleware
{
    /// <summary>Invoked on the publish path. Implementations may inspect, mutate, or short-circuit by throwing.</summary>
    Task<Message> OnPublishAsync(
        Message message,
        Func<Message, Task<Message>> nextHandler,
        CancellationToken ct);

    /// <summary>Invoked on the receive path. Implementations may inspect, mutate, or short-circuit by throwing.</summary>
    Task<Message> OnReceiveAsync(
        Message message,
        Func<Message, Task<Message>> nextHandler,
        CancellationToken ct);
}
