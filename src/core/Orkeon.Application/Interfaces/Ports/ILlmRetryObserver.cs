namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// One scheduled retry of an LLM HTTP call: the provider is about to wait
/// <see cref="Delay"/> before retry number <see cref="Attempt"/> (of
/// <see cref="MaxRetries"/>), because of <see cref="Reason"/>.
/// </summary>
public sealed record LlmRetryEvent
{
    /// <summary>Provider name (e.g. <c>"Kimi"</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>Host being reached (e.g. <c>"api.moonshot.ai"</c>); empty when unknown.</summary>
    public string Host { get; init; } = "";

    /// <summary>1-based number of the retry about to happen.</summary>
    public required int Attempt { get; init; }

    /// <summary>The call's total retry budget.</summary>
    public required int MaxRetries { get; init; }

    /// <summary>How long the provider waits before this retry.</summary>
    public required TimeSpan Delay { get; init; }

    /// <summary>Why the last attempt failed (exception message or HTTP status); empty when unknown.</summary>
    public string Reason { get; init; } = "";
}

/// <summary>
/// Host-registered receiver for LLM retry activity, so a UI can show that a stalled turn is
/// actually waiting on a reconnection (<c>Reconnecting to api.moonshot.ai… (retry 4/10)</c>)
/// instead of sitting silent through the backoff. A host that registers no observer keeps
/// the provider behaviour unchanged — retries are then only logged.
/// </summary>
/// <remarks>
/// Called from the provider's async flow, potentially on pool threads — implementations must
/// be thread-safe, stay cheap, and must not throw; the provider additionally shields itself,
/// so a faulty observer degrades to unobserved retries, never to a failed call.
/// </remarks>
public interface ILlmRetryObserver
{
    /// <summary>A retry wait is starting.</summary>
    void OnRetryScheduled(LlmRetryEvent retry);

    /// <summary>
    /// The call left the retry loop — success or final failure. Lets the host clear any
    /// "reconnecting" banner it raised. Also called for calls that never retried.
    /// </summary>
    void OnCallSettled();
}
