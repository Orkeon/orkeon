using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// One line of the Folders step (STUDIO-46): a folder the forge proposed — the name the agents
/// will use, editable; whether the team reads or writes it; and the real folder behind it: one
/// the user chose on the disk, or one inside the team (the default of an output).
/// </summary>
public sealed class WizardFolderRow : ObservableObject
{
    private readonly IStudioStrings _strings;
    private readonly Action<WizardFolderRow, string> _pathChanged;
    private string _virtualPath;
    private string? _directory;
    private bool _isInsideTeam;

    internal WizardFolderRow(
        ForgeFolder proposed,
        IStudioStrings strings,
        Action<WizardFolderRow> pick,
        Action<WizardFolderRow, string> pathChanged)
    {
        _strings = strings;
        _pathChanged = pathChanged;
        _virtualPath = proposed.Path;
        Role = proposed.Role;
        Purpose = proposed.Purpose ?? "";
        _directory = proposed.Directory;
        // « Inside the team » by default for an output (STUDIO-46, D-4): its results then travel
        // with the team. An input waits for a folder to be chosen.
        _isInsideTeam = proposed.Directory is null && !proposed.IsInput;
        PickCommand = new RelayCommand(() => pick(this));
        InsideTeamCommand = new RelayCommand(AnswerInsideTeam, () => !_isInsideTeam);
    }

    /// <summary>The name the agents will use (<c>/inpdf</c>), as the user edits it.</summary>
    public string VirtualPath
    {
        get => _virtualPath;
        set
        {
            var previous = _virtualPath;
            if (!SetProperty(ref _virtualPath, value ?? ""))
                return;

            // A folder chosen on the disk was declared under the previous name: it no longer
            // answers this one. The row falls back to its default answer.
            if (_directory is not null)
            {
                _directory = null;
                _isInsideTeam = !IsInput;
                RaiseAnswer();
            }

            _pathChanged(this, previous);
        }
    }

    /// <summary><c>input</c> or <c>output</c>, as the forge proposed it.</summary>
    public string Role { get; }

    /// <summary>Whether the team only reads this folder.</summary>
    public bool IsInput => string.Equals(Role, ForgeFolder.InputRole, StringComparison.Ordinal);

    /// <summary>« read » or « written », in the user's language.</summary>
    public string RoleLabel => _strings[IsInput ? StudioStringKeys.WizardFolderRoleInput : StudioStringKeys.WizardFolderRoleOutput];

    /// <summary>What the folder holds, in the request's words; empty when the forge said nothing.</summary>
    public string Purpose { get; }

    /// <summary>Whether the forge said what the folder holds.</summary>
    public bool HasPurpose => Purpose.Length > 0;

    /// <summary>The real folder the user chose behind it; null while none is.</summary>
    public string? Directory => _directory;

    /// <summary>Whether the folder will live inside the team.</summary>
    public bool IsInsideTeam => _isInsideTeam;

    /// <summary>Whether a real folder of the user's answers it.</summary>
    public bool HasDirectory => _directory is not null;

    /// <summary>Whether nothing answers it yet — an input left for the engine's default.</summary>
    public bool IsUnanswered => !_isInsideTeam && _directory is null;

    /// <summary>What the row shows on its right: the folder, « inside the team: x », or « not chosen yet ».</summary>
    public string FolderLabel =>
        _directory
        ?? (_isInsideTeam
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardInsideTeamFolder], FolderName)
            : _strings[StudioStringKeys.WizardFolderNotChosen]);

    /// <summary>The folder's name inside the team: <c>input</c> for <c>/workspace</c>, its own otherwise.</summary>
    private string FolderName =>
        _virtualPath.Trim('/') is { Length: > 0 } ? TeamMountPaths.FolderFor(_virtualPath) : "";

    /// <summary>« Choose a folder… »: the disk picker, behind this row.</summary>
    public RelayCommand PickCommand { get; }

    /// <summary>« Inside the team »: the folder will be the team's own.</summary>
    public RelayCommand InsideTeamCommand { get; }

    /// <summary>The rights the team takes on the folder behind it.</summary>
    internal MountRights Rights => IsInput ? MountRights.ReadOnly : MountRights.ReadWrite;

    /// <summary>A real folder now answers the row.</summary>
    internal void AnswerWithDirectory(string directory)
    {
        _directory = directory;
        _isInsideTeam = false;
        RaiseAnswer();
    }

    /// <summary>The row's folder will be the team's own.</summary>
    internal void AnswerInsideTeam()
    {
        _directory = null;
        _isInsideTeam = true;
        RaiseAnswer();
    }

    private void RaiseAnswer()
    {
        OnPropertiesChanged(
            nameof(Directory), nameof(IsInsideTeam), nameof(HasDirectory), nameof(IsUnanswered), nameof(FolderLabel));
        InsideTeamCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// The Folders step (STUDIO-46): between the brief and the plan, the forge proposes the folders
/// the request named — or <c>/workspace</c> and <c>/output</c> when it named none — and waits.
/// The panel shows them; the user renames them, binds each to a real folder or keeps it inside
/// the team, and confirms. The confirmed list is the team's folders from then on: the plan, the
/// trial, the Composer rows and the sidecar all follow it.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    /// <summary>The forge's own session root — no team folder may take it.</summary>
    private const string ForgeRoot = "/forge";

    /// <summary>The proposal the rows were last built from — a new proposal rebuilds them.</summary>
    private IReadOnlyList<ForgeFolder>? _proposalShown;

    /// <summary>The rows of the Folders panel, in the order the forge proposed them.</summary>
    public ObservableCollection<WizardFolderRow> FolderRows { get; } = [];

    /// <summary>Whether the forge waits for the folders to be confirmed — the panel shows.</summary>
    public bool IsFoldersStep => _model.FoldersPending && FolderRows.Count > 0;

    /// <summary>« Confirm the folders ».</summary>
    public RelayCommand ConfirmFoldersCommand { get; private set; } = null!;

    /// <summary>
    /// What stops the confirmation, in words: a name that is no folder, a name twice, or no
    /// folder the team writes to. Empty while the list can be confirmed.
    /// </summary>
    public string FoldersProblem
    {
        get
        {
            var roots = new List<string>();
            foreach (var row in FolderRows)
            {
                if (!TryNormalizeRootName(row.VirtualPath, out var root))
                    return string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardFolderInvalidName], row.VirtualPath);
                if (roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    return string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.WizardFolderTwice], root);
                roots.Add(root);
            }

            return FolderRows.Any(row => !row.IsInput) ? "" : _strings[StudioStringKeys.WizardFolderNoOutput];
        }
    }

    /// <summary>Whether <see cref="FoldersProblem"/> has something to say.</summary>
    public bool HasFoldersProblem => FoldersProblem.Length > 0;

    /// <summary>Whether the list can go back to the engine.</summary>
    public bool CanConfirmFolders => IsFoldersStep && IsEngineRunning && !HasFoldersProblem;

    private void InitializeFolderStep() =>
        ConfirmFoldersCommand = new RelayCommand(ConfirmFolders, () => CanConfirmFolders);

    /// <summary>Rebuilds the rows when the engine proposes (again), and republishes the panel.</summary>
    private void SyncFolderStep()
    {
        if (_model.FoldersPending && !ReferenceEquals(_model.ProposedFolders, _proposalShown))
        {
            _proposalShown = _model.ProposedFolders;
            FolderRows.Clear();
            foreach (var proposed in _model.ProposedFolders)
                FolderRows.Add(new WizardFolderRow(proposed, _strings, PickRowFolder, OnFolderRowPathChanged));
        }

        RefreshFolderStep();
    }

    private void RefreshFolderStep()
    {
        OnPropertiesChanged(nameof(IsFoldersStep), nameof(FoldersProblem), nameof(HasFoldersProblem), nameof(CanConfirmFolders));
        ConfirmFoldersCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Forgets the panel — another creation starts, or a session is resumed.</summary>
    private void ClearFolderStep()
    {
        _proposalShown = null;
        FolderRows.Clear();
        RefreshFolderStep();
    }

    /// <summary>« Choose a folder… » on a row: the disk picker, declared under the row's name.</summary>
    private void PickRowFolder(WizardFolderRow row)
    {
        if (TryNormalizeRootName(row.VirtualPath, out var root))
            RequestPickFolder(root, row.Rights);
    }

    /// <summary>A renamed row takes the binding of its previous name off the team's mounts.</summary>
    private void OnFolderRowPathChanged(WizardFolderRow row, string previous)
    {
        if (TryNormalizeRootName(previous, out var root)
            && !FolderRows.Any(other => !ReferenceEquals(other, row) && string.Equals(other.VirtualPath, previous, StringComparison.Ordinal)))
        {
            RemoveBinding(root);
        }

        RefreshMountSurfaces();
        RefreshFolderStep();
    }

    /// <summary>
    /// The folder the shell bound behind <paramref name="virtualPath"/> answers the panel's row
    /// of that name too, while the folders wait for their confirmation.
    /// </summary>
    private void AnswerFolderRow(string virtualPath, MountDefinition mount)
    {
        if (!_model.FoldersPending)
            return;

        foreach (var row in FolderRows)
        {
            if (TryNormalizeRootName(row.VirtualPath, out var root) && string.Equals(root, virtualPath, StringComparison.Ordinal))
                row.AnswerWithDirectory(mount.PhysicalPath);
        }
    }

    /// <summary>
    /// « Confirm the folders »: the list goes back to the engine as the user left it — each name
    /// normalized, each real folder with it — and the team's mounts take the answers: a chosen
    /// folder is bound already, a folder inside the team is bound team-relative now.
    /// </summary>
    private void ConfirmFolders()
    {
        if (!CanConfirmFolders)
            return;

        var folders = new List<ForgeFolder>();
        foreach (var row in FolderRows)
        {
            TryNormalizeRootName(row.VirtualPath, out var root);
            folders.Add(new ForgeFolder(root, row.Role, row.Purpose.Length > 0 ? row.Purpose : null, row.Directory));
        }

        if (!_client.SendFolders(folders))
        {
            StatusMessage = _strings[StudioStringKeys.WizardAssistantNotRunning];
            return;
        }

        foreach (var (row, folder) in FolderRows.Zip(folders))
        {
            if (row.IsInsideTeam)
                BindInsideTeam(folder.Path, row.Rights);
        }

        _model.AcknowledgeFolders(folders);
        RefreshMountSurfaces();
        SyncFromModel();
    }

    /// <summary>
    /// A resumed session's confirmed folders bound to real ones (<c>folders.json</c>): each gets
    /// its binding back — the settings declaration of that folder under that name when there is
    /// one, the folder as it is otherwise — so the Composer rows and the sidecar keep it.
    /// </summary>
    private void SeedFolderBindings()
    {
        var declared = _declaredMounts();
        foreach (var folder in _model.Folders)
        {
            if (folder.Directory is not { Length: > 0 } directory || BoundEntry(folder.Path) is not null)
                continue;

            var rights = folder.IsInput ? MountRights.ReadOnly : MountRights.ReadWrite;
            var entry = declared.FirstOrDefault(candidate =>
                MountDefinition.TryParse(candidate, out var mount, out _)
                && mount.SameRootAs(folder.Path)
                && string.Equals(MountDefinition.NormalizeFolder(mount.PhysicalPath), MountDefinition.NormalizeFolder(directory), StringComparison.Ordinal));
            TeamMounts.Add(entry ?? new MountDefinition { PhysicalPath = directory, VirtualPath = folder.Path, Rights = rights }.ToMountString());
        }

        RefreshMountSurfaces();
    }

    /// <summary>
    /// A typed name as the virtual root it means: trimmed, its slashes shed, lowercased the
    /// way the picker derives a name from a folder, one segment of the characters the engine
    /// accepts, valid for the runtime and not one of the roots it reserves
    /// (<see cref="MountDefinition.IsValidVirtualPath"/>, which <c>/forge</c> joins here).
    /// </summary>
    private static bool TryNormalizeRootName(string? text, out string root)
    {
        root = "";
        // The engine's own rule (STUDIO-46): one segment of letters, digits, '-', '_' or '.' —
        // anything else comes back refused, and is better said here, before the confirm.
        var name = (text ?? "").Trim().Trim('/', '\\').Trim();
        if (name.Length == 0 || name is "." or ".." || !name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
            return false;

#pragma warning disable CA1308 // virtual roots are lowercase by convention, not a normalization round-trip
        var candidate = "/" + name.ToLowerInvariant();
#pragma warning restore CA1308
        if (!MountDefinition.IsValidVirtualPath(candidate) || candidate == ForgeRoot)
            return false;

        root = candidate;
        return true;
    }
}
