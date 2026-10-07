using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// One line of the Folders step (STUDIO-46): a folder the forge proposed — or the user added
/// (STUDIO-57) — the name the agents will use, editable; whether the team reads or writes it,
/// flipped by a click; the user's note on what it holds and how the team must use it, which
/// the assistant follows; and the real folder behind it: one the user chose on the disk, or one
/// inside the team (the default of an output). A cross takes the row off the list.
/// </summary>
public sealed class WizardFolderRow : ObservableObject
{
    private readonly IStudioStrings _strings;
    private readonly Action<WizardFolderRow, string> _pathChanged;
    private string _virtualPath;
    private string _role;
    private string _purpose;
    private string? _directory;
    private bool _isInsideTeam;

    internal WizardFolderRow(
        ForgeFolder proposed,
        IStudioStrings strings,
        Action<WizardFolderRow> pick,
        Action<WizardFolderRow, string> pathChanged,
        Action<WizardFolderRow> remove)
    {
        _strings = strings;
        _pathChanged = pathChanged;
        _virtualPath = proposed.Path;
        _role = proposed.Role;
        _purpose = proposed.Purpose ?? "";
        _directory = proposed.Directory;
        // « Inside the team » by default for an output (STUDIO-46, D-4): its results then travel
        // with the team. An input waits for a folder to be chosen.
        _isInsideTeam = proposed.Directory is null && !proposed.IsInput;
        PickCommand = new RelayCommand(() => pick(this));
        InsideTeamCommand = new RelayCommand(AnswerInsideTeam, () => !_isInsideTeam);
        ToggleRoleCommand = new RelayCommand(ToggleRole);
        RemoveCommand = new RelayCommand(() => remove(this));
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

    /// <summary><c>input</c> or <c>output</c>, as the forge proposed it or the user flipped it.</summary>
    public string Role => _role;

    /// <summary>Whether the team only reads this folder.</summary>
    public bool IsInput => string.Equals(_role, ForgeFolder.InputRole, StringComparison.Ordinal);

    /// <summary>« read » or « written », in the user's language — the label of the chip that flips it.</summary>
    public string RoleLabel => _strings[IsInput ? StudioStringKeys.WizardFolderRoleInput : StudioStringKeys.WizardFolderRoleOutput];

    /// <summary>
    /// What the folder holds and how the team must use it (STUDIO-57): the forge's words at
    /// first, the user's once edited — it travels with the confirmation as the folder's
    /// <c>purpose</c>, and the assistant follows it when it designs the tasks.
    /// </summary>
    public string Purpose
    {
        get => _purpose;
        set
        {
            if (SetProperty(ref _purpose, value ?? ""))
                OnPropertiesChanged(nameof(HasPurpose));
        }
    }

    /// <summary>Whether anything says what the folder holds.</summary>
    public bool HasPurpose => _purpose.Length > 0;

    /// <summary>The real folder the user chose behind it; null while none is.</summary>
    public string? Directory => _directory;

    /// <summary>
    /// Whether the folder will live inside the team — the state of the « Inside the team » chip,
    /// checkable (STUDIO-57): a disabled button read as «unavailable», never as «chosen».
    /// Checking it answers the row; unchecking an input leaves it unanswered, for a folder of
    /// the disk to be chosen; an output has nowhere else to go by default, so it stays checked
    /// until a folder of the disk answers it.
    /// </summary>
    public bool IsInsideTeam
    {
        get => _isInsideTeam;
        set
        {
            if (value == _isInsideTeam)
                return;

            if (value)
            {
                AnswerInsideTeam();
                return;
            }

            if (!IsInput)
            {
                // The toggle snaps back: an output kept nowhere is kept inside the team anyway.
                OnPropertiesChanged(nameof(IsInsideTeam));
                return;
            }

            _directory = null;
            _isInsideTeam = false;
            RaiseAnswer();
        }
    }

    /// <summary>Whether a real folder of the user's answers it.</summary>
    public bool HasDirectory => _directory is not null;

    /// <summary>Whether nothing answers it yet — an input left for the engine's default.</summary>
    public bool IsUnanswered => !_isInsideTeam && _directory is null;

    /// <summary>What the row shows on its right: the folder, « inside the team: x », or « not chosen yet ».</summary>
    public string FolderLabel =>
        _directory
        ?? (_isInsideTeam
            ? _strings.Format(StudioStringKeys.WizardInsideTeamFolder, FolderName)
            : _strings[StudioStringKeys.WizardFolderNotChosen]);

    /// <summary>The folder's name inside the team: <c>input</c> for <c>/workspace</c>, its own otherwise.</summary>
    private string FolderName =>
        _virtualPath.Trim('/') is { Length: > 0 } ? TeamMountPaths.FolderFor(_virtualPath) : "";

    /// <summary>« Choose a folder… »: the disk picker, behind this row.</summary>
    public RelayCommand PickCommand { get; }

    /// <summary>« Inside the team »: the folder will be the team's own.</summary>
    public RelayCommand InsideTeamCommand { get; }

    /// <summary>The role chip: read becomes written, written becomes read (STUDIO-57).</summary>
    public RelayCommand ToggleRoleCommand { get; }

    /// <summary>The cross: the row leaves the list (STUDIO-57).</summary>
    public RelayCommand RemoveCommand { get; }

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

    /// <summary>
    /// Flips the role. A folder of the disk was bound with the former rights, so it no longer
    /// answers the row, which falls back to its default answer for the new role.
    /// </summary>
    private void ToggleRole()
    {
        _role = IsInput ? ForgeFolder.OutputRole : ForgeFolder.InputRole;
        var hadDirectory = _directory is not null;
        _directory = null;
        _isInsideTeam = !IsInput;
        OnPropertiesChanged(nameof(Role), nameof(IsInput), nameof(RoleLabel));
        RaiseAnswer();
        if (hadDirectory)
            _pathChanged(this, _virtualPath);
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

    /// <summary>
    /// Whether the forge waits for the folders to be confirmed — the panel shows, even over an
    /// empty list: the user may have taken every folder off (STUDIO-57), and confirms that.
    /// </summary>
    public bool IsFoldersStep => _model.FoldersPending;

    /// <summary>« Add a folder » (STUDIO-57): one more row, to read, named <c>/files</c> until renamed.</summary>
    public RelayCommand AddFolderRowCommand { get; private set; } = null!;

    /// <summary>
    /// Whether the list shown is the engine's defaults (STUDIO-57): the request named no folder,
    /// so the panel must not claim it read them there.
    /// </summary>
    public bool IsDefaultFolderProposal => _model.ProposedFoldersAreDefaults;

    /// <summary>The panel's subtitle: « read from your request », or the standard proposal's.</summary>
    public string FoldersStepSubtitle => _strings[IsDefaultFolderProposal
        ? StudioStringKeys.WizardFoldersStepSubDefaults
        : StudioStringKeys.WizardFoldersStepSub];

    /// <summary>« Confirm the folders ».</summary>
    public RelayCommand ConfirmFoldersCommand { get; private set; } = null!;

    /// <summary>
    /// What stops the confirmation, in words: a name that is no folder, or a name twice. Empty
    /// while the list can be confirmed — a list with no output, or no folder at all, can be
    /// (STUDIO-57): a team that sends mails from what it reads writes no file.
    /// </summary>
    public string FoldersProblem
    {
        get
        {
            var roots = new List<string>();
            foreach (var row in FolderRows)
            {
                if (!TryNormalizeRootName(row.VirtualPath, out var root))
                    return _strings.Format(StudioStringKeys.WizardFolderInvalidName, row.VirtualPath);
                if (roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                    return _strings.Format(StudioStringKeys.WizardFolderTwice, root);
                roots.Add(root);
            }

            return "";
        }
    }

    /// <summary>Whether <see cref="FoldersProblem"/> has something to say.</summary>
    public bool HasFoldersProblem => FoldersProblem.Length > 0;

    /// <summary>Whether the list can go back to the engine.</summary>
    public bool CanConfirmFolders => IsFoldersStep && IsEngineRunning && !HasFoldersProblem;

    private void InitializeFolderStep()
    {
        ConfirmFoldersCommand = new RelayCommand(ConfirmFolders, () => CanConfirmFolders);
        AddFolderRowCommand = new RelayCommand(AddFolderRow, () => IsFoldersStep);
        InitializeFillStep();
        InitializeRephrase();
    }

    /// <summary>A row of the panel, wired to the picker, the rename and the cross, and watched for its answer.</summary>
    private WizardFolderRow NewFolderRow(ForgeFolder folder)
    {
        var row = new WizardFolderRow(folder, _strings, PickRowFolder, OnFolderRowPathChanged, RemoveFolderRow);
        row.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(WizardFolderRow.Directory) && sender is WizardFolderRow changed)
                OnFolderRowAnswered(changed);
            if (e.PropertyName is nameof(WizardFolderRow.VirtualPath) or nameof(WizardFolderRow.Role))
                RefreshFolderStep();
        };
        return row;
    }

    /// <summary>« Add a folder »: a row to read, under a free name, for the user to rename and annotate.</summary>
    private void AddFolderRow()
    {
        if (!IsFoldersStep)
            return;

        FolderRows.Add(NewFolderRow(new ForgeFolder(UniqueRoot("/files"), ForgeFolder.InputRole)));
        RefreshFolderStep();
    }

    /// <summary>The cross on a row: it leaves the list, and the folder bound under its name with it.</summary>
    private void RemoveFolderRow(WizardFolderRow row)
    {
        if (!FolderRows.Remove(row))
            return;

        if (TryNormalizeRootName(row.VirtualPath, out var root)
            && !FolderRows.Any(other => string.Equals(other.VirtualPath, row.VirtualPath, StringComparison.Ordinal)))
        {
            RemoveBinding(root);
            ForgetBindingStatus(root);
        }

        RefreshMountSurfaces();
        RefreshFolderStep();
    }

    /// <summary>Rebuilds the rows when the engine proposes (again), and republishes the panel.</summary>
    private void SyncFolderStep()
    {
        if (_model.FoldersPending && !ReferenceEquals(_model.ProposedFolders, _proposalShown))
        {
            _proposalShown = _model.ProposedFolders;
            FolderRows.Clear();
            foreach (var proposed in _model.ProposedFolders)
                FolderRows.Add(NewFolderRow(InUsersWords(proposed)));

            // The folders dropped on the need answer their rows, or join the list (STUDIO-47).
            MergeDroppedFolders();
        }

        RefreshFolderStep();
        SyncFillStep();
    }

    /// <summary>
    /// A default folder's purpose in the user's language (STUDIO-57): the engine says it in the
    /// brief's, which is not always the one Studio speaks. A folder read from the request keeps
    /// the request's words.
    /// </summary>
    private ForgeFolder InUsersWords(ForgeFolder proposed) =>
        IsDefaultFolderProposal
            ? proposed with { Purpose = _strings[proposed.IsInput ? StudioStringKeys.WizardDefaultInputPurpose : StudioStringKeys.WizardDefaultOutputPurpose] }
            : proposed;

    private void RefreshFolderStep()
    {
        OnPropertiesChanged(
            nameof(IsFoldersStep), nameof(IsDefaultFolderProposal), nameof(FoldersStepSubtitle),
            nameof(FoldersProblem), nameof(HasFoldersProblem), nameof(CanConfirmFolders));
        ConfirmFoldersCommand.RaiseCanExecuteChanged();
        AddFolderRowCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Forgets the panel and the folders dropped on the need — another creation starts, or a session is resumed.</summary>
    private void ClearFolderStep()
    {
        ForgetDroppedFolders();
        _proposalShown = null;
        FolderRows.Clear();
        RefreshFolderStep();
        ClearFillStep();
        ClearRephrase();
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
            {
                row.AnswerWithDirectory(mount.PhysicalPath);
                _bindingStatusRoot = root;
            }
        }
    }

    /// <summary>
    /// A row's answer changed (STUDIO-57): when it no longer holds a folder of the disk — the
    /// chip went back to « inside the team », or the row was renamed — the status line that
    /// announced that folder goes with it.
    /// </summary>
    private void OnFolderRowAnswered(WizardFolderRow row)
    {
        if (row.Directory is null && TryNormalizeRootName(row.VirtualPath, out var root))
            ForgetBindingStatus(root);
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
            var purpose = row.Purpose.Trim();
            folders.Add(new ForgeFolder(root, row.Role, purpose.Length > 0 ? purpose : null, row.Directory));
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
