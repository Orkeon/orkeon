using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The provider balances Studio has read this session (STUDIO-35): one reading per account, in
/// memory only. Nothing here is ever written down — a figure read yesterday must not pass for
/// today's (D-05) — so a new window starts with none, and the probe is asked only when a
/// trigger asks this type: the startup, the end of an activity, a click, the optional interval
/// (D-02), or the profile editor's own line.
/// <para>
/// The status bar, the profile rows and the profile editor all read the same readings, so an
/// account read for one is current for the three. It also holds Settings › Studio, whose
/// thresholds decide the warning tone of a reading (D-03).
/// </para>
/// </summary>
public sealed class BalanceReadings
{
    private readonly IProviderBalanceProbe? _probe;
    private readonly IApiKeyStore _keys;
    private readonly TimeProvider _clock;
    private readonly Dictionary<ProviderBalanceAccount, ProviderBalanceResult> _readings = [];
    private StudioSettings _settings;
    private Task? _underWay;

    /// <summary>
    /// Builds the readings over <paramref name="probe"/>. Without one nothing is ever read, and
    /// nothing offers to read: the balance simply does not show (see <see cref="StudioServices.BalanceProbe"/>).
    /// </summary>
    /// <param name="probe">What reads an account; null for none.</param>
    /// <param name="keys">Where the keys are peeked; the user environment when absent.</param>
    /// <param name="settings">Settings › Studio as the window opens; the defaults when absent.</param>
    /// <param name="clock">Stamps the reading of a probe that failed outright; the system clock when absent.</param>
    public BalanceReadings(
        IProviderBalanceProbe? probe = null,
        IApiKeyStore? keys = null,
        StudioSettings? settings = null,
        TimeProvider? clock = null)
    {
        _probe = probe;
        _keys = keys ?? new EnvironmentApiKeyStore();
        _settings = settings ?? StudioSettings.Default;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Raised when a read starts, when each account's reading lands, when the read ends, and when the settings change.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether a probe is wired: without one there is no balance to show, nor to offer to read.</summary>
    public bool CanRead => _probe is not null;

    /// <summary>Whether a read of the covered accounts is under way.</summary>
    public bool IsReading { get; private set; }

    /// <summary>Settings › Studio: the automatic reading's interval and the alert thresholds.</summary>
    public StudioSettings Settings
    {
        get => _settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_settings, value))
                return;

            _settings = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The last reading of <paramref name="account"/> this session; null when none was taken.</summary>
    public ProviderBalanceResult? Of(ProviderBalanceAccount account) => _readings.GetValueOrDefault(account);

    /// <summary>The latest reading of <paramref name="provider"/> that told an amount, whichever account it came from.</summary>
    public ProviderBalanceResult? LatestAmountOf(string provider) => _readings.Values
        .Where(reading => reading.Status == ProviderBalanceStatus.Available
                          && string.Equals(reading.Provider, provider, StringComparison.Ordinal))
        .MaxBy(reading => reading.CheckedAt);

    /// <summary>The alert threshold of <paramref name="provider"/>, in the currency it returns; null for none.</summary>
    public decimal? ThresholdOf(string provider) =>
        _settings.BalanceThresholds.TryGetValue(provider, out var threshold) ? threshold : null;

    /// <summary>Whether an amount of <paramref name="reading"/> is under its provider's threshold — the warning tone (D-03).</summary>
    public bool IsUnderThreshold(ProviderBalanceResult reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading.Status == ProviderBalanceStatus.Available
               && ThresholdOf(reading.Provider) is { } threshold
               && reading.Amounts.Any(amount => amount.Available < threshold);
    }

    /// <summary>
    /// Reads every target once — one request per account, however many profiles share it. A
    /// read already under way is joined rather than repeated: an activity that ends during the
    /// startup read, or a click on it, costs no second request. Each reading lands as soon as
    /// its provider answers; a slow one does not hold back the others.
    /// </summary>
    public Task ReadAsync(IReadOnlyList<ProviderBalanceTarget> targets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (_probe is null || targets.Count == 0)
            return Task.CompletedTask;

        if (_underWay is { IsCompleted: false } underWay)
            return underWay;

        _underWay = ReadAllAsync(_probe, targets, cancellationToken);
        return _underWay;
    }

    /// <summary>
    /// Reads one endpoint for the profile editor, whose fields may not be saved yet. The reading
    /// is filed under its account — the rows and the bar learn it too — unless it was taken with
    /// a key typed and not remembered: that is not the key a run would present.
    /// </summary>
    /// <returns>The reading; null when no probe is wired.</returns>
    public async Task<ProviderBalanceResult?> ReadEndpointAsync(
        ProviderBalanceTarget target,
        string? typedKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_probe is null)
            return null;

        var reading = await ProbeAsync(_probe, target, typedKey, cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(typedKey))
        {
            _readings[target.Account] = reading;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return reading;
    }

    private async Task ReadAllAsync(IProviderBalanceProbe probe, IReadOnlyList<ProviderBalanceTarget> targets, CancellationToken cancellationToken)
    {
        IsReading = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await Task.WhenAll(targets.Select(target => FileAsync(probe, target, cancellationToken))).ConfigureAwait(true);
        }
        finally
        {
            IsReading = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task FileAsync(IProviderBalanceProbe probe, ProviderBalanceTarget target, CancellationToken cancellationToken)
    {
        _readings[target.Account] = await ProbeAsync(probe, target, typedKey: null, cancellationToken).ConfigureAwait(true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// One probe, and the key taken out of whatever it says. The probe masks the key already
    /// (STUDIO-33 D-05); a reading is shown in three places, so it is masked once more here
    /// rather than trusted to every probe.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "A probe answers with a typed result, never an exception; one that throws all the " +
                        "same must leave a «no answer» reading on the bar, not a fault in a discarded task.")]
    private async Task<ProviderBalanceResult> ProbeAsync(
        IProviderBalanceProbe probe,
        ProviderBalanceTarget target,
        string? typedKey,
        CancellationToken cancellationToken)
    {
        var request = target.RequestWith(_keys, typedKey);
        try
        {
            var reading = await probe.ProbeAsync(request, cancellationToken).ConfigureAwait(true);
            return reading with { Detail = Masked(reading.Detail, request.ApiKey) };
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderBalanceResult
            {
                Provider = target.Account.Provider,
                Status = ProviderBalanceStatus.NetworkError,
                CheckedAt = _clock.GetUtcNow(),
                Detail = Masked(ex.Message, request.ApiKey),
            };
        }
    }

    private static string Masked(string detail, string? key) =>
        string.IsNullOrEmpty(key) ? detail : detail.Replace(key, "***", StringComparison.Ordinal);
}
