using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>One line of the recognition report (audit 04/12).</summary>
/// <param name="Title">What was checked, in plain language.</param>
/// <param name="Detail">The finding, faint under the title.</param>
/// <param name="Tone">ok | warn | info — the view maps it to an icon.</param>
public sealed record ImportCheckViewModel(string Title, string Detail, string Tone);

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
    private readonly Func<string, string, string?> _import;
    private string? _statusMessage;

    /// <summary>Builds the screen; every collaborator is optional so tests inject doubles.</summary>
    public ImportTeamViewModel(
        ITargetProbe? targetProbe = null,
        IPathPicker? picker = null,
        IStudioStrings? strings = null,
        string? teamsRoot = null,
        Func<string, IReadOnlyList<string>>? scanSecrets = null,
        Func<string, string, string?>? import = null)
    {
        _teamsRoot = teamsRoot ?? TeamCatalog.DefaultRoot();
        _strings = strings ?? EnglishStudioStrings.Instance;
        _scanSecrets = scanSecrets ?? TeamCatalog.FindInlineSecrets;
        _import = import ?? TeamCatalog.Import;

        Target = new TargetSelectionViewModel(targetProbe, picker, strings);
        Target.TargetChanged += (_, _) => OnTargetChanged();

        ImportCommand = new RelayCommand(Import, () => Target.Target is not null);
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
            _ => StudioStringKeys.TargetKindScriptDirectory,
        };
        var described = TeamCatalog.DescribeTarget(target.SelectedPath);
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

        RecognitionReport.Add(new ImportCheckViewModel(
            _strings[StudioStringKeys.ImportToolsLater],
            _strings[StudioStringKeys.ImportToolsLaterDetail], "info"));
    }

    private void Import()
    {
        if (Target.Target is not { } target)
            return;

        var destination = _import(target.SelectedPath, _teamsRoot);
        if (destination is null)
        {
            // A silent null would read as "nothing happened" — which is also what a
            // successful click looks like to someone who missed the card refresh.
            StatusMessage = _strings[StudioStringKeys.ImportFailed];
            return;
        }

        StatusMessage = destination;
        TeamImported?.Invoke(this, new TeamActionEventArgs(destination));
    }
}
