using System.Collections.ObjectModel;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// One folder the session holds for the team (STUDIO-57): a confirmed folder kept inside the
/// team, the directory the engine created for it, and « Open the folder », which shows that
/// directory in the file explorer so the user can drop files or sub-folders in it. While the
/// engine waits on the drop, the folder is watched and its « Files » line counts what it holds.
/// </summary>
public sealed class WizardHeldFolderRow : ObservableObject, IDisposable
{
    private readonly IStudioStrings _strings;
    private readonly IFolderWatch _watch;
    private readonly IUiDispatcher _dispatcher;
    private IDisposable? _watching;
    private int _fileCount;

    internal WizardHeldFolderRow(
        ForgeFolder folder, IStudioStrings strings, IShellOpener? opener, IFolderWatch watch, IUiDispatcher dispatcher)
    {
        _strings = strings;
        _watch = watch;
        _dispatcher = dispatcher;
        VirtualPath = folder.Path;
        IsInput = folder.IsInput;
        Directory = folder.Directory ?? "";
        CanOpen = opener is not null && Directory.Length > 0;
        OpenCommand = new RelayCommand(() => opener?.Open(Directory), () => CanOpen);
    }

    /// <summary>The name the agents use (<c>/inpdf</c>).</summary>
    public string VirtualPath { get; }

    /// <summary>Whether the team only reads it.</summary>
    public bool IsInput { get; }

    /// <summary>« read » or « written », in the user's language.</summary>
    public string RoleLabel => _strings[IsInput ? StudioStringKeys.WizardFolderRoleInput : StudioStringKeys.WizardFolderRoleOutput];

    /// <summary>The session directory behind it — where the files go.</summary>
    public string Directory { get; }

    /// <summary>Whether « Open the folder » can show it: an opener is wired and the engine named the directory.</summary>
    public bool CanOpen { get; }

    /// <summary>« Open the folder »: the directory in the file explorer.</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>The files the directory holds, sub-folders included, as last counted.</summary>
    public int FileCount => _fileCount;

    /// <summary>« No file », « 3 files ».</summary>
    public string FilesLabel => _fileCount == 0
        ? _strings[StudioStringKeys.WizardFolderFilesNone]
        : _strings.Format(StudioStringKeys.WizardFolderFilesCount, _fileCount);

    /// <summary>Counts the files now, then again on every change under the directory, until <see cref="StopWatching"/>.</summary>
    internal void StartWatching()
    {
        Recount();
        if (_watching is null && Directory.Length > 0)
            _watching = _watch.Watch(Directory, () => _dispatcher.Post(Recount));
    }

    /// <summary>Stops the watch; the last count stays on the line.</summary>
    internal void StopWatching()
    {
        _watching?.Dispose();
        _watching = null;
        Recount();
    }

    /// <summary>The label in the new language.</summary>
    internal void OnLanguageChanged() => OnPropertyChanged(nameof(FilesLabel));

    /// <inheritdoc />
    public void Dispose()
    {
        _watching?.Dispose();
        _watching = null;
    }

    private void Recount()
    {
        var count = Directory.Length > 0 ? _watch.CountFiles(Directory) : 0;
        if (count == _fileCount)
            return;

        _fileCount = count;
        OnPropertiesChanged(nameof(FileCount), nameof(FilesLabel));
    }
}

/// <summary>
/// The fill step (STUDIO-57): once the folders are confirmed, when one or more of them are kept
/// inside the team, the wizard asks whether the user wants to put files or sub-folders in them.
/// « No » lets the composition go on; « Yes » makes the folders tab the wait point: each fiche
/// of a held folder gains its « Files » line — the count, watched, « Open the folder », the
/// path — and the pane's footer gives « Continue ». The engine waits on the answer
/// (<c>folders.filled</c>): it then reads what the folders hold and designs the team for those
/// files; the wizard holds its step and the trial until then.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    /// <summary>The held list the rows were last built from — a new one rebuilds them and asks again.</summary>
    private IReadOnlyList<ForgeFolder>? _heldShown;

    private bool _fillFoldersAnswered;
    private bool _isFillingFolders;

    /// <summary>The folders kept inside the team, in the confirmed order.</summary>
    public ObservableCollection<WizardHeldFolderRow> HeldFolderRows { get; } = [];

    /// <summary>Whether the fill card shows: folders are held, and the question is not settled.</summary>
    public bool IsFillFoldersStep => HeldFolderRows.Count > 0 && !_fillFoldersAnswered;

    /// <summary>Whether the card asks its question — « No » / « Yes ».</summary>
    public bool IsFillFoldersQuestion => IsFillFoldersStep && !_isFillingFolders;

    /// <summary>Whether the card shows the folders to fill, « Open » behind each, « Continue » below.</summary>
    public bool IsFillingFolders => IsFillFoldersStep && _isFillingFolders;

    /// <summary>« No »: nothing to drop, the construction goes on.</summary>
    public RelayCommand FillFoldersNoCommand { get; private set; } = null!;

    /// <summary>« Yes »: the folders to fill, each with « Open ».</summary>
    public RelayCommand FillFoldersYesCommand { get; private set; } = null!;

    /// <summary>« Continue »: the files are in place, the construction goes on.</summary>
    public RelayCommand FillFoldersContinueCommand { get; private set; } = null!;

    private void InitializeFillStep()
    {
        FillFoldersNoCommand = new RelayCommand(() => SettleFillStep(), () => IsFillFoldersQuestion);
        FillFoldersYesCommand = new RelayCommand(StartFillingFolders, () => IsFillFoldersQuestion);
        FillFoldersContinueCommand = new RelayCommand(() => SettleFillStep(), () => IsFillingFolders);
    }

    /// <summary>Rebuilds the rows when the engine announces the held folders (again), and republishes the card.</summary>
    private void SyncFillStep()
    {
        if (!ReferenceEquals(_model.HeldFolders, _heldShown))
        {
            _heldShown = _model.HeldFolders;
            ForgetHeldRows();
            foreach (var held in _model.HeldFolders)
                HeldFolderRows.Add(new WizardHeldFolderRow(held, _strings, _shellOpener, _folderWatch, _dispatcher));

            _fillFoldersAnswered = false;
            _isFillingFolders = false;
        }

        RefreshFillStep();
    }

    /// <summary>
    /// « Yes »: each held folder joins its fiche — a resumed session that shows no fiche gets
    /// the confirmed ones back — and its files are counted and watched.
    /// </summary>
    private void StartFillingFolders()
    {
        if (FolderRows.Count == 0)
        {
            foreach (var folder in _model.Folders)
                FolderRows.Add(NewFolderRow(folder));
        }

        foreach (var held in HeldFolderRows)
        {
            var fiche = FolderRows.FirstOrDefault(row =>
                TryNormalizeRootName(row.VirtualPath, out var root) && string.Equals(root, held.VirtualPath, StringComparison.Ordinal));
            if (fiche is null)
                continue;

            fiche.Held = held;
            held.StartWatching();
        }

        _isFillingFolders = true;
        RefreshFillStep();
    }

    private void SettleFillStep()
    {
        if (_model.FillPending && IsEngineRunning)
        {
            if (!_client.SendFoldersFilled())
            {
                StatusMessage = _strings[StudioStringKeys.WizardAssistantNotRunning];
                return;
            }

            _model.AcknowledgeFill();
        }

        foreach (var held in HeldFolderRows)
            held.StopWatching();

        _fillFoldersAnswered = true;
        _isFillingFolders = false;
        RefreshFillStep();
        // The engine composes from now on; a session that composed already shows its step now.
        SyncFromModel();
    }

    /// <summary>Forgets the card — another creation starts, or a session is resumed.</summary>
    private void ClearFillStep()
    {
        _heldShown = null;
        ForgetHeldRows();
        _fillFoldersAnswered = false;
        _isFillingFolders = false;
        RefreshFillStep();
    }

    /// <summary>The held rows leave their fiches, and their watches stop.</summary>
    private void ForgetHeldRows()
    {
        foreach (var fiche in FolderRows)
            fiche.Held = null;
        foreach (var held in HeldFolderRows)
            held.Dispose();
        HeldFolderRows.Clear();
    }

    private void RefreshFillStep()
    {
        OnPropertiesChanged(nameof(IsFillFoldersStep), nameof(IsFillFoldersQuestion), nameof(IsFillingFolders));
        FillFoldersNoCommand.RaiseCanExecuteChanged();
        FillFoldersYesCommand.RaiseCanExecuteChanged();
        FillFoldersContinueCommand.RaiseCanExecuteChanged();
        OnPropertiesChanged(nameof(CanTryTeam), nameof(Step1Hint));
        TryTeamCommand?.RaiseCanExecuteChanged();
        RefreshStep1Tabs();
        SyncQuestion();
        SyncWaitPoint();
    }
}
