using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The Balance segment on the right of the status bar (STUDIO-35): what the provider accounts
/// behind the default profile, the assistant's and the profiles the teams name have left — one
/// entry per account, however many profiles share it (D-01).
/// <para>
/// The run's live consumption is the activity groups' business (STUDIO-34); the balance is
/// read at startup, at the end of each activity, on a click and — only when Settings › Studio
/// asks for it — on an interval (D-02). Nothing else reads it: a window left open sends no
/// request of its own accord. A click on an entry reads again, except on a provider whose
/// balance only its console shows, where it opens that console (D-01). Under its provider's
/// threshold an amount takes the warning tone (D-03).
/// </para>
/// </summary>
public sealed class StatusBarBalanceViewModel : ObservableObject
{
    private readonly BalanceReadings? _readings;
    private readonly Func<IReadOnlyList<ProviderBalanceTarget>> _coverage;
    private readonly IStudioStrings _strings;
    private readonly IShellOpener? _opener;
    private readonly IUiTicker _ticker;
    private IReadOnlyList<ProviderBalanceTarget> _covered = [];
    private int? _beatMinutes;
    private string? _detail;

    internal StatusBarBalanceViewModel(
        BalanceReadings? readings,
        Func<IReadOnlyList<ProviderBalanceTarget>> coverage,
        IStudioStrings strings,
        IShellOpener? opener,
        IUiTicker ticker)
    {
        _readings = readings;
        _coverage = coverage;
        _strings = strings;
        _opener = opener;
        _ticker = ticker;

        // A read raises an event when it starts, as each account lands and when it ends: the
        // entries are said again each time, over the accounts already worked out — which
        // accounts are covered changes with the profiles and the teams, not with a reading.
        if (readings is not null)
        {
            readings.Changed += (_, _) =>
            {
                ArmBeat();
                Render();
            };
        }

        ArmBeat();
        Rebuild();
    }

    /// <summary>One entry per covered account that has something to say or to offer.</summary>
    public ObservableCollection<StatusBarBalanceItemViewModel> Items { get; } = [];

    /// <summary>Whether the segment is on the bar — in both modes (STUDIO-34 D-03).</summary>
    public bool HasBalance => Items.Count > 0;

    /// <summary>Whether an amount is under its provider's alert threshold (D-03).</summary>
    public bool IsWarning => Items.Any(item => item.IsWarning);

    /// <summary>The segment on hover: one line per account — its profiles, what it has left or why not, and when it was read.</summary>
    public string? Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    /// <summary>
    /// Reads the covered accounts again — the startup, the end of an activity, a click, the
    /// interval — worked out afresh, so a team adopted since the last event is read too.
    /// </summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_readings is null)
            return Task.CompletedTask;

        _covered = _coverage();
        return _readings.ReadAsync(_covered, cancellationToken);
    }

    /// <summary>
    /// Works out again which accounts the segment covers — the profiles or the teams changed,
    /// or the language switched — and says the segment again.
    /// </summary>
    internal void Rebuild()
    {
        _covered = _coverage();
        Render();
    }

    /// <summary>Says the segment again over the covered accounts: a read started, an account's reading landed, a threshold moved.</summary>
    private void Render()
    {
        Items.Clear();
        var lines = new List<string?>();

        if (_readings is { CanRead: true } readings)
        {
            foreach (var target in _covered)
            {
                var reading = readings.Of(target.Account);
                var item = new StatusBarBalanceItemViewModel(target, reading, readings, _strings, _opener, () => _ = RefreshAsync());
                if (item.IsShown)
                    Items.Add(item);

                lines.Add(item.Line);
                if (reading is { Status: not ProviderBalanceStatus.Available })
                    lines.Add($"    {reading.Detail}");
            }

            if (readings.IsReading)
                lines.Insert(0, _strings[StudioStringKeys.BalanceReading]);
        }

        Detail = Items.Count == 0
            ? null
            : StatusBarText.Join(
                [_strings[StudioStringKeys.BalanceTitle], .. lines, _strings[StudioStringKeys.BalanceClickToRead]],
                Environment.NewLine);
        OnPropertiesChanged(nameof(HasBalance), nameof(IsWarning));
    }

    /// <summary>The automatic reading follows Settings › Studio: off by default, and nothing beats while it is.</summary>
    private void ArmBeat()
    {
        var minutes = _readings?.Settings.BalanceRefreshMinutes;
        if (minutes == _beatMinutes)
            return;

        _beatMinutes = minutes;
        if (minutes is { } every && _readings is { CanRead: true })
            _ticker.StartBeat(TimeSpan.FromMinutes(every), () => _ = RefreshAsync());
        else
            _ticker.StopBeat();
    }
}

/// <summary>
/// One account on the Balance segment: its provider and what it has left, or why the balance
/// is not there — and what a click on it does (STUDIO-35 D-01).
/// </summary>
public sealed class StatusBarBalanceItemViewModel
{
    internal StatusBarBalanceItemViewModel(
        ProviderBalanceTarget target,
        ProviderBalanceResult? reading,
        BalanceReadings readings,
        IStudioStrings strings,
        IShellOpener? opener,
        Action readAgain)
    {
        var provider = BalanceText.ProviderTitle(target.Account.Provider, strings);
        var console = reading is not null && BalanceText.OffersConsole(reading) ? reading.ConsoleUrl : null;
        IsUnread = reading is null;
        OffersConsole = console is not null;
        IsWarning = reading is not null && readings.IsUnderThreshold(reading);
        IsFaint = reading?.Status is ProviderBalanceStatus.AuthenticationRefused
            or ProviderBalanceStatus.NetworkError
            or ProviderBalanceStatus.UnexpectedAnswer;

        // A balance only an admin key or a console could read, with no console to open, and an
        // endpoint with no account behind it, have nothing to offer on the bar: their line is
        // on hover only.
        IsShown = IsUnread || OffersConsole || IsFaint || reading?.Status == ProviderBalanceStatus.Available;

        var what = reading is null
            ? strings[StudioStringKeys.BalanceNotRead]
            : BalanceText.Summary(reading, readings, strings);

        Text = reading switch
        {
            { Status: ProviderBalanceStatus.Available } => $"{provider} {BalanceText.Amounts(reading)}",
            { Status: var failed } when IsFaint => $"{provider}{StatusBarText.Separator}{BalanceText.State(failed, strings)}",
            _ => provider,
        };
        Line = StatusBarText.Join(
            [
                $"{provider} ({string.Join(", ", target.Profiles)}) — {what}",
                OffersConsole ? strings[StudioStringKeys.BalanceOpenConsole] : null,
            ],
            StatusBarText.Separator) ?? provider;

        ClickCommand = console is { } link
            ? new RelayCommand(() => opener?.Open(link.AbsoluteUri), () => opener is not null)
            : new RelayCommand(readAgain, () => !readings.IsReading);
    }

    /// <summary>«DeepSeek 110.00 CNY», «OpenAI», «Kimi · key refused» — never a key.</summary>
    public string Text { get; }

    /// <summary>Whether the account was not read yet this session: a click reads it.</summary>
    public bool IsUnread { get; }

    /// <summary>Whether a click opens the vendor's console — the balance is shown there only (D-01).</summary>
    public bool OffersConsole { get; }

    /// <summary>Whether the amount is under its provider's alert threshold (D-03).</summary>
    public bool IsWarning { get; }

    /// <summary>Whether the read failed — a refused key, no answer, an answer out of shape: said quietly.</summary>
    public bool IsFaint { get; }

    /// <summary>The account's line in the segment's tooltip.</summary>
    public string Line { get; }

    /// <summary>Opens the console, or reads the balances again.</summary>
    public RelayCommand ClickCommand { get; }

    /// <summary>Whether the entry has a place on the bar, rather than on hover only.</summary>
    internal bool IsShown { get; }
}
