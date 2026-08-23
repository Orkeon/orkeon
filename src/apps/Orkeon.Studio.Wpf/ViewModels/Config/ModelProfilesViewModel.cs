using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// One profile card on the "Modèle d'IA" tab. A thin projection over
/// <see cref="ModelProfile"/>; the commands are handed in by the list so every mutation goes
/// through one place.
/// </summary>
public sealed class ModelProfileItemViewModel
{
    internal ModelProfileItemViewModel(
        ModelProfile profile,
        bool isDefault,
        bool isStudio,
        ModelProfilesViewModel owner)
    {
        Profile = profile;
        IsDefault = isDefault;
        IsStudio = isStudio;
        SetDefaultCommand = new RelayCommand(() => owner.SetDefault(profile.Name));
        EditCommand = new RelayCommand(() => owner.BeginEdit(profile));
        DuplicateCommand = new RelayCommand(() => owner.Duplicate(profile));
        DeleteCommand = new RelayCommand(() => owner.Delete(profile.Name), () => owner.CanDelete);
    }

    /// <summary>The profile being shown.</summary>
    public ModelProfile Profile { get; }

    /// <summary>Display name.</summary>
    public string Name => Profile.Name;

    /// <summary>"Provider · model" one-liner.</summary>
    public string Summary => Profile.Summary;

    /// <summary>Endpoint, shown in expert mode only.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Presentation of the profile's endpoint text, shown verbatim in a mono label.")]
    public string? Endpoint => Profile.BaseUrl;

    /// <summary>Provider label for the non-default badge.</summary>
    public string Provider => Profile.Provider ?? "";

    /// <summary>True for the elected default.</summary>
    public bool IsDefault { get; }

    /// <summary>True for the profile Studio's assistant uses.</summary>
    public bool IsStudio { get; }

    /// <summary>Elects this profile as the machine default.</summary>
    public RelayCommand SetDefaultCommand { get; }

    /// <summary>Opens the editor over this profile.</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>Adds a copy of this profile.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Removes this profile (disabled on the last one).</summary>
    public RelayCommand DeleteCommand { get; }
}

/// <summary>
/// The editor overlay for one profile ("Nouveau réglage" / "Modifier le réglage"). Picking a
/// provider seeds the endpoint and model from the preset catalogue; expert mode exposes both
/// fields for hand-editing. The API key never appears here — it stays in the environment.
/// </summary>
public sealed class ModelProfileEditorViewModel : ObservableObject
{
    private readonly ModelProfilesViewModel _owner;
    private readonly ILlmEndpointProbe _probe;
    private LlmPresetInfo? _selectedProvider;
    private string _name;
    private string? _baseUrl;
    private string? _model;
    private string? _connectionTestResult;

    internal ModelProfileEditorViewModel(
        ModelProfilesViewModel owner,
        IReadOnlyList<LlmPresetInfo> providers,
        ModelProfile profile,
        string? previousName,
        ILlmEndpointProbe probe)
    {
        _owner = owner;
        _probe = probe;
        Providers = providers;
        PreviousName = previousName;
        _name = profile.Name;
        _baseUrl = profile.BaseUrl;
        _model = profile.Model;
        _selectedProvider = providers.FirstOrDefault(p => string.Equals(p.Title, profile.Provider, StringComparison.Ordinal));

        SaveCommand = new RelayCommand(Save, () => CanSave);
        CancelCommand = new RelayCommand(() => _owner.CancelEdit());
        SelectProviderCommand = new RelayCommand(p => SelectedProvider = p as LlmPresetInfo);
        TestConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(CancellationToken.None));
    }

    /// <summary>The provider choices — the same catalogue `orkeon init` offers.</summary>
    public IReadOnlyList<LlmPresetInfo> Providers { get; }

    /// <summary>The name under which the profile was opened; null when creating.</summary>
    public string? PreviousName { get; }

    /// <summary>True when this editor creates a profile rather than reworking one.</summary>
    public bool IsNew => PreviousName is null;

    /// <summary>Profile name, the identity teams reference.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(NameCollision));
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The picked provider; picking one seeds the endpoint and the model.</summary>
    public LlmPresetInfo? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (!SetProperty(ref _selectedProvider, value) || value is null)
                return;

            BaseUrl = value.DefaultBaseUrl;
            Model = value.DefaultModel;
        }
    }

    /// <summary>Endpoint base URL (expert field).</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "User-typed form value round-tripped verbatim into a JSON string field; " +
                        "a half-typed URL must be carried and re-shown, which System.Uri cannot do.")]
    public string? BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    /// <summary>Model identifier (expert field).</summary>
    public string? Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    /// <summary>A profile needs a name of its own; everything else may be filled in later.</summary>
    public bool CanSave => _name.Trim().Length > 0 && !NameCollision;

    /// <summary>True while the typed name already belongs to another profile.</summary>
    public bool NameCollision => _owner.IsNameTaken(_name.Trim(), PreviousName);

    /// <summary>Outcome line of the last connection probe.</summary>
    public string? ConnectionTestResult
    {
        get => _connectionTestResult;
        private set => SetProperty(ref _connectionTestResult, value);
    }

    /// <summary>Commits the profile to the set.</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>Closes the editor without touching the set.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Picks the provider row carried as the command parameter.</summary>
    public RelayCommand SelectProviderCommand { get; }

    /// <summary>Probes the endpoint with the key resolved from the environment.</summary>
    public AsyncRelayCommand TestConnectionCommand { get; }

    private void Save()
    {
        if (!CanSave)
            return;

        _owner.CommitEdit(new ModelProfile
        {
            Name = _name.Trim(),
            Provider = _selectedProvider?.Title,
            Model = _model,
            BaseUrl = _baseUrl,
        }, PreviousName);
    }

    /// <summary>Probes the endpoint; public so tests can await it with a token.</summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        var result = await _probe.ProbeAsync(
            new LlmProbeRequest { BaseUrl = BaseUrl, ApiKey = LlmApiKeyResolver.Resolve(null) },
            cancellationToken).ConfigureAwait(true);

        ConnectionTestResult = result.Message;
    }
}

/// <summary>
/// The "Modèle d'IA" tab: the named, reusable model settings of this machine, the default
/// election, and the profile Studio's own assistant runs on. Every mutation is persisted to
/// the store immediately (its file is Studio state, like the history); electing a default
/// additionally mirrors it into the settings document's <c>Llm</c> section, which is what the
/// CLI reads when it runs outside Studio — that write goes through the ordinary dirty/save
/// cycle of the settings screen.
/// </summary>
public sealed class ModelProfilesViewModel : ObservableObject
{
    private readonly IModelProfileStore _store;
    private readonly LlmSectionViewModel _llm;
    private readonly IStudioStrings _strings;
    private readonly ILlmEndpointProbe _probe;
    private ModelProfileSet _set = ModelProfileSet.Empty;
    private ModelProfileEditorViewModel? _editor;

    /// <summary>Builds the tab over its seams.</summary>
    public ModelProfilesViewModel(
        IModelProfileStore? store,
        LlmSectionViewModel llm,
        IStudioStrings? strings = null,
        ILlmEndpointProbe? probe = null)
    {
        ArgumentNullException.ThrowIfNull(llm);

        _store = store ?? new InMemoryModelProfileStore();
        _llm = llm;
        _strings = strings ?? EnglishStudioStrings.Instance;
        // Lives as long as the tab, which lives as long as the window.
        _probe = probe ?? HttpLlmEndpointProbe.ForCurrentMachine();
        NewProfileCommand = new RelayCommand(BeginCreate);
    }

    /// <summary>The profile cards, rebuilt after every mutation.</summary>
    public ObservableCollection<ModelProfileItemViewModel> Profiles { get; } = [];

    /// <summary>The profile names, for the assistant picker.</summary>
    public ObservableCollection<string> ProfileNames { get; } = [];

    /// <summary>The current set, for consumers outside this tab (the wizard's gate).</summary>
    public ModelProfileSet Set => _set;

    /// <summary>Name of the assistant's profile; null while unconfigured.</summary>
    public string? StudioProfileName
    {
        get => _set.StudioProfile;
        set
        {
            if (value is not { Length: > 0 } || string.Equals(_set.StudioProfile, value, StringComparison.Ordinal))
                return;

            Mutate(_set.WithStudio(value));
        }
    }

    /// <summary>True once the assistant has a model — the creation wizard is gated on this.</summary>
    public bool HasStudioProfile => _set.Studio is not null;

    /// <summary>Name of the default profile, or null.</summary>
    public string? DefaultProfileName => _set.DefaultProfile;

    /// <summary>True while the set holds no profile at all — the empty-state hint shows.</summary>
    public bool IsEmpty => _set.Profiles.Count == 0;

    /// <summary>Deleting is allowed only while more than one profile remains.</summary>
    public bool CanDelete => _set.Profiles.Count > 1;

    /// <summary>The editor overlay; null while closed.</summary>
    public ModelProfileEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
                OnPropertyChanged(nameof(IsEditorOpen));
        }
    }

    /// <summary>Whether the editor overlay is showing.</summary>
    public bool IsEditorOpen => _editor is not null;

    /// <summary>Opens the editor over a fresh profile seeded from the first preset.</summary>
    public RelayCommand NewProfileCommand { get; }

    /// <summary>Loads the persisted set. Called once, from the window's deferred initialize.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _set = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);
        Rebuild();
    }

    /// <summary>Elects <paramref name="name"/> as the default and mirrors it into the settings.</summary>
    public void SetDefault(string name)
    {
        var mutated = _set.WithDefault(name);
        if (ReferenceEquals(mutated, _set))
            return;

        Mutate(mutated);
        ApplyDefaultToSettings();
    }

    internal void BeginEdit(ModelProfile profile) =>
        Editor = new ModelProfileEditorViewModel(this, ProviderCatalog(), profile, profile.Name, _probe);

    internal void Duplicate(ModelProfile profile)
    {
        var name = _set.CopyNameFor(profile.Name, _strings[StudioStringKeys.ProfileCopySuffix]);
        Mutate(_set.Upsert(profile with { Name = name }));
    }

    internal void Delete(string name)
    {
        if (!CanDelete)
            return;

        var wasDefault = string.Equals(_set.DefaultProfile, name, StringComparison.Ordinal);
        Mutate(_set.Remove(name));
        if (wasDefault)
            ApplyDefaultToSettings();
    }

    internal void CommitEdit(ModelProfile profile, string? previousName)
    {
        var wasDefault = previousName is not null
            && string.Equals(_set.DefaultProfile, previousName, StringComparison.Ordinal);

        Mutate(_set.Upsert(profile, previousName));
        Editor = null;

        if (wasDefault || string.Equals(_set.DefaultProfile, profile.Name, StringComparison.Ordinal))
            ApplyDefaultToSettings();
    }

    internal void CancelEdit() => Editor = null;

    /// <summary>Whether <paramref name="name"/> already belongs to a profile other than the one being edited.</summary>
    internal bool IsNameTaken(string name, string? previousName) =>
        _set.Profiles.Any(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal)
            && !string.Equals(p.Name, previousName, StringComparison.Ordinal));

    private void BeginCreate()
    {
        var catalog = ProviderCatalog();
        var seed = catalog.Count > 0 ? catalog[0] : null;
        Editor = new ModelProfileEditorViewModel(
            this,
            catalog,
            new ModelProfile
            {
                Name = _strings[StudioStringKeys.ProfileNewName],
                Provider = seed?.Title,
                Model = seed?.DefaultModel,
                BaseUrl = seed?.DefaultBaseUrl,
            },
            previousName: null,
            _probe);
    }

    private IReadOnlyList<LlmPresetInfo> ProviderCatalog() => LlmPresets.CatalogFor(_strings);

    private void Mutate(ModelProfileSet set)
    {
        _set = set;
        Rebuild();
        _ = _store.SaveAsync(set);
    }

    private void ApplyDefaultToSettings()
    {
        if (_set.Default is not { } profile)
            return;

        // Mirrored into the Llm section so a bare `orkeon run` follows the same election;
        // the write lands in the settings document and travels through the screen's own
        // dirty/save cycle — Studio never saves the settings file behind the user's back.
        _llm.Model = profile.Model;
        _llm.BaseUrl = profile.BaseUrl;
    }

    private void Rebuild()
    {
        Profiles.Clear();
        ProfileNames.Clear();
        foreach (var profile in _set.Profiles)
        {
            Profiles.Add(new ModelProfileItemViewModel(
                profile,
                isDefault: string.Equals(profile.Name, _set.DefaultProfile, StringComparison.Ordinal),
                isStudio: string.Equals(profile.Name, _set.StudioProfile, StringComparison.Ordinal),
                this));
            ProfileNames.Add(profile.Name);
        }

        OnPropertiesChanged(
            nameof(StudioProfileName), nameof(HasStudioProfile),
            nameof(DefaultProfileName), nameof(IsEmpty), nameof(CanDelete), nameof(Set));
    }
}
