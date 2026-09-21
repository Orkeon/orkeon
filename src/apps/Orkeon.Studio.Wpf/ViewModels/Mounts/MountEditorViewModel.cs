using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>
/// One VFS mount, edited as a form instead of as the Docker-style
/// <c>&lt;physical&gt;:&lt;virtual&gt;:&lt;rights&gt;[;&lt;sub-path&gt;:&lt;rights&gt;]*</c> string (spec §4.5).
/// <para>
/// An entry that the Core parser rejects is kept verbatim in <see cref="RawText"/> with
/// <see cref="IsParsed"/> false, so loading and saving a file never silently drops a mount the user
/// wrote by hand; the validator reports it instead.
/// </para>
/// </summary>
public sealed class MountEditorViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private readonly IClipboardService? _clipboard;
    private string _physicalPath = "";
    private string _virtualPath = "";
    private MountRights _rights = MountRights.ReadOnly;
    private string _rawText = "";
    private IReadOnlyList<string> _usedByTeams = [];

    /// <summary>
    /// Creates an empty, editable mount row — with an id of its own from the start (VFS-90)
    /// when <paramref name="withId"/> is true, the settings' case; the launcher's per-run rows
    /// are not settings entries and carry none.
    /// </summary>
    public MountEditorViewModel(IStudioStrings? strings = null, IClipboardService? clipboard = null, bool withId = true)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _clipboard = clipboard;
        IsParsed = true;
        Id = withId ? MountId.Create() : null;
        IsIdNew = withId;
        CopyIdCommand = new RelayCommand(() => { if (Id is { } id) _clipboard?.SetText(id.ToString()); }, () => Id is not null);
        Overrides.CollectionChanged += OnOverridesChanged;
    }

    /// <summary>
    /// The entry's identity (VFS-90): what a team's sidecar and a crew's <c>mounts:</c> block
    /// name this entry by. Null only for an unparsed raw entry.
    /// </summary>
    public MountId? Id { get; private set; }

    /// <summary>Whether the id was assigned in this session and is written at the next save.</summary>
    public bool IsIdNew { get; private set; }

    /// <summary>The last six characters of the id — what the row shows.</summary>
    public string ShortId => Id?.ToString() is { } text ? text[^6..] : "";

    /// <summary>"Id 01J…" or "Id 01J… — assigned on save".</summary>
    public string IdDisplay
    {
        get
        {
            if (Id is null)
                return "";
            var key = IsIdNew ? StudioStringKeys.MountIdAssignedOnSave : StudioStringKeys.MountId;
            return string.Format(CultureInfo.CurrentCulture, _strings[key], Id.ToString());
        }
    }

    /// <summary>Copies the full id to the clipboard — for a hand-written crew's <c>mounts:</c> block.</summary>
    public RelayCommand CopyIdCommand { get; }

    /// <summary>The names of the adopted teams naming this entry by id.</summary>
    public IReadOnlyList<string> UsedByTeams
    {
        get => _usedByTeams;
        internal set
        {
            _usedByTeams = value;
            OnPropertiesChanged(nameof(UsedByTeams), nameof(IsReferenced), nameof(UsedByDisplay));
        }
    }

    /// <summary>Whether a team depends on this entry.</summary>
    public bool IsReferenced => UsedByTeams.Count > 0;

    /// <summary>"Used by Veille, Audit", or empty.</summary>
    public string UsedByDisplay => IsReferenced
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.MountUsedBy], string.Join(", ", UsedByTeams))
        : "";

    /// <summary>Gives the row an id when it has none; returns whether one was assigned.</summary>
    internal bool EnsureId()
    {
        if (!IsParsed || Id is not null)
            return false;

        Id = MountId.Create();
        IsIdNew = true;
        OnPropertiesChanged(nameof(Id), nameof(IsIdNew), nameof(ShortId), nameof(IdDisplay), nameof(MountString));
        CopyIdCommand.RaiseCanExecuteChanged();
        return true;
    }

    private MountEditorViewModel(string rawText, IStudioStrings? strings, IClipboardService? clipboard)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _clipboard = clipboard;
        IsParsed = false;
        _rawText = rawText;
        CopyIdCommand = new RelayCommand(() => { }, () => false);
        Overrides.CollectionChanged += OnOverridesChanged;
    }

    /// <summary>Raised whenever any field changes, so the owning editor can re-validate.</summary>
    public event EventHandler? Edited;

    /// <summary>
    /// Whether this row was produced from a well-formed mount string. A row that is not parsed is
    /// shown read-only as <see cref="RawText"/> and round-trips unchanged.
    /// </summary>
    public bool IsParsed { get; }

    /// <summary>The original text of an entry the parser rejected.</summary>
    public string RawText
    {
        get => _rawText;
        private set => SetProperty(ref _rawText, value);
    }

    /// <summary>The folder on disk that backs the mount. Picked in a browser, per spec §4.5.</summary>
    public string PhysicalPath
    {
        get => _physicalPath;
        set
        {
            if (SetProperty(ref _physicalPath, value))
                RaiseEdited();
        }
    }

    /// <summary>The path the agents see, e.g. <c>/workspace</c>.</summary>
    public string VirtualPath
    {
        get => _virtualPath;
        set
        {
            if (SetProperty(ref _virtualPath, value))
            {
                OnPropertyChanged(nameof(IsVirtualPathValid));
                RaiseEdited();
            }
        }
    }

    /// <summary>The default rights for the whole mount.</summary>
    public MountRights Rights
    {
        get => _rights;
        set
        {
            if (SetProperty(ref _rights, value))
            {
                OnPropertiesChanged(nameof(RightsLabel), nameof(RightsBadge));
                RaiseEdited();
            }
        }
    }

    /// <summary>Per-sub-path rights that differ from <see cref="Rights"/>.</summary>
    public ObservableCollection<SubPathOverrideViewModel> Overrides { get; } = [];

    /// <summary>The closed list of rights tokens, exactly the ones the Domain parser accepts.</summary>
    public IReadOnlyList<MountRightsChoice> RightsChoices => MountRightsTokens.ChoicesFor(_strings);

    /// <summary>The virtual paths suggested next to the text box.</summary>
    public static IReadOnlyList<string> SuggestedVirtualPaths => MountDefinition.SuggestedVirtualPaths;

    /// <summary>The explicit wording of the selected rights, e.g. "Read only".</summary>
    public string RightsLabel => MountRightsTokens.GetLabel(Rights, _strings);

    /// <summary>
    /// The one-word reading of the selected rights, e.g. "write" — what the mount list's badge
    /// shows (STUDIO-16, D-05). The badge used to carry <see cref="RightsLabel"/>, fifty-two
    /// characters that took the whole row and pushed the virtual name out of it; the label is
    /// the badge's tooltip now.
    /// </summary>
    public string RightsBadge => MountRightsTokens.GetBadge(Rights, _strings);

    /// <summary>Whether <see cref="VirtualPath"/> satisfies the Domain rule, checked as the user types.</summary>
    public bool IsVirtualPathValid => MountDefinition.IsValidVirtualPath(VirtualPath);

    /// <summary>The serialized mount, as it will be written to the JSON array or passed to <c>--mount</c>.</summary>
    public string MountString => IsParsed ? ToDefinition().ToMountString() : RawText;

    /// <summary>
    /// Builds a row from a parsed Core definition. An entry declared without an id gets one
    /// here, marked as new: it exists from the first paint and is written at the next save.
    /// </summary>
    public static MountEditorViewModel FromDefinition(
        MountDefinition definition, IStudioStrings? strings = null, IClipboardService? clipboard = null, bool assignId = true)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var mount = new MountEditorViewModel(strings, clipboard, withId: assignId)
        {
            PhysicalPath = definition.PhysicalPath,
            VirtualPath = definition.VirtualPath,
            Rights = definition.Rights,
        };
        if (definition.Id is { } id)
        {
            mount.Id = id;
            mount.IsIdNew = false;
        }

        foreach (var item in definition.Overrides)
            mount.Overrides.Add(new SubPathOverrideViewModel(item, strings));

        return mount;
    }

    /// <summary>
    /// Builds a row from a raw entry: parsed into a form when the Core parser accepts it, kept
    /// verbatim and read-only when it does not.
    /// </summary>
    public static MountEditorViewModel FromRaw(
        string entry, IStudioStrings? strings = null, IClipboardService? clipboard = null, bool assignId = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return MountDefinition.TryParse(entry, out var definition, out _)
            ? FromDefinition(definition, strings, clipboard, assignId)
            : new MountEditorViewModel(entry, strings, clipboard);
    }

    /// <summary>Converts the form back to the Core record.</summary>
    /// <exception cref="InvalidOperationException">The row is an unparsed raw entry.</exception>
    public MountDefinition ToDefinition()
    {
        if (!IsParsed)
            throw new InvalidOperationException("This entry could not be parsed and has no form representation.");

        return new MountDefinition
        {
            Id = Id,
            PhysicalPath = PhysicalPath,
            VirtualPath = VirtualPath,
            Rights = Rights,
            Overrides = [.. Overrides.Select(o => o.ToOverride())],
        };
    }

    /// <summary>Appends an empty override row.</summary>
    public SubPathOverrideViewModel AddOverride()
    {
        var item = new SubPathOverrideViewModel(_strings);
        Overrides.Add(item);
        return item;
    }

    /// <summary>
    /// Re-emits every culture-dependent property; called by the owning editor on a language
    /// switch, so transient rows never subscribe to the port themselves (STUDIO-11).
    /// </summary>
    public void RefreshCulture()
    {
        OnPropertiesChanged(nameof(RightsChoices), nameof(RightsLabel), nameof(RightsBadge), nameof(IdDisplay), nameof(UsedByDisplay));

        foreach (var item in Overrides)
            item.RefreshCulture();
    }

    /// <summary>Removes an override row.</summary>
    public void RemoveOverride(SubPathOverrideViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Overrides.Remove(item);
    }

    /// <inheritdoc />
    public override string ToString() => IsParsed
        ? string.Create(CultureInfo.InvariantCulture, $"{VirtualPath} ← {PhysicalPath} ({MountRightsTokens.ToToken(Rights)})")
        : RawText;

    private void OnOverridesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<SubPathOverrideViewModel>() ?? [])
            item.Edited += OnOverrideEdited;

        foreach (var item in e.OldItems?.OfType<SubPathOverrideViewModel>() ?? [])
            item.Edited -= OnOverrideEdited;

        RaiseEdited();
    }

    private void OnOverrideEdited(object? sender, EventArgs e) => RaiseEdited();

    private void RaiseEdited()
    {
        OnPropertiesChanged(nameof(MountString), nameof(Display));
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The one-line summary shown in the mount list.</summary>
    public string Display => ToString();
}
