using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
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

/// <summary>One past launch, ready to be shown and replayed.</summary>
public sealed class LaunchHistoryEntryViewModel
{
    /// <summary>Wraps a stored entry.</summary>
    public LaunchHistoryEntryViewModel(LaunchHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
    }

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

    /// <summary>Builds the panel over a history store; a null store keeps the history in memory only.</summary>
    public LaunchHistoryViewModel(ILaunchHistoryStore? store = null, IUiDispatcher? dispatcher = null)
    {
        _store = store;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;

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
            Entries.Add(new LaunchHistoryEntryViewModel(entry));

        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }

    private void Replay()
    {
        if (SelectedEntry is { } entry)
            ReplayRequested?.Invoke(this, new LaunchReplayEventArgs(entry.Entry));
    }
}
