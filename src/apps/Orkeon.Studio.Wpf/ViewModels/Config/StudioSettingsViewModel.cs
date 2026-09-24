using System.Globalization;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The « Studio » tab of the settings: how Studio itself behaves on this machine, in both
/// modes. Everything here is <see cref="StudioSettings"/>, kept in <c>ui-preferences.json</c>
/// through <see cref="StudioUiPreferences.PersistStudio"/> — never in the settings file the
/// teams run on — and every change is written at once, merged into the file (STUDIO-35 D-06).
/// <para>
/// STUDIO-35 puts the provider balance here: the optional automatic reading, off by default
/// (D-02), and one alert threshold per provider whose balance a key reads (D-03). STUDIO-32 puts
/// the archive suggestion of My teams beside it (DB-1): its switch and its threshold, written
/// through the same <see cref="Apply"/>.
/// </para>
/// </summary>
public sealed class StudioSettingsViewModel : ObservableObject
{
    /// <summary>The intervals offered besides «never», in minutes: none short enough to strain a vendor's rate limit.</summary>
    private static readonly int[] Intervals = [5, 15, 30, 60];

    /// <summary>The thresholds the archive suggestion offers, in days: a month to a year.</summary>
    private static readonly int[] SuggestionThresholds = [30, 60, 90, 180, 365];

    private readonly BalanceReadings _balances;
    private readonly Action<StudioSettings>? _persist;
    private readonly IStudioStrings _strings;

    /// <summary>
    /// Builds the tab over the balances it tunes; <paramref name="persist"/> writes each change —
    /// left out, the choices last for the session only (the screenshot campaign's case).
    /// </summary>
    public StudioSettingsViewModel(
        BalanceReadings? balances = null,
        Action<StudioSettings>? persist = null,
        IStudioStrings? strings = null)
    {
        _balances = balances ?? new BalanceReadings();
        _persist = persist;
        _strings = strings ?? EnglishStudioStrings.Instance;

        // A value written by hand that the list does not offer is offered too, in its place:
        // the combo must show what the file says, not an empty box.
        var current = _balances.Settings.BalanceRefreshMinutes;
        RefreshChoices =
        [
            new BalanceRefreshChoice(null, _strings),
            .. Intervals.Append(current ?? Intervals[0]).Distinct().Order().Select(minutes => new BalanceRefreshChoice(minutes, _strings)),
        ];
        Thresholds = [.. HttpProviderBalanceProbe.ReadableProviders.Select(provider => new BalanceThresholdRowViewModel(this, provider, _strings))];
        // The same rule for the suggestion's threshold: a number of days written by hand is offered.
        ArchiveSuggestionChoices =
        [
            .. SuggestionThresholds.Append(_balances.Settings.ArchiveSuggestionDays).Distinct().Order()
                .Select(days => new ArchiveSuggestionChoice(days, _strings)),
        ];

        _balances.Changed += (_, _) =>
        {
            foreach (var row in Thresholds)
                row.RefreshHint();
        };
        _strings.CultureChanged += (_, _) =>
        {
            foreach (var choice in RefreshChoices)
                choice.RefreshLabel();
            foreach (var row in Thresholds)
                row.RefreshLabels();
            foreach (var choice in ArchiveSuggestionChoices)
                choice.RefreshLabel();
        };
    }

    /// <summary>
    /// Settings › Studio as in force now; raised on every change. What a screen outside the tab
    /// reads, and listens to, for a setting of its own (STUDIO-32's archiving suggestion).
    /// </summary>
    public StudioSettings Current => _balances.Settings;

    /// <summary>«Never — the default», then every 5, 15, 30 or 60 minutes.</summary>
    public IReadOnlyList<BalanceRefreshChoice> RefreshChoices { get; }

    /// <summary>The automatic reading in force; setting it writes it and re-arms the bar's beat.</summary>
    public BalanceRefreshChoice SelectedRefresh
    {
        get => RefreshChoices.FirstOrDefault(choice => choice.Minutes == _balances.Settings.BalanceRefreshMinutes)
               ?? RefreshChoices[0];
        set
        {
            if (value is null || value.Minutes == _balances.Settings.BalanceRefreshMinutes)
                return;

            Apply(_balances.Settings with { BalanceRefreshMinutes = value.Minutes });
            OnPropertyChanged();
        }
    }

    /// <summary>One alert threshold per provider whose balance a key reads (STUDIO-33) — the only ones that can fire.</summary>
    public IReadOnlyList<BalanceThresholdRowViewModel> Thresholds { get; }

    /// <summary>
    /// Whether My teams proposes to archive the teams not launched for a while (STUDIO-32, DB-1): on
    /// by default. Turned off, the proposal leaves the screen at once.
    /// </summary>
    public bool ArchiveSuggestion
    {
        get => _balances.Settings.ArchiveSuggestion;
        set
        {
            if (value == _balances.Settings.ArchiveSuggestion)
                return;

            Apply(_balances.Settings with { ArchiveSuggestion = value });
            OnPropertyChanged();
        }
    }

    /// <summary>The thresholds offered: a month, two, three, six, a year — and a number of days written by hand.</summary>
    public IReadOnlyList<ArchiveSuggestionChoice> ArchiveSuggestionChoices { get; }

    /// <summary>The days without activity past which a team is proposed for archiving; setting it writes it.</summary>
    public ArchiveSuggestionChoice SelectedArchiveSuggestion
    {
        get => ArchiveSuggestionChoices.FirstOrDefault(choice => choice.Days == _balances.Settings.ArchiveSuggestionDays)
               ?? ArchiveSuggestionChoices.First(choice => choice.Days == StudioSettings.DefaultArchiveSuggestionDays);
        set
        {
            if (value is null || value.Days == _balances.Settings.ArchiveSuggestionDays)
                return;

            Apply(_balances.Settings with { ArchiveSuggestionDays = value.Days });
            OnPropertyChanged();
        }
    }

    /// <summary>The threshold a provider's balance is held to now; null for none.</summary>
    internal decimal? ThresholdOf(string provider) => _balances.ThresholdOf(provider);

    /// <summary>The latest amount read for a provider, beside its threshold: the currency to type it in.</summary>
    internal ProviderBalanceResult? LatestAmountOf(string provider) => _balances.LatestAmountOf(provider);

    /// <summary>Sets or clears one provider's threshold.</summary>
    internal void SetThreshold(string provider, decimal? threshold)
    {
        if (threshold == _balances.ThresholdOf(provider))
            return;

        var thresholds = _balances.Settings.BalanceThresholds;
        Apply(_balances.Settings with
        {
            BalanceThresholds = threshold is { } value ? thresholds.SetItem(provider, value) : thresholds.Remove(provider),
        });
    }

    /// <summary>One change: in force at once — the bar's tone and beat follow — and written, merged into the file.</summary>
    private void Apply(StudioSettings settings)
    {
        _balances.Settings = settings;
        _persist?.Invoke(settings);
        OnPropertyChanged(nameof(Current));
    }
}

/// <summary>One threshold of the archive suggestion (STUDIO-32, DB-1): so many days without activity.</summary>
public sealed class ArchiveSuggestionChoice : ObservableObject
{
    private readonly IStudioStrings _strings;

    internal ArchiveSuggestionChoice(int days, IStudioStrings strings)
    {
        Days = days;
        _strings = strings;
    }

    /// <summary>The days without activity.</summary>
    public int Days { get; }

    /// <summary>What the combo says: «60 days».</summary>
    public string Label => string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.SettingsArchiveSuggestionDays], Days);

    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>One position of the automatic balance reading: never, or every so many minutes.</summary>
public sealed class BalanceRefreshChoice : ObservableObject
{
    private readonly IStudioStrings _strings;

    internal BalanceRefreshChoice(int? minutes, IStudioStrings strings)
    {
        Minutes = minutes;
        _strings = strings;
    }

    /// <summary>The interval in minutes; null for never.</summary>
    public int? Minutes { get; }

    /// <summary>What the combo says.</summary>
    public string Label => Minutes is { } every
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.BalanceRefreshEvery], every)
        : _strings[StudioStringKeys.BalanceRefreshOff];

    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// One provider's alert threshold (STUDIO-35 D-03), typed in the currency the provider returns
/// — nothing is converted, and the last amount read says which currency that is.
/// </summary>
public sealed class BalanceThresholdRowViewModel : ObservableObject
{
    private readonly StudioSettingsViewModel _owner;
    private readonly IStudioStrings _strings;
    private string _amountText;
    private bool _isInvalid;

    internal BalanceThresholdRowViewModel(StudioSettingsViewModel owner, string provider, IStudioStrings strings)
    {
        _owner = owner;
        _strings = strings;
        Provider = provider;
        _amountText = owner.ThresholdOf(provider) is { } threshold ? BalanceText.Threshold(threshold) : "";
    }

    /// <summary>The provider key the threshold is filed under.</summary>
    public string Provider { get; }

    /// <summary>The provider as the profile editor names it.</summary>
    public string Title => BalanceText.ProviderTitle(Provider, _strings);

    /// <summary>
    /// The threshold as typed: empty for none, a plain amount otherwise — «5», «12.50» or
    /// «12,50». A text that is no amount is flagged and changes nothing.
    /// </summary>
    public string AmountText
    {
        get => _amountText;
        set
        {
            if (!SetProperty(ref _amountText, value ?? ""))
                return;

            var typed = _amountText.Trim();
            if (typed.Length == 0)
            {
                IsInvalid = false;
                _owner.SetThreshold(Provider, null);
            }
            else if (decimal.TryParse(
                         typed.Replace(',', '.'),
                         NumberStyles.AllowDecimalPoint,
                         CultureInfo.InvariantCulture,
                         out var threshold))
            {
                IsInvalid = false;
                _owner.SetThreshold(Provider, threshold);
            }
            else
            {
                IsInvalid = true;
            }
        }
    }

    /// <summary>Whether the typed text is no amount — the row says so, and the threshold in force stays.</summary>
    public bool IsInvalid
    {
        get => _isInvalid;
        private set => SetProperty(ref _isInvalid, value);
    }

    /// <summary>«last read: 110.00 CNY», or that nothing was read this session.</summary>
    public string Hint => _owner.LatestAmountOf(Provider) is { } reading
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.BalanceThresholdLastRead], BalanceText.Amounts(reading))
        : _strings[StudioStringKeys.BalanceThresholdNoReading];

    internal void RefreshHint() => OnPropertyChanged(nameof(Hint));

    internal void RefreshLabels() => OnPropertiesChanged(nameof(Title), nameof(Hint));
}
