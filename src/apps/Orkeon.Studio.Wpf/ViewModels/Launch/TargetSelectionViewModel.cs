using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Launch;

/// <summary>
/// The target picker of spec §5.1: the three accepted shapes, detected automatically.
/// <para>
/// Ambiguity is never resolved silently. A directory that is both a multi-file crew and a script
/// entry point stays unresolved, naming both candidates, until the user picks
/// <see cref="PreferredDirectoryKind"/> — the same policy the multi-file loader applies.
/// </para>
/// </summary>
public sealed class TargetSelectionViewModel : ObservableObject
{
    private readonly RunTargetDetector _detector;
    private readonly IPathPicker _picker;
    private readonly IStudioStrings _strings;
    private string _selectedPath = "";
    private RunTargetDetection? _detection;
    private RunTargetKind? _preferredDirectoryKind;
    private string? _selectedCandidate;

    /// <summary>Builds the picker over a target probe and the browse dialogs.</summary>
    public TargetSelectionViewModel(
        ITargetProbe? probe = null,
        IPathPicker? picker = null,
        IStudioStrings? strings = null)
    {
        _detector = new RunTargetDetector(probe);
        _picker = picker ?? NullPathPicker.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) =>
            OnPropertiesChanged(nameof(StatusDisplay), nameof(DirectoryRunNotice));

        BrowseFileCommand = new RelayCommand(BrowseFile);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        DetectCommand = new RelayCommand(() => Detect(), () => SelectedPath.Length > 0);
        UseCandidateCommand = new RelayCommand(UseSelectedCandidate, () => SelectedCandidate is { Length: > 0 });
    }

    /// <summary>Raised when the resolved target changes, so the option panel can re-gate itself.</summary>
    public event EventHandler? TargetChanged;

    /// <summary>Opens the file browser.</summary>
    public RelayCommand BrowseFileCommand { get; }

    /// <summary>Opens the folder browser.</summary>
    public RelayCommand BrowseFolderCommand { get; }

    /// <summary>Re-runs the detection on <see cref="SelectedPath"/>.</summary>
    public RelayCommand DetectCommand { get; }

    /// <summary>Re-runs the detection on <see cref="SelectedCandidate"/>.</summary>
    public RelayCommand UseCandidateCommand { get; }

    /// <summary>The candidates offered when a directory holds several scripts, or is ambiguous.</summary>
    public ObservableCollection<string> Candidates { get; } = [];

    /// <summary>The two directory shapes, offered when the detector reports an ambiguity.</summary>
    public static IReadOnlyList<RunTargetKind> DirectoryKindChoices { get; } =
        [RunTargetKind.MultiFileCrewDirectory, RunTargetKind.ScriptDirectory];

    /// <summary>The file or folder the user selected.</summary>
    public string SelectedPath
    {
        get => _selectedPath;
        set
        {
            if (SetProperty(ref _selectedPath, value ?? ""))
                DetectCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Which shape to run when a directory matches both, chosen by the user.</summary>
    public RunTargetKind? PreferredDirectoryKind
    {
        get => _preferredDirectoryKind;
        set
        {
            if (SetProperty(ref _preferredDirectoryKind, value) && SelectedPath.Length > 0)
                Detect();
        }
    }

    /// <summary>The candidate highlighted in the list.</summary>
    public string? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetProperty(ref _selectedCandidate, value))
                UseCandidateCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The last detection result, whatever its status.</summary>
    public RunTargetDetection? Detection
    {
        get => _detection;
        private set => SetProperty(ref _detection, value);
    }

    /// <summary>The resolved target, or <see langword="null"/> while unresolved.</summary>
    public RunTarget? Target => Detection?.Target;

    /// <summary>Whether a target is resolved and a launch can be prepared.</summary>
    public bool IsResolved => Detection?.IsResolved ?? false;

    /// <summary>Why the detection failed, when it did.</summary>
    public string? ErrorMessage => Detection?.Error;

    /// <summary>The stable code of the detection failure, e.g. <c>STUDIO-TARGET-AMBIGUOUS</c>.</summary>
    public string? ErrorCode => Detection?.ErrorCode;

    /// <summary>Whether the user must choose among <see cref="Candidates"/>.</summary>
    public bool NeedsSelection => Detection?.Status == RunTargetDetectionStatus.NeedsSelection;

    /// <summary>Whether the directory matched both shapes at once, so a preference is required.</summary>
    public bool IsAmbiguous => Detection?.ErrorCode == RunTargetCodes.AmbiguousDirectory;

    /// <summary>
    /// Whether the shape chooser must stay on screen. Preferring the YAML layout of a contested
    /// directory counts: the CLI rejects such a directory outright, so that answer leaves the
    /// user with an unrunnable target and the other shape still to pick. Hiding the chooser then
    /// would strand them on a failure with no way back.
    /// </summary>
    public bool NeedsShapeChoice =>
        Detection?.ErrorCode is RunTargetCodes.AmbiguousDirectory or RunTargetCodes.YamlLayoutBlockedByScript;

    /// <summary>
    /// Set when the chosen YAML layout cannot be run because the directory also holds a script:
    /// the detector's own remediation, shown as-is since it names the scripts to move.
    /// </summary>
    public string? YamlLayoutBlockedMessage =>
        Detection?.ErrorCode == RunTargetCodes.YamlLayoutBlockedByScript ? Detection.Error : null;

    /// <summary>
    /// The notice shown for a multi-file crew directory, whose launch relies on
    /// <c>orkeon run &lt;directory&gt;</c> (spec §6).
    /// </summary>
    public string? DirectoryRunNotice =>
        Target?.RequiresDirectoryRunSupport == true ? RunTargetRequirements.DirectoryRunNoticeFor(_strings) : null;

    /// <summary>The path that will be handed to <c>orkeon run</c>, which is not always the selected one.</summary>
    public string? RunPath => Target?.RunPath;

    /// <summary>The detected shape, phrased for the status line.</summary>
    public string StatusDisplay => Detection switch
    {
        null => _strings[StudioStringKeys.TargetNone],
        { Status: RunTargetDetectionStatus.Resolved, Target: { } target } => string.Format(
            CultureInfo.InvariantCulture,
            _strings[StudioStringKeys.TargetResolved], Describe(target.Kind), target.RunPath),
        { Status: RunTargetDetectionStatus.NeedsSelection } => string.Format(
            CultureInfo.InvariantCulture,
            _strings[StudioStringKeys.TargetPickScript], Candidates.Count),
        _ => Detection.Error ?? _strings[StudioStringKeys.TargetDetectionFailed],
    };

    /// <summary>The extensions the CLI accepts, shown next to the browse button.</summary>
    public static string SupportedFileExtensions => RunTargetDetector.SupportedFileExtensions;

    /// <summary>Runs the detection on <see cref="SelectedPath"/> and republishes the panel.</summary>
    public RunTargetDetection Detect()
    {
        var detection = _detector.Detect(SelectedPath, PreferredDirectoryKind);
        Detection = detection;

        Candidates.Clear();
        foreach (var candidate in detection.Candidates)
            Candidates.Add(candidate);

        SelectedCandidate = Candidates.Count > 0 ? Candidates[0] : null;

        OnPropertiesChanged(
            nameof(Target),
            nameof(IsResolved),
            nameof(ErrorMessage),
            nameof(ErrorCode),
            nameof(NeedsSelection),
            nameof(IsAmbiguous),
            nameof(NeedsShapeChoice),
            nameof(YamlLayoutBlockedMessage),
            nameof(DirectoryRunNotice),
            nameof(RunPath),
            nameof(StatusDisplay));

        TargetChanged?.Invoke(this, EventArgs.Empty);
        return detection;
    }

    /// <summary>Selects a path and detects it in one step.</summary>
    public RunTargetDetection Select(string path)
    {
        SelectedPath = path ?? "";
        return Detect();
    }

    private string Describe(RunTargetKind kind) => kind switch
    {
        RunTargetKind.YamlFile => _strings[StudioStringKeys.TargetKindYamlFile],
        RunTargetKind.ScriptFile => _strings[StudioStringKeys.TargetKindScriptFile],
        RunTargetKind.MultiFileCrewDirectory => _strings[StudioStringKeys.TargetKindCrewDirectory],
        RunTargetKind.ScriptDirectory => _strings[StudioStringKeys.TargetKindScriptDirectory],
        _ => kind.ToString(),
    };

    private void BrowseFile()
    {
        var picked = _picker.PickFile(
            _strings[StudioStringKeys.DialogSelectCrewDefinition],
            _strings[StudioStringKeys.DialogFilterCrew],
            SelectedPath);

        if (picked is { Length: > 0 })
            Select(picked);
    }

    private void BrowseFolder()
    {
        var picked = _picker.PickFolder(_strings[StudioStringKeys.DialogSelectCrewDirectory], SelectedPath);
        if (picked is { Length: > 0 })
            Select(picked);
    }

    private void UseSelectedCandidate()
    {
        if (SelectedCandidate is { Length: > 0 } candidate)
            Select(candidate);
    }
}
