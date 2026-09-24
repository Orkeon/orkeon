using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Llm;

/// <summary>What a balance probe learned about the account behind an endpoint and a key.</summary>
public enum ProviderBalanceStatus
{
    /// <summary>The provider answered with the balance: see <see cref="ProviderBalanceResult.Amounts"/>.</summary>
    Available,

    /// <summary>
    /// No API of the provider returns the balance to any key — some report spend to an admin
    /// key, which is not what is left. The vendor's console is the only place to read it.
    /// </summary>
    NotExposed,

    /// <summary>
    /// The balance is behind an API, but only an administrative credential reads it (a
    /// management key, an account access key) — never the inference key a profile holds.
    /// </summary>
    AdminKeyRequired,

    /// <summary>The provider refused the key (401 or 403), or there was no key to present.</summary>
    AuthenticationRefused,

    /// <summary>
    /// The exchange failed: no answer, none within the probe's own deadline, or an error status
    /// other than a refused key or a missing endpoint.
    /// </summary>
    NetworkError,

    /// <summary>
    /// The provider answered, but not in the shape its reference documents — the endpoint has
    /// changed since it was verified, and retrying will not help.
    /// </summary>
    UnexpectedAnswer,

    /// <summary>There is no account to ask: a local runtime, or no endpoint configured.</summary>
    NotApplicable,
}

/// <summary>What the amounts of an available balance measure.</summary>
public enum ProviderBalanceScope
{
    /// <summary>The account's own balance, which every key of the account draws from.</summary>
    Account,

    /// <summary>
    /// What this key may still spend under the limit set on it. The account behind it may
    /// hold less — only an administrative credential reads that (OpenRouter).
    /// </summary>
    ApiKey,
}

/// <summary>One balance, in one currency, as the provider reported it.</summary>
/// <param name="Currency">ISO 4217 code of the amounts (<c>USD</c>, <c>CNY</c>).</param>
/// <param name="Available">What can still be spent.</param>
/// <param name="Granted">
/// The part the vendor gave (vouchers, promotional credit), when it tells it apart; null otherwise.
/// </param>
/// <param name="Paid">
/// The part that was paid for (topped up, cash), when the vendor tells it apart; null otherwise.
/// Negative on an account in arrears (Kimi documents it).
/// </param>
public sealed record ProviderBalanceAmount(
    string Currency,
    decimal Available,
    decimal? Granted = null,
    decimal? Paid = null);

/// <summary>
/// Outcome of a balance probe. Always a value, never an exception — and never silent: every
/// provider maps to an explicit <see cref="Status"/>, whether or not it has a balance to tell.
/// </summary>
public sealed record ProviderBalanceResult
{
    /// <summary>The provider inferred from the base URL, as <see cref="LlmProviderDetector"/> reports it.</summary>
    public required string Provider { get; init; }

    /// <summary>What the probe learned.</summary>
    public required ProviderBalanceStatus Status { get; init; }

    /// <summary>The balances, one per currency. Empty unless <see cref="Status"/> is available.</summary>
    public IReadOnlyList<ProviderBalanceAmount> Amounts { get; init; } = [];

    /// <summary>What <see cref="Amounts"/> measure.</summary>
    public ProviderBalanceScope Scope { get; init; } = ProviderBalanceScope.Account;

    /// <summary>When the probe answered — for an available balance, the moment it was read.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// One line for a tooltip or a log, in English. It never carries the key, nor any part of
    /// the provider's answer: an answer may hold account data, so none of it is quoted back —
    /// a deliberate difference with the connectivity probe (STUDIO-33 D-05).
    /// </summary>
    public required string Detail { get; init; }

    /// <summary>
    /// Where the vendor shows the balance — the https link of its card's key console, from
    /// <see cref="Presets.LlmPresets.KeyConsoleFor(string?)"/> — or null when the endpoint has
    /// no console of its own in the catalogue.
    /// </summary>
    public Uri? ConsoleUrl { get; init; }
}

/// <summary>
/// Reads what is left on the provider account behind an endpoint and a key (STUDIO-33), or says
/// plainly that the provider does not tell. It never calls itself — no timer, no background
/// refresh: the caller decides when a read is worth a request (D-06), so Studio still sends
/// nothing the user did not ask for, and the vendors' rate limits are spared.
/// </summary>
public interface IProviderBalanceProbe
{
    /// <summary>Asks the provider behind <paramref name="request"/> for its balance.</summary>
    /// <param name="request">
    /// The endpoint and the key, as the connectivity probe takes them. The request follows the
    /// endpoint's own host — Kimi's <c>.ai</c> or <c>.cn</c> — never a host of its own (D-04).
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the probe. Cancellation requested through this token propagates as an
    /// <see cref="OperationCanceledException"/>; every other outcome — a refused key, a silent
    /// host, the probe's own timeout — comes back as a <see cref="ProviderBalanceResult"/>.
    /// </param>
    Task<ProviderBalanceResult> ProbeAsync(LlmProbeRequest request, CancellationToken cancellationToken = default);
}
