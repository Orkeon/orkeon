using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Common;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>
/// The mount list editor of spec §4.5. The same component serves both tabs — the Config tab writes
/// its result into <c>Orkeon:FileSystem:Mounts</c>, the Launch tab turns it into <c>--mount</c>
/// arguments — so only <see cref="RequireAtLeastOne"/> differs between the two uses.
/// </summary>
public sealed class MountsEditorViewModel : ObservableObject
{
    private readonly IDirectoryProbe _directories;
    private readonly IPathPicker _picker;
    private readonly MountValidator _validator;
    private readonly IStudioStrings _strings;
    private readonly IClipboardService _clipboard;
    private MountEditorViewModel? _selectedMount;
    private bool _suspendValidation;

    /// <summary>
    /// Reads the adopted teams, so each row can say which teams name it by id (VFS-90) and a
    /// removal can ask first. Null in the launcher, where the rows are the run's own.
    /// </summary>
    public Func<IReadOnlyList<Orkeon.Studio.Core.Teams.TeamSummary>>? LoadTeams { get; set; }

    /// <summary>
    /// Asked before removing an entry a team depends on, with the entry's folder and the teams'
    /// names; false keeps the entry. Null removes without asking (the tests, the launcher).
    /// </summary>
    public Func<string, IReadOnlyList<string>, bool>? ConfirmRemoval { get; set; }

    /// <summary>Creates an editor over the given directory probe and browse dialogs.</summary>
    /// <param name="directories">Probes and creates the physical folders a mount points at.</param>
    /// <param name="picker">The folder browser used to fill in a physical path.</param>
    /// <param name="requireAtLeastOne">
    /// <see langword="true"/> in the appsettings editor, where an empty list makes the runtime refuse
    /// to boot; <see langword="false"/> in the launcher, where adding no mount is normal.
    /// </param>
    /// <param name="strings">Localization port; defaults to the English strings (STUDIO-11).</param>
    /// <param name="clipboard">The clipboard behind "copy the id" (VFS-90); in-memory when absent.</param>
    /// <param name="assignIds">
    /// <see langword="true"/> in the appsettings editor, where every row is a settings entry
    /// and gets an id (VFS-90); <see langword="false"/> in the launcher, whose per-run
    /// <c>--mount</c> rows are not settings entries and carry none.
    /// </param>
    public MountsEditorViewModel(
        IDirectoryProbe? directories = null,
        IPathPicker? picker = null,
        bool requireAtLeastOne = true,
        IStudioStrings? strings = null,
        IClipboardService? clipboard = null,
        bool assignIds = true)
    {
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _picker = picker ?? NullPathPicker.Instance;
        _validator = new MountValidator(_directories);
        _strings = strings ?? EnglishStudioStrings.Instance;
        _clipboard = clipboard ?? new InMemoryClipboardService();
        AssignIds = assignIds;
        _strings.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Summary));
            foreach (var mount in Mounts)
                mount.RefreshCulture();
        };
        RequireAtLeastOne = requireAtLeastOne;

        Mounts.CollectionChanged += OnMountsChanged;

        AddMountCommand = new RelayCommand(() => AddMount());
        AllowFolderCommand = new RelayCommand(AllowFolder);
        DropMountCommand = new RelayCommand(
            parameter => { if (parameter is MountEditorViewModel mount) Remove(mount); },
            parameter => parameter is MountEditorViewModel);
        ToggleRightsCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is MountEditorViewModel { IsParsed: true } mount)
                    mount.Rights = mount.Rights == MountRights.ReadOnly ? MountRights.ReadWrite : MountRights.ReadOnly;
            },
            parameter => parameter is MountEditorViewModel { IsParsed: true });
        RemoveMountCommand = new RelayCommand(RemoveSelected, () => SelectedMount is not null);
        BrowsePhysicalPathCommand = new RelayCommand(BrowsePhysicalPath, () => SelectedMount is { IsParsed: true });
        CreatePhysicalFolderCommand = new RelayCommand(
            CreatePhysicalFolder,
            () => SelectedMount is { IsParsed: true } mount
                && mount.PhysicalPath.Length > 0
                && !_directories.Exists(mount.PhysicalPath));
        AddOverrideCommand = new RelayCommand(
            () => SelectedMount?.AddOverride(),
            () => SelectedMount is { IsParsed: true });
        RemoveOverrideCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is SubPathOverrideViewModel item)
                    SelectedMount?.RemoveOverride(item);
            },
            parameter => parameter is SubPathOverrideViewModel);

        // No validation at construction (audit 08/17): an empty form must not open in an
        // error state. The first Validate() runs on the first change, load, or save.
    }

    /// <summary>Whether an empty list is itself an error.</summary>
    public bool RequireAtLeastOne { get; }

    /// <summary>Whether the rows are settings entries and carry an id (VFS-90).</summary>
    public bool AssignIds { get; }

    /// <summary>The edited mounts, in the order they will be written.</summary>
    public ObservableCollection<MountEditorViewModel> Mounts { get; } = [];

    /// <summary>The findings of the last <see cref="Validate"/>.</summary>
    public ObservableCollection<ValidationMessageViewModel> ValidationMessages { get; } = [];

    /// <summary>Adds an empty mount row and selects it.</summary>
    public RelayCommand AddMountCommand { get; }

    /// <summary>Removes <see cref="SelectedMount"/>.</summary>
    public RelayCommand RemoveMountCommand { get; }

    /// <summary>Opens the folder browser for the selected mount's physical path.</summary>
    public RelayCommand BrowsePhysicalPathCommand { get; }

    /// <summary>Creates the selected mount's physical folder when it does not exist yet (spec §4.5).</summary>
    public RelayCommand CreatePhysicalFolderCommand { get; }

    /// <summary>Appends a sub-path rights override to the selected mount.</summary>
    public RelayCommand AddOverrideCommand { get; }

    /// <summary>Removes the sub-path override passed as the command parameter.</summary>
    public RelayCommand RemoveOverrideCommand { get; }

    /// <summary>The row shown in the detail form.</summary>
    public MountEditorViewModel? SelectedMount
    {
        get => _selectedMount;
        set
        {
            if (!SetProperty(ref _selectedMount, value))
                return;

            RemoveMountCommand.RaiseCanExecuteChanged();
            BrowsePhysicalPathCommand.RaiseCanExecuteChanged();
            CreatePhysicalFolderCommand.RaiseCanExecuteChanged();
            AddOverrideCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether the current list would be rejected at save or launch time.</summary>
    public bool HasErrors => ValidationMessages.Any(m => m.IsError);

    /// <summary>Raised after every edit, so the owning tab can refresh its own state.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces the list with the entries of an appsettings mount array.</summary>
    public void Load(IReadOnlyList<string> rawEntries)
    {
        ArgumentNullException.ThrowIfNull(rawEntries);

        _suspendValidation = true;
        try
        {
            Mounts.Clear();
            // A parsed entry without an id gets one on the way in (VFS-90): it shows from the
            // first paint, marked as new, and lands in the file at the next save.
            foreach (var entry in rawEntries)
                Mounts.Add(MountEditorViewModel.FromRaw(entry, _strings, _clipboard, AssignIds));
        }
        finally
        {
            _suspendValidation = false;
        }

        SelectedMount = Mounts.Count > 0 ? Mounts[0] : null;
        RefreshTeamReferences();
        Validate();
    }

    /// <summary>
    /// Re-reads which teams name each row by id. The shell calls it when the teams on disk
    /// change; <see cref="Load"/> calls it itself.
    /// </summary>
    public void RefreshTeamReferences()
    {
        if (LoadTeams is null)
            return;

        var byId = Orkeon.Studio.Core.Teams.TeamMountReferences.ByMountId(LoadTeams());
        foreach (var mount in Mounts)
        {
            mount.UsedByTeams = mount.Id is { } id && byId.TryGetValue(id, out var teams)
                ? teams.Select(t => Orkeon.Studio.Core.Teams.TeamCatalog.NormalizeName(t.Name)).ToList()
                : [];
        }
    }

    /// <summary>
    /// The declared entry a team may bind (VFS-90, D-01): an existing row that declares the
    /// same thing — folder, root, rights — is reused (and given an id if it had none);
    /// otherwise the mount is added as a new row under an id of its own, even when another
    /// row already claims its root. Mirrors <c>MountsSection.EnsureDeclared</c> on the
    /// document, for the editor's live list the verdicts read.
    /// </summary>
    /// <returns>The entry as the editor now holds it, and whether it already existed.</returns>
    public (MountDefinition Declared, bool Reused) EnsureDeclared(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        foreach (var row in Mounts)
        {
            if (!row.IsParsed)
                continue;

            var existing = row.ToDefinition();
            if (!existing.SameDeclaration(mount))
                continue;

            row.EnsureId();
            return (row.ToDefinition(), true);
        }

        var added = mount.Id is not null && Mounts.All(r => !r.IsParsed || !mount.Id.Equals(r.Id)) ? mount : mount.WithFreshId();
        AddPickedMount(added);
        return (added, false);
    }

    /// <summary>The serialized entries, unparsed rows included, in list order.</summary>
    public IReadOnlyList<string> ToRawEntries() => [.. Mounts.Select(m => m.MountString)];

    /// <summary>"Autoriser un dossier…" — the novice add path (audit 08/17).</summary>
    public RelayCommand AllowFolderCommand { get; }

    /// <summary>Removes the mount passed as parameter — the novice card's discrete action.</summary>
    public RelayCommand DropMountCommand { get; }

    /// <summary>
    /// Flips the rights of the mount passed as parameter — read-only becomes read-and-write,
    /// anything else becomes read-only. The novice card's badge (STUDIO-19): the OS folder
    /// dialog asks no rights question, so the pick lands read-only and the card is where it
    /// widens — or narrows again — after the fact.
    /// </summary>
    public RelayCommand ToggleRightsCommand { get; }

    /// <summary>
    /// The mount strings this editor currently holds — what the verdicts read live, and what
    /// the wizard's disk pick counts as taken.
    /// </summary>
    public IReadOnlyList<string> CurrentMountStrings => [.. Mounts.Select(m => m.MountString)];

    /// <summary>Adds a mount the shell declares on the wizard's behalf as a new row, id included when it carries one.</summary>
    public void AddPickedMount(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        // The collection hook validates and raises Changed on its own.
        Mounts.Add(MountEditorViewModel.FromDefinition(mount, _strings, _clipboard, AssignIds));
    }

    /// <summary>
    /// « Autoriser un dossier… », the novice add path: the OS folder dialog, then the folder
    /// mounted read-only under a virtual name derived from its own name (STUDIO-19 — no in-app
    /// picker in between). Read-only is the safe default; the card's badge widens it.
    /// </summary>
    private void AllowFolder()
    {
        var picked = _picker.PickFolder(_strings[StudioStringKeys.DialogSelectMountFolder]);
        if (picked is not { Length: > 0 })
            return;

        var mount = new MountEditorViewModel(_strings, _clipboard, AssignIds)
        {
            PhysicalPath = picked,
            VirtualPath = MountDefinition.SuggestVirtualPath(picked, Mounts.Select(m => m.VirtualPath)),
            Rights = MountRights.ReadOnly,
        };

        Mounts.Add(mount);
        SelectedMount = mount;
        Validate();
    }

    /// <summary>Appends an empty mount row, pre-filled with the first suggested virtual path.</summary>
    public MountEditorViewModel AddMount()
    {
        var mount = new MountEditorViewModel(_strings, _clipboard, AssignIds)
        {
            VirtualPath = MountDefinition.SuggestedVirtualPaths[0],
        };

        Mounts.Add(mount);
        SelectedMount = mount;
        return mount;
    }

    /// <summary>
    /// Removes a row and selects a neighbour. A row a team names by id is removed only once
    /// <see cref="ConfirmRemoval"/> agreed (VFS-90): those teams stop starting the moment the
    /// entry is gone, and a novice clicking ✕ on a folder has no other way of knowing.
    /// </summary>
    public void Remove(MountEditorViewModel mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        var index = Mounts.IndexOf(mount);
        if (index < 0)
            return;

        if (mount.IsReferenced && ConfirmRemoval is { } confirm && !confirm(mount.PhysicalPath, mount.UsedByTeams))
            return;

        Mounts.RemoveAt(index);
        SelectedMount = Mounts.Count == 0 ? null : Mounts[Math.Min(index, Mounts.Count - 1)];
    }

    /// <summary>
    /// Re-runs <see cref="MountValidator"/> over the current list and republishes
    /// <see cref="ValidationMessages"/>.
    /// </summary>
    public IReadOnlyList<ValidationMessage> Validate()
    {
        var messages = _validator.Validate(ToRawEntries(), RequireAtLeastOne);

        ValidationMessages.Clear();
        foreach (var message in messages)
            ValidationMessages.Add(new ValidationMessageViewModel(message, _strings));

        OnPropertiesChanged(nameof(HasErrors), nameof(Summary));
        return messages;
    }

    /// <summary>A one-line status for the header of the mount panel.</summary>
    public string Summary
    {
        get
        {
            var errors = ValidationMessages.Count(m => m.IsError);
            return errors == 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    _strings[StudioStringKeys.MountsSummaryOk], Mounts.Count)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    _strings[StudioStringKeys.MountsSummaryErrors], Mounts.Count, errors);
        }
    }

    private void BrowsePhysicalPath()
    {
        if (SelectedMount is not { IsParsed: true } mount)
            return;

        var picked = _picker.PickFolder(_strings[StudioStringKeys.DialogSelectMountFolder], mount.PhysicalPath);
        if (picked is { Length: > 0 })
            mount.PhysicalPath = picked;
    }

    private void CreatePhysicalFolder()
    {
        if (SelectedMount is not { IsParsed: true } mount || mount.PhysicalPath.Length == 0)
            return;

        _directories.Create(mount.PhysicalPath);
        Validate();
        CreatePhysicalFolderCommand.RaiseCanExecuteChanged();
    }

    private void RemoveSelected()
    {
        if (SelectedMount is { } mount)
            Remove(mount);
    }

    private void OnMountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var mount in e.NewItems?.OfType<MountEditorViewModel>() ?? [])
            mount.Edited += OnMountEdited;

        foreach (var mount in e.OldItems?.OfType<MountEditorViewModel>() ?? [])
            mount.Edited -= OnMountEdited;

        OnMountEdited(sender, EventArgs.Empty);
    }

    private void OnMountEdited(object? sender, EventArgs e)
    {
        if (_suspendValidation)
            return;

        Validate();
        CreatePhysicalFolderCommand.RaiseCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
