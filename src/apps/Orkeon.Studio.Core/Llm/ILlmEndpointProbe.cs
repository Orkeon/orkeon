using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// What to probe: the endpoint, the key to present, how long to wait, and — for the second
/// step — the model and thinking switch of the profile being tested.
/// </summary>
public sealed record LlmProbeRequest
{
    /// <summary>
    /// The deadline of a probe when the profile pins none shorter (STUDIO-43). Long enough for a
    /// first request that pays DNS and TLS to a distant host; far shorter than the minutes a
    /// reasoning profile allows a real call, which no button should keep the user waiting for.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Endpoint base URL, as typed in the form.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "The value comes straight from the 'Llm:BaseUrl' text field and may be " +
                        "half-typed or malformed; the probe reports that as a failed result, which " +
                        "System.Uri cannot represent.")]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// API key presented to the endpoint. Resolve it with
    /// <see cref="LlmApiKeyResolver.Resolve(string?, string?)"/> so a key held only in the
    /// environment is used exactly as the runtime would use it.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// How long the whole probe may take, both steps included. Defaults to
    /// <see cref="DefaultTimeout"/>; <see cref="TimeoutFor"/> bounds it by a profile's own.
    /// </summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>The model of the profile, sent by the test completion.</summary>
    public string? Model { get; init; }

    /// <summary>The profile's thinking switch: null leaves the provider's default.</summary>
    public bool? ThinkingEnabled { get; init; }

    /// <summary>
    /// When true and a <see cref="Model"/> is set, a reachable catalogue is followed by a minimal
    /// completion (16 tokens, one "ping") carrying the model and the thinking switch — the only
    /// way to learn that the model exists and that the switch is accepted. It costs a few tokens.
    /// </summary>
    public bool CheckCompletion { get; init; }

    /// <summary>
    /// The deadline for a profile whose own timeout is <paramref name="profileTimeoutSeconds"/>:
    /// <see cref="DefaultTimeout"/>, or the profile's when it is shorter.
    /// </summary>
    public static TimeSpan TimeoutFor(int? profileTimeoutSeconds) =>
        profileTimeoutSeconds is { } seconds and > 0 && TimeSpan.FromSeconds(seconds) < DefaultTimeout
            ? TimeSpan.FromSeconds(seconds)
            : DefaultTimeout;
}

/// <summary>The step of a probe a result speaks of.</summary>
public enum LlmProbeStage
{
    /// <summary>Before any request: the configuration itself cannot be probed.</summary>
    Configuration,

    /// <summary>The model catalogue (<c>GET /models</c>, Ollama's <c>/api/tags</c>).</summary>
    Models,

    /// <summary>The minimal test completion on the profile's model.</summary>
    Completion,
}

/// <summary>Why a probe failed; <see cref="None"/> when it did not.</summary>
public enum LlmProbeFailure
{
    /// <summary>The probe passed.</summary>
    None,

    /// <summary>No base URL is configured.</summary>
    NoBaseUrl,

    /// <summary>The base URL is not an absolute URL (<see cref="LlmProbeResult.Detail"/> holds it).</summary>
    NotAbsoluteUrl,

    /// <summary>The base URL is not http(s) (<see cref="LlmProbeResult.Detail"/> holds it).</summary>
    NotHttpUrl,

    /// <summary>Azure OpenAI: deployments, no catalogue to probe.</summary>
    NoCatalogue,

    /// <summary>No answer before the deadline.</summary>
    Timeout,

    /// <summary>The endpoint answered with an error status.</summary>
    HttpStatus,

    /// <summary>The request could not be carried (DNS, TCP, TLS…); the exception chain is the detail.</summary>
    Transport,
}

/// <summary>
/// Outcome of a connectivity probe. Always a value, never an exception: a probe that fails
/// is an ordinary answer about the endpoint, not a fault of the caller. Structured (STUDIO-43)
/// so each front can describe it in its own language with <see cref="LlmProbeText"/>; the
/// English line is <see cref="Message"/>. Nothing here ever carries the API key.
/// </summary>
public sealed record LlmProbeResult
{
    /// <summary>True when every step that ran answered successfully.</summary>
    public bool Succeeded { get; init; }

    /// <summary>The step that failed, or the last step that ran when the probe passed.</summary>
    public LlmProbeStage Stage { get; init; } = LlmProbeStage.Models;

    /// <summary>Why the probe failed; <see cref="LlmProbeFailure.None"/> when it passed.</summary>
    public LlmProbeFailure Failure { get; init; }

    /// <summary>The URL of the step, without credentials nor query string.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Display text, already stripped of anything that could carry a key.")]
    public string? Url { get; init; }

    /// <summary>Time from the start of the probe to its verdict.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>The deadline the probe ran under.</summary>
    public TimeSpan Timeout { get; init; } = LlmProbeRequest.DefaultTimeout;

    /// <summary>The HTTP status of a refused step.</summary>
    public int? StatusCode { get; init; }

    /// <summary>The reason phrase that came with <see cref="StatusCode"/>.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>
    /// The untranslated detail: the start of an error body, the exception chain down to its
    /// innermost cause, or the reason a caller gave to <see cref="Unreachable"/>.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// How many models the catalogue listed, when its answer could be parsed. A reachable endpoint
    /// whose payload is in an unknown shape reports <see langword="null"/> and still succeeds.
    /// </summary>
    public int? ModelCount { get; init; }

    /// <summary>The model the test completion ran on; null when no completion ran.</summary>
    public string? Model { get; init; }

    /// <summary>One English line, ready to show next to the button that ran the probe.</summary>
    public string Message => LlmProbeText.Describe(this, EnglishStudioStrings.Instance, CultureInfo.InvariantCulture);

    /// <summary>The endpoint answered; <paramref name="modelCount"/> is null when unparsable.</summary>
    public static LlmProbeResult Reachable(int? modelCount) => new() { Succeeded = true, ModelCount = modelCount };

    /// <summary>The endpoint could not be reached, for a reason the caller words itself.</summary>
    public static LlmProbeResult Unreachable(string reason) => new()
    {
        Succeeded = false,
        Failure = LlmProbeFailure.Transport,
        Detail = reason,
    };
}

/// <summary>
/// Checks that a configured LLM endpoint answers — the Studio equivalent of the connectivity
/// probe <c>orkeon init</c> runs unless <c>--no-probe</c> is passed (SPEC §4.2), extended by
/// STUDIO-43 to a test completion on the profile's model. Optional and never blocking: nothing
/// in Studio depends on its result, and a failure never prevents a configuration from being saved.
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
