using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// What the gallery's « Import as is » needs from its wizard (STUDIO-41): where teams land, the
/// window's Novice/Expert switch — the action is the expert's (D-01) — and the way to My teams.
/// </summary>
/// <param name="TeamsRoot">The teams root an imported case lands in.</param>
/// <param name="Mode">The window's switch: the action exists in expert mode only.</param>
/// <param name="OpenTeams">Brings My teams forward, on the team at the path given.</param>
internal sealed record UseCaseImportSeams(string TeamsRoot, UiModeViewModel Mode, Action<string> OpenTeams);

/// <summary>
/// « Import as is » (STUDIO-41): from the gallery, an expert turns a use case into a team of My
/// teams — ready to run with its data, and modifiable — without the workshop. The name the team
/// would take, the case's title in the window's language, is checked first, by the adoption's own
/// rule (D-05, STUDIO-26 D-07): when something already holds its folder, the banner says what,
/// offers the free name and the way to the team holding this one, and nothing is exported until
/// the name is free. Then the CLI writes the case as a team folder and the import every team takes
/// brings it into the teams root (<see cref="UseCaseImporter"/>); the banner ends on the adoption's
/// own line and on the Import screen's report on the folders — what the team reads, what it writes.
/// One import at a time; a reference-only case is never offered (D-03).
/// </summary>
public sealed class UseCaseImportViewModel : ObservableObject
{
    private readonly UseCaseClient? _client;
    private readonly UseCaseImportSeams? _seams;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private readonly Func<string> _language;
    private readonly Action _closeGallery;

    /// <summary>The case the banner speaks of, and the name the team takes.</summary>
    private UseCase? _useCase;
    private string _name = "";
    /// <summary>What holds the name's folder (D-05); None while nothing does.</summary>
    private TeamFolderOccupant _occupant;
    private string _takenFolder = "";
    private string _occupantName = "";
    private string? _freeName;
    private string? _freeFolder;
    private bool _isImporting;
    private string? _failure;
    private string? _importedTeam;
    private IReadOnlyList<string> _importedMounts = [];

    internal UseCaseImportViewModel(
        UseCaseClient? client,
        UseCaseImportSeams? seams,
        IUiDispatcher dispatcher,
        IStudioStrings strings,
        Func<string> language,
        Action closeGallery)
    {
        _client = client;
        _seams = seams;
        _dispatcher = dispatcher;
        _strings = strings;
        _language = language;
        _closeGallery = closeGallery;

        UseFreeNameCommand = new AsyncRelayCommand(UseFreeNameAsync, () => _freeName is not null && !_isImporting);
        OpenConflictingTeamCommand = new RelayCommand(() => OpenTeams(_takenFolder), () => CanOpenConflictingTeam);
        OpenTeamsCommand = new RelayCommand(() => OpenTeams(_importedTeam!), () => _importedTeam is not null);
        DismissCommand = new RelayCommand(Clear, () => !_isImporting);

        if (seams is not null)
            seams.Mode.PropertyChanged += (_, e) => OnModeChanged(e.PropertyName);

        // The banner's sentences follow the window's language; the team's name does not.
        _strings.CultureChanged += (_, _) => OnPropertiesChanged(
            nameof(Conflict), nameof(UseFreeNameLabel), nameof(ImportingLabel), nameof(Failure),
            nameof(ImportedLine), nameof(FoldersReport));
    }

    /// <summary>Raised when the cards start or stop offering the action: the expert switch moved.</summary>
    internal event EventHandler? AvailabilityChanged;

    /// <summary>Raised once a case landed in the teams root — the shell refreshes My teams.</summary>
    public event EventHandler<TeamActionEventArgs>? TeamImported;

    /// <summary>Whether the cards offer « Import as is »: in expert mode (D-01), over a CLI to export through.</summary>
    internal bool IsAvailable => _client is not null && _seams is { Mode.IsExpert: true };

    /// <summary>Whether the banner shows: a taken name, an import under way, its failure, or the team it made.</summary>
    public bool HasBanner => HasConflict || IsImporting || HasFailure || HasImported;

    // ── a taken name (D-05) ──

    /// <summary>Whether something holds the folder the name would take.</summary>
    public bool HasConflict => _occupant != TeamFolderOccupant.None;

    /// <summary>What holds it, in the adoption's own words; empty while nothing does.</summary>
    public string Conflict
    {
        get
        {
            var folder = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(_takenFolder));
            return _occupant switch
            {
                TeamFolderOccupant.None => "",
                TeamFolderOccupant.Team => Format(StudioStringKeys.WizardNameTakenTeam, _occupantName, folder),
                TeamFolderOccupant.Folder => Format(StudioStringKeys.WizardNameTakenFolder, folder),
                _ => Format(StudioStringKeys.WizardNameTakenFile, folder),
            };
        }
    }

    /// <summary>The free name the banner proposes — « Daily email digest (2) »; null while nothing is taken.</summary>
    public string? FreeName => _freeName;

    /// <summary>« Import as “Daily email digest (2)” ».</summary>
    public string UseFreeNameLabel => _freeName is { } name ? Format(StudioStringKeys.WizardGalleryImportAs, name) : "";

    /// <summary>Imports under the free name, into the free folder it was chosen with.</summary>
    public AsyncRelayCommand UseFreeNameCommand { get; }

    /// <summary>Whether a team holds the taken folder — the only occupant there is a way to.</summary>
    public bool CanOpenConflictingTeam => _occupant == TeamFolderOccupant.Team;

    /// <summary>« Open the existing team »: the gallery closes, My teams comes forward.</summary>
    public RelayCommand OpenConflictingTeamCommand { get; }

    // ── the import ──

    /// <summary>Whether an import is running.</summary>
    public bool IsImporting
    {
        get => _isImporting;
        private set => SetProperty(ref _isImporting, value);
    }

    /// <summary>« Importing “Daily email digest”… ».</summary>
    public string ImportingLabel => Format(StudioStringKeys.WizardGalleryImporting, _name);

    /// <summary>Whether the last import did not land.</summary>
    public bool HasFailure => _failure is not null;

    /// <summary>« Not imported — » and why, in the CLI's words or the import's; empty while nothing failed.</summary>
    public string Failure => _failure is { } detail ? Format(StudioStringKeys.ImportRefused, detail) : "";

    /// <summary>Whether a case just became a team.</summary>
    public bool HasImported => _importedTeam is not null;

    /// <summary>The adoption's own line (STUDIO-20): « Team “Daily email digest” is saved in My teams. »</summary>
    public string ImportedLine => HasImported ? Format(StudioStringKeys.WizardAdoptedLine, _name) : "";

    /// <summary>
    /// The Import screen's report on the folders, for the team just imported: the folders it reads
    /// and writes, spelled the way its agents address them — never this machine's paths (ADR-008).
    /// </summary>
    public ImportCheckViewModel? FoldersReport
    {
        get
        {
            if (!HasImported)
                return null;

            return _importedMounts.Count > 0
                ? new ImportCheckViewModel(
                    _strings[StudioStringKeys.ImportMountsDeclared],
                    Format(StudioStringKeys.ImportMountsDeclaredDetail, _importedMounts.Count, MountLabels.DescribeAll(_importedMounts, _strings, ", ")),
                    "ok")
                : new ImportCheckViewModel(
                    _strings[StudioStringKeys.ImportMountsNone], _strings[StudioStringKeys.ImportMountsNoneDetail], "info");
        }
    }

    /// <summary>« Open My teams », on the team just imported.</summary>
    public RelayCommand OpenTeamsCommand { get; }

    /// <summary>The banner's ✕ — never while an import runs.</summary>
    public RelayCommand DismissCommand { get; }

    /// <summary>
    /// A card's « Import as is »: the name first, then the import. A second click while one runs,
    /// a reference-only case, or novice mode does nothing.
    /// </summary>
    internal Task StartAsync(UseCase useCase)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        if (!IsAvailable || !useCase.Importable || _isImporting)
            return Task.CompletedTask;

        Clear();
        var name = UseCaseImporter.TeamNameOf(useCase, _language());
        return ImportUnlessTakenAsync(useCase, name, UseCaseImporter.TeamFolderOf(useCase, name, _seams!.TeamsRoot));
    }

    /// <summary>Clears the banner — a new visit of the gallery starts without the last one's. Not while an import runs.</summary>
    internal void Clear()
    {
        if (_isImporting)
            return;

        _useCase = null;
        _name = "";
        _occupant = TeamFolderOccupant.None;
        _takenFolder = "";
        _occupantName = "";
        _freeName = null;
        _freeFolder = null;
        _failure = null;
        _importedTeam = null;
        _importedMounts = [];
        RaiseBanner();
    }

    private Task UseFreeNameAsync()
    {
        if (_useCase is not { } useCase || _freeName is not { } name || _freeFolder is not { } folder)
            return Task.CompletedTask;

        Clear();
        // Taken too, since? The same rule answers again, with the next free name.
        return ImportUnlessTakenAsync(useCase, name, folder);
    }

    /// <summary>
    /// Says what holds <paramref name="folder"/>, when something does, and prepares the free name
    /// that goes with the free folder; imports otherwise.
    /// </summary>
    private Task ImportUnlessTakenAsync(UseCase useCase, string name, string folder)
    {
        _useCase = useCase;
        _name = name;

        var occupant = TeamCatalog.OccupantOf(folder);
        if (occupant == TeamFolderOccupant.None)
            return ImportAsync(useCase, name, folder);

        _occupant = occupant;
        _takenFolder = folder;
        _occupantName = occupant == TeamFolderOccupant.Team ? TeamCatalog.NormalizeName(TeamCatalog.Describe(folder).Name) : "";
        _freeFolder = TeamCatalog.FreeSibling(folder);
        _freeName = TeamCatalog.FreeName(name, folder, _freeFolder);
        RaiseBanner();
        return Task.CompletedTask;
    }

    [SuppressMessage("Design", "CA1031", Justification =
        "The import's fault barrier: whatever stops it — an exception out of the launch or the disk "
        + "included — is a line in the banner, never a dead task or a message box.")]
    private async Task ImportAsync(UseCase useCase, string name, string folder)
    {
        IsImporting = true;
        RaiseBanner();

        UseCaseImportResult result;
        IReadOnlyList<string> mounts = [];
        try
        {
            result = await UseCaseImporter
                .ImportAsync(_client!, useCase.Id, name, folder, _seams!.TeamsRoot, _language())
                .ConfigureAwait(false);
            // Read here, off the UI thread: the report names the folders the team really has.
            if (result.TeamPath is { } team)
                mounts = TeamCatalog.Describe(team).Mounts;
        }
        catch (Exception exception)
        {
            result = new UseCaseImportResult
            {
                Failure = new UseCaseFailure(UseCaseFailureKind.Stopped, $"{exception.GetType().Name}: {exception.Message}"),
            };
        }

        await PostAndAwaitAsync(() => Land(result, mounts)).ConfigureAwait(false);
    }

    private void Land(UseCaseImportResult result, IReadOnlyList<string> mounts)
    {
        IsImporting = false;
        if (result.TeamPath is { } team)
        {
            _importedTeam = team;
            _importedMounts = mounts;
        }
        else
        {
            _failure = result.Failure?.Detail ?? "";
        }

        RaiseBanner();
        if (result.TeamPath is { } imported)
            TeamImported?.Invoke(this, new TeamActionEventArgs(imported));
    }

    private void OpenTeams(string team)
    {
        _closeGallery();
        _seams?.OpenTeams(team);
    }

    private void OnModeChanged(string? propertyName)
    {
        if (propertyName != nameof(UiModeViewModel.IsExpert))
            return;

        // Back to novice: the expert's banner goes with the expert's action.
        if (!IsAvailable)
            Clear();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseBanner()
    {
        OnPropertiesChanged(
            nameof(HasBanner), nameof(HasConflict), nameof(Conflict), nameof(FreeName), nameof(UseFreeNameLabel),
            nameof(CanOpenConflictingTeam), nameof(ImportingLabel), nameof(HasFailure), nameof(Failure),
            nameof(HasImported), nameof(ImportedLine), nameof(FoldersReport));
        UseFreeNameCommand.RaiseCanExecuteChanged();
        OpenConflictingTeamCommand.RaiseCanExecuteChanged();
        OpenTeamsCommand.RaiseCanExecuteChanged();
        DismissCommand.RaiseCanExecuteChanged();
    }

    private string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, _strings[key], arguments);

    /// <summary>Posts <paramref name="action"/> and completes once it ran on the UI thread.</summary>
    private Task PostAndAwaitAsync(Action action)
    {
        var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() =>
        {
            try
            {
                action();
            }
            finally
            {
                landed.TrySetResult();
            }
        });
        return landed.Task;
    }
}
