using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IProviderBalanceProbe"/> that answers from a script instead of a provider,
/// recording every request it was asked — what the balance tests count.
/// </summary>
public sealed class FakeProviderBalanceProbe : IProviderBalanceProbe
{
    /// <summary>The instant every scripted answer says it was read at.</summary>
    public static readonly DateTimeOffset ReadAt = new(2026, 9, 24, 10, 31, 0, TimeSpan.Zero);

    /// <summary>Every request, in order.</summary>
    public List<LlmProbeRequest> Requests { get; } = [];

    /// <summary>
    /// What a request is answered; by default 110 CNY available on the endpoint's provider — or, as
    /// the real probe answers a provider whose balance a key reads, the key missing when the request
    /// carries none (STUDIO-54).
    /// </summary>
    public Func<LlmProbeRequest, ProviderBalanceResult> Answer { get; set; } = WithoutKeyMissing(Available(110m, "CNY"));

    /// <summary>The detail the real probe gives a request without a key.</summary>
    public const string KeyMissingDetail = "No API key is set, so there is no account to ask.";

    /// <summary>When set, no answer comes back until this is completed.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>An available balance, in one currency.</summary>
    public static Func<LlmProbeRequest, ProviderBalanceResult> Available(decimal amount, string currency) =>
        request => Result(request, ProviderBalanceStatus.Available, $"{amount} {currency} available.") with
        {
            Amounts = [new ProviderBalanceAmount(currency, amount)],
        };

    /// <summary>An answer with no amount — refused, not exposed, no answer…</summary>
    public static Func<LlmProbeRequest, ProviderBalanceResult> Without(ProviderBalanceStatus status, string detail) =>
        request => Result(request, status, detail);

    /// <summary><paramref name="answer"/> for a request with a key; the key missing, without asking anyone, for one without.</summary>
    public static Func<LlmProbeRequest, ProviderBalanceResult> WithoutKeyMissing(Func<LlmProbeRequest, ProviderBalanceResult> answer) =>
        request => string.IsNullOrWhiteSpace(request.ApiKey)
            ? Result(request, ProviderBalanceStatus.KeyMissing, KeyMissingDetail)
            : answer(request);

    /// <inheritdoc />
    public async Task<ProviderBalanceResult> ProbeAsync(LlmProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);

        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancellationToken);

        return Answer(request);
    }

    private static ProviderBalanceResult Result(LlmProbeRequest request, ProviderBalanceStatus status, string detail) => new()
    {
        Provider = LlmProviderDetector.Detect(request.BaseUrl),
        Status = status,
        CheckedAt = ReadAt,
        Detail = detail,
        ConsoleUrl = LlmPresets.KeyConsoleFor(request.BaseUrl),
    };
}
