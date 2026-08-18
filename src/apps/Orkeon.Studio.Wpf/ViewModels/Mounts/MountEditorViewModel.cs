using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

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
    private string _physicalPath = "";
    private string _virtualPath = "";
    private MountRights _rights = MountRights.ReadOnly;
    private string _rawText = "";

    /// <summary>Creates an empty, editable mount row.</summary>
    public MountEditorViewModel(IStudioStrings? strings = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        IsParsed = true;
        Overrides.CollectionChanged += OnOverridesChanged;
    }

    private MountEditorViewModel(string rawText, IStudioStrings? strings)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        IsParsed = false;
        _rawText = rawText;
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
                OnPropertyChanged(nameof(RightsLabel));
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

    /// <summary>Whether <see cref="VirtualPath"/> satisfies the Domain rule, checked as the user types.</summary>
    public bool IsVirtualPathValid => MountDefinition.IsValidVirtualPath(VirtualPath);

    /// <summary>The serialized mount, as it will be written to the JSON array or passed to <c>--mount</c>.</summary>
    public string MountString => IsParsed ? ToDefinition().ToMountString() : RawText;

    /// <summary>Builds a row from a parsed Core definition.</summary>
    public static MountEditorViewModel FromDefinition(MountDefinition definition, IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var mount = new MountEditorViewModel(strings)
        {
            PhysicalPath = definition.PhysicalPath,
            VirtualPath = definition.VirtualPath,
            Rights = definition.Rights,
        };

        foreach (var item in definition.Overrides)
            mount.Overrides.Add(new SubPathOverrideViewModel(item, strings));

        return mount;
    }

    /// <summary>
    /// Builds a row from a raw entry: parsed into a form when the Core parser accepts it, kept
    /// verbatim and read-only when it does not.
    /// </summary>
    public static MountEditorViewModel FromRaw(string entry, IStudioStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return MountDefinition.TryParse(entry, out var definition, out _)
            ? FromDefinition(definition, strings)
            : new MountEditorViewModel(entry, strings);
    }

    /// <summary>Converts the form back to the Core record.</summary>
    /// <exception cref="InvalidOperationException">The row is an unparsed raw entry.</exception>
    public MountDefinition ToDefinition()
    {
        if (!IsParsed)
            throw new InvalidOperationException("This entry could not be parsed and has no form representation.");

        return new MountDefinition
        {
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
        OnPropertiesChanged(nameof(RightsChoices), nameof(RightsLabel));

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
