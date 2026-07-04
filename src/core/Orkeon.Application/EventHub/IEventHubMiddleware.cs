namespace Orkeon.Application.EventHub;

/// <summary>
/// Middleware port allowing cross-cutting concerns (logging, telemetry, ACL, idempotency, validation)
/// to be plugged into both the publish and the receive paths.
/// </summary>
/// <remarks>
/// v1.0 ships the contract only; the pipeline runner arrives in v1.2 alongside ACL and OTel.
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
