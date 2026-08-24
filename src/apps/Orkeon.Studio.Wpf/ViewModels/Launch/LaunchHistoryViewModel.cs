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
public sealed class LaunchHistoryEntryViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Wraps a stored entry.</summary>
    public LaunchHistoryEntryViewModel(
        LaunchHistoryEntry entry,
        IStudioStrings? strings = null,
        Action<LaunchHistoryEntry>? onReplay = null,
        IShellOpener? shellOpener = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        _strings = strings ?? EnglishStudioStrings.Instance;
        ReplayCommand = new RelayCommand(() => onReplay?.Invoke(Entry), () => onReplay is not null);
        OpenResultCommand = new RelayCommand(
            () => { if (ResultFolder() is { } folder) shellOpener?.Open(folder); },
            () => shellOpener is not null && ResultFolder() is not null);
    }

    /// <summary>Replays this launch (per-card button — the mock has no global action row).</summary>
    public RelayCommand ReplayCommand { get; }

    /// <summary>Opens the run's writable folder, when the argv named one.</summary>
    public RelayCommand OpenResultCommand { get; }

    /// <summary>Whether the "open result" button should show at all.</summary>
    public bool HasResultFolder => ResultFolder() is not null;

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

    /// <summary>The team's short name — the card title (the full path stays expert detail).</summary>
    public string TeamName
    {
        get
        {
            var trimmed = Target.TrimEnd('/', '\\');
            var name = System.IO.Path.GetFileNameWithoutExtension(trimmed);
            return name is { Length: > 0 } ? name : Target;
        }
    }

    /// <summary>"12/08/2026 09:41 · 2 min 05 s" — date plus duration, when one was recorded.</summary>
    public string DateLine
    {
        get
        {
            var date = StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            return Entry.Duration is { } d ? $"{date} · {FormatDuration(d)}" : date;
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

    /// <summary>The physical folder of the first writable mount in the recorded argv.</summary>
    internal string? ResultFolder()
    {
        var arguments = Entry.Arguments;
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] is not ("--mount" or "-V"))
                continue;

            for (var j = i + 1; j < arguments.Count && !arguments[j].StartsWith('-'); j++)
            {
                if (MountDefinition.TryParse(arguments[j], out var mount, out _)
                    && mount is { Rights: MountRights.ReadWrite, PhysicalPath.Length: > 0 })
                {
                    return mount.PhysicalPath;
                }
            }
        }

        return null;
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours} h {duration.Minutes:00} min")
        : duration.TotalMinutes >= 1
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s")
            : string.Create(CultureInfo.CurrentCulture, $"{duration.Seconds} s");

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

    /// <summary>Builds the panel over a history store; a null store keeps the history in memory only.</summary>
    public LaunchHistoryViewModel(
        ILaunchHistoryStore? store = null,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null,
        IShellOpener? shellOpener = null)
    {
        _store = store;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings;
        _shellOpener = shellOpener;

        ReplayCommand = new RelayCommand(Replay, () => SelectedEntry is not null);
        ReloadCommand = new AsyncRelayCommand(() => LoadAsync());
    }

    /// <summary>Raised when the user asks to replay <see cref="SelectedEntry"/>.</summary>
    public event EventHandler<LaunchReplayEventArgs>? ReplayRequested;

    /// <summary>The stored launches, most recent first.</summary>
    public ObservableCollection<LaunchHistoryEntryViewModel> Entries { get; } = [];

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
                entry, _strings, e => ReplayRequested?.Invoke(this, new LaunchReplayEventArgs(e)), _shellOpener));

        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }

    private void Replay()
    {
        if (SelectedEntry is { } entry)
            ReplayRequested?.Invoke(this, new LaunchReplayEventArgs(entry.Entry));
    }
}
