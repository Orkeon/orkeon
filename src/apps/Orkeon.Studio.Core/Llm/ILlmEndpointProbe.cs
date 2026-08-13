using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Orkeon.Studio.Core.Llm;

/// <summary>What to probe: the endpoint, the key to present, and how long to wait.</summary>
public sealed record LlmProbeRequest
{
    /// <summary>Endpoint base URL, as typed in the form.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "The value comes straight from the 'Llm:BaseUrl' text field and may be " +
                        "half-typed or malformed; the probe reports that as a failed result, which " +
                        "System.Uri cannot represent.")]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// API key presented to the endpoint. Resolve it with
    /// <see cref="LlmApiKeyResolver.Resolve(string?)"/> so a key held only in the
    /// environment is used exactly as the runtime would use it.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// How long to wait before giving up. Kept short on purpose: the probe is a convenience,
    /// and a UI must not appear stuck because an endpoint is silently dropping packets.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Outcome of a connectivity probe. Always a value, never an exception: a probe that fails
/// is an ordinary answer about the endpoint, not a fault of the caller.
/// </summary>
/// <param name="Succeeded">True when the endpoint answered successfully.</param>
/// <param name="Message">One line, ready to show next to the button that ran the probe.</param>
/// <param name="ModelCount">
/// How many models the endpoint listed, when its answer could be parsed. A reachable endpoint
/// whose payload is in an unknown shape reports <see langword="null"/> and still succeeds — the
/// question asked was whether it answers, not what it serves.
/// </param>
public sealed record LlmProbeResult(bool Succeeded, string Message, int? ModelCount = null)
{
    /// <summary>The endpoint answered; <paramref name="modelCount"/> is null when unparsable.</summary>
    public static LlmProbeResult Reachable(int? modelCount) => new(
        Succeeded: true,
        Message: modelCount is { } count
            ? string.Create(CultureInfo.InvariantCulture, $"Endpoint reachable — {count} model(s).")
            : "Endpoint reachable.",
        ModelCount: modelCount);

    /// <summary>The endpoint could not be reached, or refused the request.</summary>
    public static LlmProbeResult Unreachable(string reason) => new(
        Succeeded: false,
        Message: string.Create(CultureInfo.InvariantCulture, $"Connection failed: {reason}"));
}

/// <summary>
/// Checks that a configured LLM endpoint answers — the Studio equivalent of the connectivity
/// probe <c>orkeon init</c> runs unless <c>--no-probe</c> is passed (SPEC §4.2). Optional and
/// never blocking: nothing in Studio depends on its result, and a failure never prevents a
/// configuration from being saved.
/// </summary>
public interface ILlmEndpointProbe
{
    /// <summary>
    /// Probes <paramref name="request"/> and describes what happened.
    /// </summary>
    /// <param name="request">The endpoint to reach and the key to present.</param>
    /// <param name="cancellationToken">
    /// Cancels the probe. Cancellation requested through this token propagates as an
    /// <see cref="OperationCanceledException"/>; every other failure — an unreachable host,
    /// a rejected key, the probe's own timeout — comes back as a failed
    /// <see cref="LlmProbeResult"/>.
    /// </param>
    Task<LlmProbeResult> ProbeAsync(LlmProbeRequest request, CancellationToken cancellationToken = default);
}
