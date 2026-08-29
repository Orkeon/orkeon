using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>
/// One row of the allowed-folder chooser: a folder declared once in
/// « Réglages › Dossiers autorisés », offered to a team as-is.
/// </summary>
public sealed class AllowedFolderRowViewModel : ObservableObject
{
    private readonly AllowedFolderChooserViewModel _owner;
    private bool _isChecked;

    internal AllowedFolderRowViewModel(
        string mountString,
        MountDefinition? mount,
        string? unavailableNote,
        AllowedFolderChooserViewModel owner,
        IStudioStrings strings)
    {
        _owner = owner;
        MountString = mountString;
        Mount = mount;
        UnavailableNote = unavailableNote;
        // ADR-008: a mount string the parser refuses has no virtual spelling — say so rather
        // than dumping the raw string, which carries the folder on this machine.
        VirtualPath = mount?.VirtualPath ?? MountLabels.Unreadable(strings);
        PhysicalPath = mount?.PhysicalPath ?? "";
        IsReadOnly = mount is null or { Rights: MountRights.ReadOnly };
        RightsLabel = strings[mount is { Rights: MountRights.ReadWrite }
            ? StudioStringKeys.RightsReadWrite
            : StudioStringKeys.RightsReadOnly];
    }

    /// <summary>The declared entry, verbatim — what the team will record.</summary>
    public string MountString { get; }

    /// <summary>The parsed entry, or <see langword="null"/> when the settings hold something unreadable.</summary>
    internal MountDefinition? Mount { get; }

    /// <summary>The virtual spelling the agents address.</summary>
    public string VirtualPath { get; }

    /// <summary>The folder on this machine.</summary>
    public string PhysicalPath { get; }

    /// <summary>The rights pill's text — the rights of the settings entry, never re-chosen here.</summary>
    public string RightsLabel { get; }

    /// <summary>True for a read-only entry — the pill's quiet tone.</summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Why this row cannot be picked: already on the team, its virtual root already taken by
    /// another folder, or unreadable. <see langword="null"/> when the row is selectable.
    /// </summary>
    public string? UnavailableNote { get; }

    /// <summary>Whether the row carries a note.</summary>
    public bool HasNote => UnavailableNote is { Length: > 0 };

    /// <summary>Whether the row can be checked at all.</summary>
    public bool IsSelectable => UnavailableNote is null;

    /// <summary>Whether the team will receive this folder.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            // A row the team cannot take must not become checked by a stray click on the
            // template: the guard lives here rather than only in the view.
            if (value && !IsSelectable)
                return;

            if (SetProperty(ref _isChecked, value))
                _owner.RowToggled();
        }
    }

    internal void CheckSilently() => _isChecked = IsSelectable;
}

/// <summary>
/// « Ajouter un dossier autorisé » — the modal a team uses to pick among the folders already
/// declared in « Réglages › Dossiers autorisés » (<c>Orkeon:FileSystem:Mounts</c>).
/// <para>
/// A team does not declare a folder, it associates one: the entry is carried over verbatim,
/// rights included. Declaring is what the settings screen is for, and
/// <see cref="DeclareNewCommand"/> is the door to it — one door, leading to the one screen
/// that declares, rather than a second picker that would let a folder be declared from two
/// places and drift between them.
/// </para>
/// </summary>
public sealed class AllowedFolderChooserViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<string>> _declaredMounts;
    private readonly IStudioStrings _strings;

    private Action<MountDefinition>? _onAdd;
    private IReadOnlyList<string> _alreadyOnTarget = [];
    private bool _isOpen;

    /// <summary>Builds the chooser over the settings list it renders.</summary>
    /// <param name="declaredMounts">Reads the folders declared in the settings, on each open.</param>
    /// <param name="strings">Localization port; defaults to the English strings (STUDIO-11).</param>
    public AllowedFolderChooserViewModel(
        Func<IReadOnlyList<string>> declaredMounts,
        IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(declaredMounts);

        _declaredMounts = declaredMounts;
        _strings = strings ?? EnglishStudioStrings.Instance;

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Close);
        DeclareNewCommand = new RelayCommand(() =>
        {
            Close();
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Whether the modal is showing.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>The declared folders, as checkable rows.</summary>
    public ObservableCollection<AllowedFolderRowViewModel> Rows { get; } = [];

    /// <summary>Whether the settings declare anything at all — false drives the empty state.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>The footer's count of what « Ajouter à l'équipe » would add.</summary>
    public string Summary => string.Format(
        CultureInfo.CurrentCulture,
        _strings[StudioStringKeys.AllowedFoldersSummary],
        Rows.Count(r => r.IsChecked));

    /// <summary>Whether at least one selectable row is checked.</summary>
    public bool CanConfirm => Rows.Any(r => r is { IsChecked: true, IsSelectable: true });

    /// <summary>« Ajouter à l'équipe ».</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>Closes without adding anything.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    /// « Déclarer un nouveau dossier… » — closes and sends the shell to
    /// « Réglages › Dossiers autorisés », the one screen that declares.
    /// </summary>
    public RelayCommand DeclareNewCommand { get; }

    /// <summary>Raised by <see cref="DeclareNewCommand"/> — the shell opens the settings on their folders tab.</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>
    /// Shows the modal. <paramref name="alreadyOnTarget"/> are the mount strings the team
    /// already carries — they drive the "déjà ajouté" and virtual-root notes;
    /// <paramref name="onAdd"/> receives one call per checked folder.
    /// </summary>
    public void Open(IReadOnlyList<string> alreadyOnTarget, Action<MountDefinition> onAdd)
    {
        ArgumentNullException.ThrowIfNull(alreadyOnTarget);
        ArgumentNullException.ThrowIfNull(onAdd);

        _alreadyOnTarget = alreadyOnTarget;
        _onAdd = onAdd;
        // A fresh open starts on a clean selection: Rebuild carries the ticks over, which is
        // what a declaration made mid-selection needs and what a reopen must not inherit.
        Rows.Clear();
        Rebuild();
        IsOpen = true;
    }

    internal void RowToggled()
    {
        OnPropertyChanged(nameof(Summary));
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private void Confirm()
    {
        var onAdd = _onAdd;
        // Snapshot before closing: Close() drops the callback, and the rows are rebuilt on the
        // next open anyway.
        var picked = Rows
            .Where(r => r is { IsChecked: true, IsSelectable: true, Mount: not null })
            .Select(r => r.Mount!)
            .ToList();

        Close();

        foreach (var mount in picked)
            onAdd?.Invoke(mount);
    }

    private void Close()
    {
        _onAdd = null;
        IsOpen = false;
    }

    /// <summary>Rebuilds the rows from the settings, keeping what is already ticked.</summary>
    private void Rebuild()
    {
        var checkedBefore = Rows
            .Where(r => r.IsChecked)
            .Select(r => r.MountString)
            .ToHashSet(StringComparer.Ordinal);

        // The virtual roots the team already spends, and the folder behind each one: a second
        // mount on the same root is not a duplicate the runtime merges, it is one the runtime
        // silently drops.
        var takenRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in _alreadyOnTarget)
        {
            if (MountDefinition.TryParse(entry, out var mount, out _) && mount is not null)
                takenRoots[mount.VirtualPath] = mount.PhysicalPath.TrimEnd('/', '\\');
        }

        Rows.Clear();
        foreach (var entry in _declaredMounts())
        {
            var parsed = MountDefinition.TryParse(entry, out var mount, out _) ? mount : null;
            var row = new AllowedFolderRowViewModel(entry, parsed, UnavailableNote(parsed, takenRoots), this, _strings);

            if (checkedBefore.Contains(entry))
                row.CheckSilently();

            Rows.Add(row);
        }

        OnPropertiesChanged(nameof(HasRows), nameof(Summary));
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private string? UnavailableNote(MountDefinition? mount, Dictionary<string, string> takenRoots)
    {
        if (mount is null)
            return MountLabels.Unreadable(_strings);

        if (!takenRoots.TryGetValue(mount.VirtualPath, out var takenBy))
            return null;

        // Same folder on the same root: the team already has it. A different folder on the
        // same root is a collision, and the row says which one it is by its virtual name.
        return string.Equals(takenBy, mount.PhysicalPath.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
            ? _strings[StudioStringKeys.AllowedFoldersAlreadyAdded]
            : string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.AllowedFoldersConflict], mount.VirtualPath);
    }
}
