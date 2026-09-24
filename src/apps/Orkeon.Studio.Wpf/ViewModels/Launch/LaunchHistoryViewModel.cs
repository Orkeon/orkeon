using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>Carries the entry the user asked to replay.</summary>
public sealed class LaunchReplayEventArgs : EventArgs
{
    /// <summary>Wraps the entry to replay.</summary>
    public LaunchReplayEventArgs(LaunchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
    }

    /// <summary>The past launch to prepare again.</summary>
    public LaunchHistoryEntry Entry { get; }
}

/// <summary>One past launch, ready to be shown and replayed — one card of the mock's list.</summary>
public sealed class LaunchHistoryEntryViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private bool _isOfferingRestore;

    /// <summary>Wraps a stored entry.</summary>
    public LaunchHistoryEntryViewModel(
        LaunchHistoryEntry entry,
        IStudioStrings? strings = null,
        Action<LaunchHistoryEntry>? onReplay = null,
        IShellOpener? shellOpener = null,
        Func<IReadOnlyList<string>>? declaredMounts = null,
        Action<LaunchHistoryEntry>? onRestore = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _declaredMounts = declaredMounts;
        ReplayCommand = new RelayCommand(() => onReplay?.Invoke(Entry), () => onReplay is not null);
        OpenResultCommand = new RelayCommand(
            () => { foreach (var folder in ResultFolders()) shellOpener?.Open(folder); },
            () => shellOpener is not null && ResultFolders().Count > 0);
        RestoreCommand = new RelayCommand(
            () =>
            {
                IsOfferingRestore = false;
                onRestore?.Invoke(Entry);
            },
            () => onRestore is not null);
        DismissRestoreCommand = new RelayCommand(() => IsOfferingRestore = false);
    }

    private readonly Func<IReadOnlyList<string>>? _declaredMounts;

    /// <summary>Replays this launch (per-card button — the mock has no global action row).</summary>
    public RelayCommand ReplayCommand { get; }

    /// <summary>
    /// Whether the card offers « Archived team — restore it? » in place (STUDIO-31, D-07): « Replay »
    /// found the team archived and ran nothing. One card at a time, like a delete banner.
    /// </summary>
    public bool IsOfferingRestore
    {
        get => _isOfferingRestore;
        internal set => SetProperty(ref _isOfferingRestore, value);
    }

    /// <summary>« Archived team — restore it? » — the sentence of the offer.</summary>
    public string RestoreOffer => _strings[StudioStringKeys.CommonArchivedTeamRestore];

    /// <summary>« Restore » — the team goes back among the active teams; « Replay » runs it from then on.</summary>
    public RelayCommand RestoreCommand { get; }

    /// <summary>Closes the offer, restoring nothing.</summary>
    public RelayCommand DismissRestoreCommand { get; }

    /// <summary>Opens the run's writable folders — one window each — when the argv named any.</summary>
    public RelayCommand OpenResultCommand { get; }

    /// <summary>Whether the "open result" button should show at all.</summary>
    public bool HasResultFolder => ResultFolders().Count > 0;

    /// <summary>The underlying Core record.</summary>
    public LaunchHistoryEntry Entry { get; }

    /// <summary>The crew definition that was run.</summary>
    public string Target => Entry.Target;

    /// <summary>The appsettings that was passed, when one was.</summary>
    public string? SettingsPath => Entry.SettingsPath;

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt => Entry.StartedAt;

    /// <summary>How it ended.</summary>
    public RunOutcome Outcome => Entry.Outcome;

    /// <summary>The process exit code, when the process actually started.</summary>
    public int? ExitCode => Entry.ExitCode;

    /// <summary>The exact command line that was run, quoted for the host platform.</summary>
    public string CommandLine => CommandLineDisplay.Format(Entry.Arguments);

    /// <summary>The one-line form shown in the history list.</summary>
    public string Display => string.Create(
        CultureInfo.InvariantCulture,
        $"{StartedAt.ToLocalTime():yyyy-MM-dd HH:mm} — {Target} [{Outcome}]");

    /// <summary>
    /// The team's short name — the card title (the full path stays expert detail). One line,
    /// through the same normalisation as every other team name (STUDIO-16, D-01).
    /// </summary>
    public string TeamName
    {
        get
        {
            var trimmed = Target.TrimEnd('/', '\\');
            var name = System.IO.Path.GetFileNameWithoutExtension(trimmed);
            var display = name is { Length: > 0 } ? name : Target;
            return display.Length > 0 ? Orkeon.Studio.Core.Teams.TeamCatalog.NormalizeName(display) : display;
        }
    }

    /// <summary>
    /// "12/08/2026 09:41 · 2 min 05 s · 12 840 tokens · cache 62 % · 7 980 tokens" —
    /// date, duration, then the usage chips when the run measured them (W-08).
    /// </summary>
    public string DateLine
    {
        get
        {
            var date = StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var line = Entry.Duration is { } d ? $"{date} · {FormatDuration(d)}" : date;
            var chips = Orkeon.Studio.Core.Launch.UsageMetricsFormatter.Chips(
                Entry.Tokens, Entry.CacheHitTokens, Entry.CacheMissTokens, durationMs: null,
                _strings, CultureInfo.CurrentCulture);
            return chips.Count > 0 ? $"{line} · {string.Join(" · ", chips)}" : line;
        }
    }

    /// <summary>The plain-language reading of how the run ended (no raw enum on screen).</summary>
    public string OutcomeSentence => Outcome switch
    {
        RunOutcome.Success => _strings[StudioStringKeys.HistOutcomeSuccess],
        RunOutcome.Cancelled => _strings[StudioStringKeys.HistOutcomeCancelled],
        RunOutcome.NotStarted => _strings[StudioStringKeys.HistOutcomeNotStarted],
        _ => string.Format(
            CultureInfo.CurrentCulture, _strings[StudioStringKeys.HistOutcomeFailed], ExitCode ?? -1),
    };

    /// <summary>Tone key the view maps to the status icon: ok | fail | warn | idle.</summary>
    public string OutcomeTone => Outcome switch
    {
        RunOutcome.Success => "ok",
        RunOutcome.Cancelled => "warn",
        RunOutcome.NotStarted => "idle",
        _ => "fail",
    };

    /// <summary>The first folder of <see cref="ResultFolders"/>.</summary>
    internal string? ResultFolder()
    {
        var folders = ResultFolders();
        return folders.Count > 0 ? folders[0] : null;
    }

    /// <summary>
    /// The writable folders of the recorded argv, each once: the <c>--mount</c> values with
    /// write rights, and — since VFS-90 a Studio launch names a declared folder by
    /// <c>--mount-id</c> rather than by path — the settings entries those ids name, read from
    /// the declared list as it stands today.
    /// </summary>
    internal IReadOnlyList<string> ResultFolders()
    {
        var arguments = Entry.Arguments;
        var folders = new List<string>();
        var seen = new HashSet<string>(
            Orkeon.Domain.FileSystem.PhysicalPathContainment.Comparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        List<MountDefinition>? declared = null;

        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] is "--mount" or "-m" or "-V")
            {
                foreach (var value in OptionValues(arguments, i))
                    AddWritable(ParseMount(value), seen, folders);
            }
            else if (arguments[i] == "--mount-id" && _declaredMounts is not null)
            {
                declared ??= _declaredMounts().Select(ParseMount).OfType<MountDefinition>().ToList();
                foreach (var value in OptionValues(arguments, i))
                    AddWritable(DeclaredById(declared, value), seen, folders);
            }
        }

        return folders;
    }

    /// <summary>Adds the folder of a writable mount, once per normalized folder.</summary>
    private static void AddWritable(MountDefinition? mount, HashSet<string> seen, List<string> folders)
    {
        if (mount is { Rights: MountRights.ReadWrite, PhysicalPath.Length: > 0 }
            && seen.Add(MountDefinition.NormalizeFolder(mount.PhysicalPath)))
        {
            folders.Add(mount.PhysicalPath);
        }
    }

    /// <summary>The values that follow the option at <paramref name="optionIndex"/>, up to the next option.</summary>
    private static IEnumerable<string> OptionValues(IReadOnlyList<string> arguments, int optionIndex)
    {
        for (var j = optionIndex + 1; j < arguments.Count && !arguments[j].StartsWith('-'); j++)
            yield return arguments[j];
    }

    private static MountDefinition? ParseMount(string mountString) =>
        MountDefinition.TryParse(mountString, out var mount, out _) ? mount : null;

    /// <summary>The declared entry whose id <paramref name="value"/> spells, or null.</summary>
    private static MountDefinition? DeclaredById(List<MountDefinition> declared, string value) =>
        Orkeon.Domain.Common.MountId.TryParse(value, out var id)
            ? declared.FirstOrDefault(entry => id.Equals(entry.Id))
            : null;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours} h {duration.Minutes:00} min");

        if (duration.TotalMinutes >= 1)
            return string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s");

        return string.Create(CultureInfo.CurrentCulture, $"{duration.Seconds} s");
    }

    /// <inheritdoc />
    public override string ToString() => Display;
}

/// <summary>
/// The launch history of spec §5.3: the last runs kept locally so any of them can be replayed in one
/// click. Persistence is delegated to the Core store, which caps the file at 50 entries.
/// </summary>
public sealed class LaunchHistoryViewModel : ObservableObject
{
    private readonly ILaunchHistoryStore? _store;
    private readonly IUiDispatcher _dispatcher;
    private LaunchHistoryEntryViewModel? _selectedEntry;

    private readonly IStudioStrings? _strings;
    private readonly IShellOpener? _shellOpener;
    private readonly Func<IReadOnlyList<string>>? _declaredMounts;

    /// <summary>Builds the panel over a history store; a null store keeps the history in memory only.</summary>
    public LaunchHistoryViewModel(
        ILaunchHistoryStore? store = null,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null,
        IShellOpener? shellOpener = null,
        Func<IReadOnlyList<string>>? declaredMounts = null)
    {
        _store = store;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings;
        _shellOpener = shellOpener;
        _declaredMounts = declaredMounts;

        ReplayCommand = new RelayCommand(Replay, () => SelectedEntry is not null);
        ReloadCommand = new AsyncRelayCommand(() => LoadAsync());
    }

    /// <summary>Raised when the user asks to replay <see cref="SelectedEntry"/>.</summary>
    public event EventHandler<LaunchReplayEventArgs>? ReplayRequested;

    /// <summary>
    /// Raised by « Restore » on a card whose replay found its team archived (STUDIO-31, D-07) — the
    /// launcher restores the team the entry ran.
    /// </summary>
    public event EventHandler<LaunchReplayEventArgs>? RestoreRequested;

    /// <summary>The stored launches, most recent first.</summary>
    public ObservableCollection<LaunchHistoryEntryViewModel> Entries { get; } = [];

    /// <summary>Whether the empty-state phrase shows (T-12).</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Raises <see cref="ReplayRequested"/> for the selected entry.</summary>
    public RelayCommand ReplayCommand { get; }

    /// <summary>Re-reads the history file.</summary>
    public AsyncRelayCommand ReloadCommand { get; }

    /// <summary>The entry targeted by <see cref="ReplayCommand"/>.</summary>
    public LaunchHistoryEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
                ReplayCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Loads the persisted history, if there is a store.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_store is null)
            return;

        var history = await _store.LoadAsync(cancellationToken);
        Publish(history);
    }

    /// <summary>
    /// Replaces the list with <paramref name="history"/>, marshalled onto the UI thread.
    /// This is how the tab republishes the shared <c>RunSession</c>'s list after a run —
    /// the session is the only recorder, the panel only projects, so the two can never
    /// drift apart.
    /// </summary>
    public void Publish(LaunchHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);

        _dispatcher.Post(() => PublishCore(history));
    }

    private void PublishCore(LaunchHistory history)
    {
        Entries.Clear();
        foreach (var entry in history.Entries)
            Entries.Add(new LaunchHistoryEntryViewModel(
                entry, _strings, e => ReplayRequested?.Invoke(this, new LaunchReplayEventArgs(e)), _shellOpener, _declaredMounts,
                e => RestoreRequested?.Invoke(this, new LaunchReplayEventArgs(e))));

        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Opens « Archived team — restore it? » on the card of <paramref name="entry"/> — the replay
    /// that found its team archived (STUDIO-31, D-07) — and closes it on every other card: two open
    /// offers would ask the same question twice. The card is the entry itself, or the first one
    /// that ran the same target.
    /// </summary>
    internal void OfferRestore(LaunchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var offered = Entries.FirstOrDefault(row => ReferenceEquals(row.Entry, entry))
            ?? Entries.FirstOrDefault(row => string.Equals(
                Orkeon.Studio.Core.Teams.TeamCatalog.NormalizePath(row.Target),
                Orkeon.Studio.Core.Teams.TeamCatalog.NormalizePath(entry.Target),
                StringComparison.OrdinalIgnoreCase));
        foreach (var row in Entries)
            row.IsOfferingRestore = ReferenceEquals(row, offered);
    }

    private void Replay()
    {
        if (SelectedEntry is { } entry)
            ReplayRequested?.Invoke(this, new LaunchReplayEventArgs(entry.Entry));
    }
}
