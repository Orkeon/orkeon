using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>One checkbox row of the team-mounts modal.</summary>
public sealed class TeamMountRowViewModel : ObservableObject
{
    private readonly TeamMountsDialogViewModel _owner;
    private bool _isChecked;

    internal TeamMountRowViewModel(
        string mountString,
        MountDefinition? mount,
        bool isUndeclared,
        TeamMountsDialogViewModel owner,
        IStudioStrings strings,
        string shortId = "",
        bool isUnknownId = false)
    {
        _owner = owner;
        MountString = mountString;
        IsUndeclared = isUndeclared;
        // An entry the parser refuses has no virtual spelling — say so, rather than falling
        // back to the raw string, which carries the physical folder (ADR-008).
        VirtualPath = mount?.VirtualPath ?? MountLabels.Unreadable(strings);
        PhysicalPath = mount?.PhysicalPath ?? "";
        IsReadOnly = mount is null or { Rights: MountRights.ReadOnly };
        RightsLabel = strings[mount is { Rights: MountRights.ReadWrite }
            ? StudioStringKeys.RightsReadWrite
            : StudioStringKeys.RightsReadOnly];
        RightsBadge = MountRightsTokens.GetBadge(mount?.Rights ?? MountRights.ReadOnly, strings);
        _isChecked = true;
        ShortId = shortId;
        IsUnknownId = isUnknownId;
    }

    /// <summary>The last six characters of the settings entry the row names (VFS-90); empty otherwise.</summary>
    public string ShortId { get; }

    /// <summary>Whether the row shows an id.</summary>
    public bool HasId => ShortId.Length > 0;

    /// <summary>Whether the row names a declaration this machine does not have (D-06).</summary>
    public bool IsUnknownId { get; }

    /// <summary>The row's mount string, verbatim — what a save writes.</summary>
    public string MountString { get; }

    /// <summary>The virtual spelling the agents see.</summary>
    public string VirtualPath { get; }

    /// <summary>The physical folder behind it.</summary>
    public string PhysicalPath { get; }

    /// <summary>The rights pill's text.</summary>
    public string RightsLabel { get; }

    /// <summary>The one-word rights pill (STUDIO-16); <see cref="RightsLabel"/> is its tooltip.</summary>
    public string RightsBadge { get; }

    /// <summary>True for a read-only mount — the pill's quiet tone.</summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// True when this folder is not in Settings › Allowed folders — the row reads red.
    /// It is not an error: a team's <c>/output</c> lives inside the team itself and is never
    /// declared. It is the one thing the row cannot say by naming a virtual path.
    /// </summary>
    public bool IsUndeclared { get; }

    /// <summary>Whether the team keeps this folder; unchecked rows are dropped on save.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                _owner.RowToggled();
        }
    }
}

/// <summary>
/// The folders-of-team-X modal (remediation v2, F-03): the team's mount strings as
/// checkbox rows, an add button routed through the shared folder picker, and a save that
/// writes the sidecar — the card chips, this list and the launch arguments are one list.
/// </summary>
public sealed class TeamMountsDialogViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private readonly Func<IReadOnlyList<string>> _declaredMounts;
    private readonly Action<string, IReadOnlyList<string>> _saveMounts;
    private string _teamDirectory = "";
    private string _teamName = "";
    private Action? _onSaved;
    private bool _isOpen;

    /// <summary>Builds the modal; <paramref name="saveMounts"/> defaults to the real catalog.</summary>
    public TeamMountsDialogViewModel(
        IStudioStrings? strings = null,
        Action<string, IReadOnlyList<string>>? saveMounts = null,
        Func<IReadOnlyList<string>>? declaredMounts = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _declaredMounts = declaredMounts ?? (() => []);
        _saveMounts = saveMounts ?? TeamCatalog.SaveMounts;
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Close);
        AddCommand = new RelayCommand(() => AddRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Raised by the allow-another-folder action — the shell opens the shared folder
    /// picker over this modal and feeds the choice back through <see cref="AddMount"/>.
    /// </summary>
    public event EventHandler? AddRequested;

    /// <summary>The allow-another-folder action.</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>Whether the modal is showing.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>Shows the modal for one team; <paramref name="onSaved"/> runs after a save.</summary>
    public void Open(string teamDirectory, string teamName, IReadOnlyList<string> mounts, Action? onSaved = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(mounts);

        _teamDirectory = teamDirectory;
        _teamName = teamName;
        _onSaved = onSaved;
        Rows.Clear();
        foreach (var mountString in mounts)
            Rows.Add(Row(mountString));

        IsOpen = true;
        OnPropertiesChanged(nameof(Title), nameof(Summary));
    }

    /// <summary>Adds the picker's choice as a new, checked row.</summary>
    public void AddMount(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        Rows.Add(Row(mount.ToMountString()));
        OnPropertiesChanged(nameof(Summary));
    }

    /// <summary>The mount strings a save would keep — the checked rows, in order.</summary>
    public IReadOnlyList<string> CheckedMounts => [.. Rows.Where(r => r.IsChecked).Select(r => r.MountString)];

    /// <summary>The checkbox rows.</summary>
    public ObservableCollection<TeamMountRowViewModel> Rows { get; } = [];

    /// <summary>The modal's title: the folders of team X.</summary>
    public string Title => string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamMountsTitle], _teamName);

    /// <summary>The footer's plain-words summary of what a save would keep.</summary>
    public string Summary
    {
        get
        {
            var kept = CheckedMounts;
            if (kept.Count == 0)
                return _strings[StudioStringKeys.TeamMountsNone];

            var virtualPaths = Rows.Where(r => r.IsChecked).Select(r => r.VirtualPath);
            return string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamMountsSummary],
                kept.Count, string.Join(", ", virtualPaths));
        }
    }

    /// <summary>Writes the checked rows into the sidecar and closes.</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>Closes without writing.</summary>
    public RelayCommand CancelCommand { get; }

    internal void RowToggled() => OnPropertiesChanged(nameof(Summary));

    private TeamMountRowViewModel Row(string mountString)
    {
        var declared = _declaredMounts();
        var parsed = MountDefinition.TryParse(mountString, out var mount, out _) ? mount : null;
        var insideTeam = Orkeon.Studio.Core.FileSystem.DeclaredMounts.IsInsideTeam(mountString, _teamDirectory);
        // A row naming a settings declaration by id shows THAT declaration (VFS-90, D-01).
        var entry = parsed is null || insideTeam ? null : Orkeon.Studio.Core.FileSystem.DeclaredMounts.FindDeclared(mountString, declared);

        return new TeamMountRowViewModel(
            mountString,
            entry ?? parsed,
            // The team's own folders are vouched for by being the team's, exactly as the launcher
            // has it. Asking only «is it declared» painted a team's own /output red here while
            // the Run screen let it through.
            !Orkeon.Studio.Core.FileSystem.DeclaredMounts.IsVouchedFor(mountString, declared, _teamDirectory),
            this,
            _strings,
            shortId: insideTeam ? "" : (entry?.ShortId ?? parsed?.ShortId ?? ""),
            isUnknownId: !insideTeam && Orkeon.Studio.Core.FileSystem.DeclaredMounts.HasUnknownId(mountString, declared));
    }

    /// <summary>
    /// The checked rows as the sidecar will record them: a copy of a settings entry written
    /// before ids — same folder, root and rights — is upgraded to the entry itself, id
    /// included, so the team names its declaration from now on (VFS-90 migration).
    /// </summary>
    private List<string> MountsToSave()
    {
        var declared = _declaredMounts();
        var saved = new List<string>();
        foreach (var raw in CheckedMounts)
        {
            if (!Orkeon.Studio.Core.FileSystem.DeclaredMounts.IsInsideTeam(raw, _teamDirectory)
                && Orkeon.Studio.Core.FileSystem.DeclaredMounts.FindDeclared(raw, declared) is { Id: not null } entry)
            {
                saved.Add(entry.ToMountString());
                continue;
            }

            saved.Add(raw);
        }

        return saved;
    }

    private void Save()
    {
        _saveMounts(_teamDirectory, MountsToSave());
        var onSaved = _onSaved;
        Close();
        onSaved?.Invoke();
    }

    private void Close()
    {
        _onSaved = null;
        IsOpen = false;
    }
}
