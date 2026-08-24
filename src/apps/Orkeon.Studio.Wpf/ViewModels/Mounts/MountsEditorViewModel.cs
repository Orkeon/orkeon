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
    private MountEditorViewModel? _selectedMount;
    private bool _suspendValidation;

    /// <summary>Creates an editor over the given directory probe and browse dialogs.</summary>
    /// <param name="directories">Probes and creates the physical folders a mount points at.</param>
    /// <param name="picker">The folder browser used to fill in a physical path.</param>
    /// <param name="requireAtLeastOne">
    /// <see langword="true"/> in the appsettings editor, where an empty list makes the runtime refuse
    /// to boot; <see langword="false"/> in the launcher, where adding no mount is normal.
    /// </param>
    /// <param name="strings">Localization port; defaults to the English strings (STUDIO-11).</param>
    public MountsEditorViewModel(
        IDirectoryProbe? directories = null,
        IPathPicker? picker = null,
        bool requireAtLeastOne = true,
        IStudioStrings? strings = null)
    {
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _picker = picker ?? NullPathPicker.Instance;
        _validator = new MountValidator(_directories);
        _strings = strings ?? EnglishStudioStrings.Instance;
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
            foreach (var entry in rawEntries)
                Mounts.Add(MountEditorViewModel.FromRaw(entry, _strings));
        }
        finally
        {
            _suspendValidation = false;
        }

        SelectedMount = Mounts.Count > 0 ? Mounts[0] : null;
        Validate();
    }

    /// <summary>The serialized entries, unparsed rows included, in list order.</summary>
    public IReadOnlyList<string> ToRawEntries() => [.. Mounts.Select(m => m.MountString)];

    /// <summary>"Autoriser un dossier…" — the novice add path (audit 08/17).</summary>
    public RelayCommand AllowFolderCommand { get; }

    /// <summary>Removes the mount passed as parameter — the novice card's discrete action.</summary>
    public RelayCommand DropMountCommand { get; }

    /// <summary>
    /// The novice flow: pick a real folder, mount it read-only under a virtual name derived
    /// from the folder itself (first free suggested path as fallback). Read-only is the safe
    /// default — the expert form is where rights widen.
    /// </summary>
    /// <summary>
    /// Raised by « Autoriser un dossier… » when the shell wired the shared picker modal
    /// (remediation v2, F-03): the rights choice then belongs to the modal. Without a
    /// subscriber, the legacy OS browser opens and the mount lands read-only.
    /// </summary>
    public event EventHandler? FolderPickRequested;

    /// <summary>The mount strings this editor currently holds — what the picker's notes show.</summary>
    public IReadOnlyList<string> CurrentMountStrings => [.. Mounts.Select(m => m.MountString)];

    /// <summary>Adds the picker modal's choice as a new mount row.</summary>
    public void AddPickedMount(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        // The collection hook validates and raises Changed on its own.
        Mounts.Add(new MountEditorViewModel(_strings)
        {
            PhysicalPath = mount.PhysicalPath,
            VirtualPath = mount.VirtualPath,
            Rights = mount.Rights,
        });
    }

    private void AllowFolder()
    {
        if (FolderPickRequested is { } requested)
        {
            requested.Invoke(this, EventArgs.Empty);
            return;
        }

        var picked = _picker.PickFolder(_strings[StudioStringKeys.DialogSelectMountFolder]);
        if (picked is not { Length: > 0 })
            return;

        var name = System.IO.Path.GetFileName(picked.TrimEnd('/', '\\'));
        var candidate = "/" + (name is { Length: > 0 }
#pragma warning disable CA1308 // virtual paths are lowercase by convention, not a normalization round-trip
            ? name.ToLowerInvariant()
#pragma warning restore CA1308
            : "docs");
        if (!MountDefinition.IsValidVirtualPath(candidate) || Mounts.Any(m => m.VirtualPath == candidate))
        {
            candidate = MountDefinition.SuggestedVirtualPaths
                .FirstOrDefault(s => Mounts.All(m => m.VirtualPath != s)) ?? "/workspace";
        }

        var mount = new MountEditorViewModel(_strings)
        {
            PhysicalPath = picked,
            VirtualPath = candidate,
            Rights = MountRights.ReadOnly,
        };

        Mounts.Add(mount);
        SelectedMount = mount;
        Validate();
    }

    /// <summary>Appends an empty mount row, pre-filled with the first suggested virtual path.</summary>
    public MountEditorViewModel AddMount()
    {
        var mount = new MountEditorViewModel(_strings)
        {
            VirtualPath = MountDefinition.SuggestedVirtualPaths.Count > 0
                ? MountDefinition.SuggestedVirtualPaths[0]
                : "/workspace",
        };

        Mounts.Add(mount);
        SelectedMount = mount;
        return mount;
    }

    /// <summary>Removes a row and selects a neighbour.</summary>
    public void Remove(MountEditorViewModel mount)
    {
        ArgumentNullException.ThrowIfNull(mount);

        var index = Mounts.IndexOf(mount);
        if (index < 0)
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
