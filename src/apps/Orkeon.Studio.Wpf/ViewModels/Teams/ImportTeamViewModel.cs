using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

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
        if (Target.Target is { } target)
        {
            foreach (var file in _scanSecrets(target.SelectedPath))
                SecretWarnings.Add(file);
        }

        OnPropertyChanged(nameof(HasSecretWarnings));
        ImportCommand.RaiseCanExecuteChanged();
    }

    private void Import()
    {
        if (Target.Target is not { } target)
            return;

        var destination = _import(target.SelectedPath, _teamsRoot);
        if (destination is null)
        {
            StatusMessage = null;
            return;
        }

        StatusMessage = destination;
        TeamImported?.Invoke(this, new TeamActionEventArgs(destination));
    }
}
