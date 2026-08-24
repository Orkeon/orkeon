using System.Collections.ObjectModel;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>One row of the folder picker's tree: an ancestor of the current path, or one of its children.</summary>
public sealed class FolderNodeViewModel
{
    internal FolderNodeViewModel(string path, string name, int depth, bool isSelected, string? note, Action<FolderNodeViewModel> pick)
    {
        Path = path;
        Name = name;
        Depth = depth;
        IsSelected = isSelected;
        Note = note;
        PickCommand = new RelayCommand(() => pick(this));
    }

    /// <summary>Absolute path this row stands for.</summary>
    public string Path { get; }

    /// <summary>Display name — the last path segment, or the root spelling itself.</summary>
    public string Name { get; }

    /// <summary>Nesting level; the view indents by 15 px per level, like the mock.</summary>
    public int Depth { get; }

    /// <summary>Left indent in pixels.</summary>
    public double Indent => Depth * 15d;

    /// <summary>True for the row the picker currently points at.</summary>
    public bool IsSelected { get; }

    /// <summary>The faint right-hand note ("déjà autorisé"), when one applies.</summary>
    public string? Note { get; }

    /// <summary>Whether a note exists.</summary>
    public bool HasNote => Note is { Length: > 0 };

    /// <summary>Makes this row the current path.</summary>
    public RelayCommand PickCommand { get; }
}

/// <summary>
/// The "Autoriser un dossier" modal (remediation v2, F-03): path + browse, a one-level
/// tree, the rights choice in two radio rows, and — expert — the exact mount string the
/// choice will be written as. One shared instance serves every caller: each
/// <see cref="Open"/> names its own callback.
/// </summary>
public sealed class FolderPickerViewModel : ObservableObject
{
    private const int MaxChildren = 100;

    private readonly IDirectoryProbe _directories;
    private readonly IPathPicker _picker;
    private readonly IStudioStrings _strings;

    private Action<MountDefinition>? _onPicked;
    private IReadOnlyList<string> _mountedPhysical = [];
    private IReadOnlyList<string> _mountedVirtual = [];
    private string _path = "";
    private bool _isReadOnly = true;
    private bool _isOpen;

    /// <summary>Builds the picker over its seams.</summary>
    public FolderPickerViewModel(
        IDirectoryProbe? directories = null,
        IPathPicker? picker = null,
        IStudioStrings? strings = null)
    {
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _picker = picker ?? NullPathPicker.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;

        BrowseCommand = new RelayCommand(Browse);
        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(Close);
        PickReadOnlyCommand = new RelayCommand(() => IsReadOnly = true);
        PickReadWriteCommand = new RelayCommand(() => IsReadOnly = false);
    }

    /// <summary>Whether the modal is showing.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>
    /// Shows the modal. <paramref name="existingMounts"/> (mount strings) drive the
    /// "déjà autorisé" notes and the virtual-path uniqueness; <paramref name="onPicked"/>
    /// receives the confirmed mount.
    /// </summary>
    public void Open(IReadOnlyList<string> existingMounts, Action<MountDefinition> onPicked, string? initialPath = null)
    {
        ArgumentNullException.ThrowIfNull(existingMounts);
        ArgumentNullException.ThrowIfNull(onPicked);

        var parsed = new List<MountDefinition>();
        foreach (var mountString in existingMounts)
        {
            if (MountDefinition.TryParse(mountString, out var mount, out _) && mount is not null)
                parsed.Add(mount);
        }

        _mountedPhysical = [.. parsed.Select(m => m.PhysicalPath.TrimEnd('/', '\\'))];
        _mountedVirtual = [.. parsed.Select(m => m.VirtualPath)];
        _onPicked = onPicked;
        _isReadOnly = true;
        _path = initialPath is { Length: > 0 }
            ? initialPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IsOpen = true;
        OnPropertiesChanged(nameof(Path), nameof(IsReadOnly), nameof(IsReadWrite), nameof(MountPreview));
        RebuildTree();
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    /// <summary>The chosen physical path — typed, browsed, or clicked in the tree.</summary>
    public string Path
    {
        get => _path;
        set
        {
            if (!SetProperty(ref _path, value))
                return;

            RebuildTree();
            OnPropertiesChanged(nameof(MountPreview));
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The tree rows: the ancestors of the current path, then its children.</summary>
    public ObservableCollection<FolderNodeViewModel> Nodes { get; } = [];

    /// <summary>True when the "Lecture seule" row is chosen (the default).</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        private set
        {
            if (SetProperty(ref _isReadOnly, value))
                OnPropertiesChanged(nameof(IsReadWrite), nameof(MountPreview));
        }
    }

    /// <summary>True when the "Lecture et écriture" row is chosen.</summary>
    public bool IsReadWrite => !_isReadOnly;

    /// <summary>Chooses "Lecture seule".</summary>
    public RelayCommand PickReadOnlyCommand { get; }

    /// <summary>Chooses "Lecture et écriture".</summary>
    public RelayCommand PickReadWriteCommand { get; }

    /// <summary>Opens the OS folder browser.</summary>
    public RelayCommand BrowseCommand { get; }

    /// <summary>"Autoriser ce dossier".</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>Closes without picking.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>The mount this choice builds, as the runtime will read it — the expert note.</summary>
    public string MountPreview => BuildMount().ToMountString();

    /// <summary>The path must name an existing folder — the tree only browses what is there.</summary>
    public bool CanConfirm => _path.Trim() is { Length: > 0 } path && _directories.Exists(path);

    private void Browse()
    {
        if (_picker.PickFolder(_strings[StudioStringKeys.DialogSelectMountFolder], _path) is { Length: > 0 } picked)
            Path = picked;
    }

    private void Confirm()
    {
        var onPicked = _onPicked;
        Close();
        onPicked?.Invoke(BuildMount());
    }

    private void Close()
    {
        _onPicked = null;
        IsOpen = false;
    }

    private MountDefinition BuildMount() => new()
    {
        PhysicalPath = _path.Trim(),
        VirtualPath = VirtualPathCandidate(),
        Rights = _isReadOnly ? MountRights.ReadOnly : MountRights.ReadWrite,
    };

    /// <summary>
    /// The virtual spelling: the folder's own name, lowercased — or the first free
    /// suggestion when that name is taken or unusable. Same derivation as the mounts
    /// editor's "Autoriser un dossier".
    /// </summary>
    private string VirtualPathCandidate()
    {
        var name = System.IO.Path.GetFileName(_path.Trim().TrimEnd('/', '\\'));
#pragma warning disable CA1308 // virtual paths are lowercase by convention, not a normalization round-trip
        var candidate = "/" + (name is { Length: > 0 } ? name.ToLowerInvariant() : "docs");
#pragma warning restore CA1308
        if (!MountDefinition.IsValidVirtualPath(candidate) || _mountedVirtual.Contains(candidate, StringComparer.Ordinal))
        {
            candidate = MountDefinition.SuggestedVirtualPaths
                .FirstOrDefault(s => !_mountedVirtual.Contains(s, StringComparer.Ordinal)) ?? "/workspace";
        }

        return candidate;
    }

    private void RebuildTree()
    {
        Nodes.Clear();
        var current = _path.Trim();
        if (current.Length == 0)
            return;

        // The lineage, root first — each ancestor is a clickable way back up.
        var lineage = new List<string>();
        for (var step = current.TrimEnd('/', '\\'); step is { Length: > 0 };)
        {
            lineage.Insert(0, step);
            var parent = System.IO.Path.GetDirectoryName(step);
            if (parent is not { Length: > 0 } || string.Equals(parent, step, StringComparison.Ordinal))
                break;
            step = parent.TrimEnd('/', '\\') is { Length: > 0 } trimmed ? trimmed : parent;
        }

        for (var i = 0; i < lineage.Count; i++)
        {
            var isCurrent = i == lineage.Count - 1;
            Nodes.Add(Node(lineage[i], i, isCurrent));
        }

        foreach (var child in _directories.ListSubdirectories(current).Take(MaxChildren))
            Nodes.Add(Node(child, lineage.Count, isSelected: false));
    }

    private FolderNodeViewModel Node(string path, int depth, bool isSelected)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        var mounted = _mountedPhysical.Contains(path.TrimEnd('/', '\\'), StringComparer.OrdinalIgnoreCase);
        return new FolderNodeViewModel(
            path,
            name is { Length: > 0 } ? name : path,
            depth,
            isSelected,
            mounted ? _strings[StudioStringKeys.PickerAlreadyMounted] : null,
            node => Path = node.Path);
    }
}
