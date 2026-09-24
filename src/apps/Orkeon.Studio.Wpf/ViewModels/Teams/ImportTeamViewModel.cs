using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>One line of the recognition report (audit 04/12).</summary>
/// <param name="Title">What was checked, in plain language.</param>
/// <param name="Detail">The finding, faint under the title.</param>
/// <param name="Tone">ok | warn | info — the view maps it to an icon.</param>
public sealed record ImportCheckViewModel(string Title, string Detail, string Tone);

/// <summary>
/// The import seam — <see cref="TeamCatalog.Import"/> by default, dated on the screen's clock (a
/// copy's arrival is its activity, STUDIO-32): copies the source into the teams root and returns the
/// new team folder, or null with <paramref name="refusal"/> set when the source was refused (null
/// when the disk was the reason).
/// </summary>
/// <param name="sourcePath">The folder or file to import.</param>
/// <param name="root">The teams root.</param>
/// <param name="refusal">Why the source was refused, when it was.</param>
public delegate string? ImportTeamAction(string sourcePath, string root, out string? refusal);

/// <summary>
/// The import screen's seams: the collaborators it otherwise builds itself. They travel as
/// one record rather than as nine constructor parameters — the screen has one real caller
/// (the shell) and a row of tests, each naming two or three of these and leaving the rest
/// to the real catalogs.
/// </summary>
public sealed record ImportTeamDependencies
{
    /// <summary>Recognizes the candidate; the launcher's own detector when null.</summary>
    public ITargetProbe? TargetProbe { get; init; }

    /// <summary>The OS file/folder dialogs; none when null.</summary>
    public IPathPicker? Picker { get; init; }

    /// <summary>The localized strings; English when null.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>Where the adopted teams live; the default teams root when null.</summary>
    public string? TeamsRoot { get; init; }

    /// <summary>Scans the candidate for pasted secrets; the real scan when null.</summary>
    public Func<string, IReadOnlyList<string>>? ScanSecrets { get; init; }

    /// <summary>Copies the candidate into the teams root; <see cref="TeamCatalog.Import"/> when null.</summary>
    public ImportTeamAction? Import { get; init; }

    /// <summary>The clock an import dates the team's arrival on (STUDIO-32); the system's when null.</summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>Reads the settings' mounts, so unknown ids are reported (VFS-90, D-06); not consulted when null.</summary>
    public Func<IReadOnlyList<string>>? DeclaredMounts { get; init; }

    /// <summary>Declares a folder in the settings under the id the team carries; the review card offers nothing when null.</summary>
    public Action<MountDefinition>? DeclareMount { get; init; }

    /// <summary>Saves the settings after the declarations; skipped when null.</summary>
    public Func<Task<bool>>? SaveSettings { get; init; }
}

/// <summary>
/// The "Importer" screen (design v3): point at a shared team — a folder, a YAML crew, an
/// <c>.ork.ts</c> script — Studio recognizes it with the launcher's own detector, scans it
/// for pasted secrets, and copies it into the teams root only on your say-so. Nothing is
/// copied before the confirmation, and a definition carrying an inline API key is warned
/// about loudly: keys travel through the environment, never inside a shared folder.
/// </summary>
public sealed class ImportTeamViewModel : ObservableObject
{
    private readonly string _teamsRoot;
    private readonly IStudioStrings _strings;
    private readonly Func<string, IReadOnlyList<string>> _scanSecrets;
    private readonly ImportTeamAction _import;
    private string? _statusMessage;

    /// <summary>Builds the screen over its seams; every collaborator defaults to the real one.</summary>
    public ImportTeamViewModel(ImportTeamDependencies? dependencies = null)
    {
        var wired = dependencies ?? new ImportTeamDependencies();
        _teamsRoot = wired.TeamsRoot ?? TeamCatalog.DefaultRoot();
        _strings = wired.Strings ?? EnglishStudioStrings.Instance;
        _scanSecrets = wired.ScanSecrets ?? TeamCatalog.FindInlineSecrets;
        var clock = wired.Clock ?? TimeProvider.System;
        _import = wired.Import
            ?? ((string sourcePath, string root, out string? refusal) =>
                TeamCatalog.Import(sourcePath, root, clock.GetUtcNow(), out refusal));
        _declaredMounts = wired.DeclaredMounts;
        _declareMount = wired.DeclareMount;
        _saveSettings = wired.SaveSettings;

        Target = new TargetSelectionViewModel(wired.TargetProbe, wired.Picker, wired.Strings);
        Target.TargetChanged += (_, _) => OnTargetChanged();

        ImportCommand = new RelayCommand(Import, () => Target.Target is not null);
        DeclareCopiesCommand = new AsyncRelayCommand(DeclareCopiesAsync, () => UnknownMounts.Count > 0 && _declareMount is not null);
    }

    private readonly Func<IReadOnlyList<string>>? _declaredMounts;
    private readonly Action<MountDefinition>? _declareMount;
    private readonly Func<Task<bool>>? _saveSettings;

    /// <summary>
    /// The candidate's folders that name a declaration this machine does not have (VFS-90,
    /// D-06): the exporting machine's ids. Empty when the settings were not consulted.
    /// </summary>
    public IReadOnlyList<ResolvedTeamMount> UnknownMounts { get; private set; } = [];

    /// <summary>Whether the candidate refers to declarations missing here.</summary>
    public bool HasUnknownMounts => UnknownMounts.Count > 0;

    /// <summary>
    /// "Authorize them as recorded": declares each unknown folder in the settings under the
    /// SAME id the team carries (D-06), saves, and re-reads the candidate — the team then
    /// names declarations this machine has, and the launcher stops refusing it.
    /// </summary>
    public AsyncRelayCommand DeclareCopiesCommand { get; }

    private async Task DeclareCopiesAsync()
    {
        if (_declareMount is null)
            return;

        foreach (var mount in UnknownMounts)
        {
            if (MountDefinition.TryParse(mount.Raw, out var copy, out _))
                _declareMount(copy);
        }

        if (_saveSettings is not null)
            await _saveSettings().ConfigureAwait(true);

        OnTargetChanged();
    }

    /// <summary>Raised when a team landed in the teams root.</summary>
    public event EventHandler<TeamActionEventArgs>? TeamImported;

    /// <summary>The path picker + detector — the launcher's own.</summary>
    public TargetSelectionViewModel Target { get; }

    /// <summary>Files of the candidate carrying what looks like an inline secret.</summary>
    public ObservableCollection<string> SecretWarnings { get; } = [];

    /// <summary>
    /// The recognition report (audit 04/12): what Studio could honestly establish about the
    /// candidate — the recognized shape and name, the secret scan, and the reminder that
    /// tools are checked on first launch. Empty until a target resolves.
    /// </summary>
    public ObservableCollection<ImportCheckViewModel> RecognitionReport { get; } = [];

    /// <summary>Whether the report card shows.</summary>
    public bool HasRecognitionReport => RecognitionReport.Count > 0;

    /// <summary>Whether the secret warning block shows.</summary>
    public bool HasSecretWarnings => SecretWarnings.Count > 0;

    /// <summary>Outcome line of the last import attempt.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Copies the recognized team into the teams root.</summary>
    public RelayCommand ImportCommand { get; }

    private void OnTargetChanged()
    {
        SecretWarnings.Clear();
        RecognitionReport.Clear();
        if (Target.Target is { } target)
        {
            foreach (var file in _scanSecrets(target.SelectedPath))
                SecretWarnings.Add(file);

            BuildRecognitionReport(target);
        }

        OnPropertyChanged(nameof(HasSecretWarnings));
        OnPropertyChanged(nameof(HasRecognitionReport));
        ImportCommand.RaiseCanExecuteChanged();
    }

    private void BuildRecognitionReport(RunTarget target)
    {
        var kindKey = target.Kind switch
        {
            RunTargetKind.YamlFile => StudioStringKeys.TargetKindYamlFile,
            RunTargetKind.ScriptFile => StudioStringKeys.TargetKindScriptFile,
            RunTargetKind.MultiFileCrewDirectory => StudioStringKeys.TargetKindCrewDirectory,
            RunTargetKind.SingleFileCrewDirectory => StudioStringKeys.TargetKindSingleFileCrewDirectory,
            _ => StudioStringKeys.TargetKindScriptDirectory,
        };
        var described = TeamCatalog.DescribeTarget(target.SelectedPath, _declaredMounts?.Invoke());
        UnknownMounts = described.ResolvedMounts.Where(m => m.Source == TeamMountSource.UnknownId).ToList();
        OnPropertiesChanged(nameof(UnknownMounts), nameof(HasUnknownMounts));
        DeclareCopiesCommand.RaiseCanExecuteChanged();
        var name = described.Name ?? target.SelectedPath;
        var detail = described.AgentCount is { } agents
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ImportRecognizedAgents], name, agents)
            : name;
        RecognitionReport.Add(new ImportCheckViewModel(_strings[kindKey], detail, "ok"));

        RecognitionReport.Add(SecretWarnings.Count == 0
            ? new ImportCheckViewModel(
                _strings[StudioStringKeys.ImportSecretsClean],
                _strings[StudioStringKeys.ImportSecretsCleanDetail], "ok")
            : new ImportCheckViewModel(
                _strings[StudioStringKeys.ImportSecretsFound],
                string.Format(CultureInfo.CurrentCulture,
                    _strings[StudioStringKeys.ImportSecretsFoundDetail], SecretWarnings.Count), "warn"));

        // The imported sidecar's declared folders, when it carries any (F-08): a crew
        // parser is still out of scope, but a Studio-adopted team travels with its list.
        if (described.Mounts.Count > 0)
        {
            RecognitionReport.Add(new ImportCheckViewModel(
                _strings[StudioStringKeys.ImportMountsDeclared],
                string.Format(CultureInfo.CurrentCulture,
                    _strings[StudioStringKeys.ImportMountsDeclaredDetail],
                    described.Mounts.Count,
                    // The way the agents address them, never the exporting machine's folders
                    // — an import report is read by whoever received the team (ADR-008).
                    MountLabels.DescribeAll(described.Mounts, _strings, ", ")), "ok"));
        }
        else
        {
            RecognitionReport.Add(new ImportCheckViewModel(
                _strings[StudioStringKeys.ImportMountsNone],
                _strings[StudioStringKeys.ImportMountsNoneDetail], "info"));
        }

        // VFS-90 D-06: a folder naming a declaration this machine does not have. Said before
        // the copy, with the way out — the launcher would otherwise refuse the team afterwards.
        if (UnknownMounts.Count > 0)
        {
            RecognitionReport.Add(new ImportCheckViewModel(
                string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ImportUnknownMountIds], UnknownMounts.Count),
                MountLabels.DescribeAll(UnknownMounts.Select(m => m.Raw).ToList(), _strings, ", "),
                "warn"));
        }

        RecognitionReport.Add(new ImportCheckViewModel(
            _strings[StudioStringKeys.ImportToolsLater],
            _strings[StudioStringKeys.ImportToolsLaterDetail], "info"));
    }

    private void Import()
    {
        if (Target.Target is not { } target)
            return;

        var destination = _import(target.SelectedPath, _teamsRoot, out var refusal);
        if (destination is null)
        {
            // A silent null would read as "nothing happened" — which is also what a
            // successful click looks like to someone who missed the card refresh. A refused
            // source says why (STUDIO-12 C1); a disk failure keeps the generic line.
            StatusMessage = refusal is { Length: > 0 }
                ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ImportRefused], refusal)
                : _strings[StudioStringKeys.ImportFailed];
            return;
        }

        StatusMessage = destination;
        TeamImported?.Invoke(this, new TeamActionEventArgs(destination));
    }
}
