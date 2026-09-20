using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using System.Globalization;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// One profile card on the model-settings tab. A thin projection over
/// <see cref="ModelProfile"/>; the commands are handed in by the list so every mutation goes
/// through one place.
/// </summary>
public sealed class ModelProfileItemViewModel
{
    internal ModelProfileItemViewModel(
        ModelProfile profile,
        bool isDefault,
        bool isStudio,
        ModelProfilesViewModel owner,
        IReadOnlyList<string>? usedByTeams = null)
    {
        Profile = profile;
        IsDefault = isDefault;
        IsStudio = isStudio;
        UsedByTeams = usedByTeams ?? [];
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

    /// <summary>The adopted teams whose sidecar names this profile (audit 07/16 chips).</summary>
    public IReadOnlyList<string> UsedByTeams { get; }

    /// <summary>Whether the "used by" row shows.</summary>
    public bool IsUsedByTeams => UsedByTeams.Count > 0;

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
/// The editor overlay for one profile (the new-profile / edit-profile form). Picking a
/// provider seeds the endpoint and model from the preset catalogue; expert mode exposes both
/// fields for hand-editing. The API key never appears here — it stays in the environment.
/// </summary>
/// <summary>One position of the profile editor's thinking switch: null is the provider's default.</summary>
/// <param name="Value">What the profile pins — null, true or false.</param>
/// <param name="Label">The localized label shown in the combo box.</param>
public sealed record ThinkingChoice(bool? Value, string Label);

public sealed class ModelProfileEditorViewModel : ObservableObject
{
    private readonly ModelProfilesViewModel _owner;
    private readonly ILlmEndpointProbe _probe;
    private readonly IApiKeyStore _keyStore;
    private readonly IStudioStrings _strings;
    private LlmPresetInfo? _selectedProvider;
    private string _name;
    private string? _baseUrl;
    private string? _model;
    private string? _apiKeyEnv;
    private string _apiKeyInput = "";
    private string? _connectionTestResult;
    private string _temperatureText = "";
    private string _timeoutText = "";
    private string _maxTokensText = "";
    private bool? _thinkingEnabled;
    private string _thinkingEffortText = "";

    internal ModelProfileEditorViewModel(
        ModelProfilesViewModel owner,
        IReadOnlyList<LlmPresetInfo> providers,
        ModelProfile profile,
        string? previousName,
        ILlmEndpointProbe probe,
        IApiKeyStore keyStore,
        IStudioStrings strings)
    {
        _owner = owner;
        _probe = probe;
        _keyStore = keyStore;
        _strings = strings;
        Providers = providers;
        PreviousName = previousName;
        _name = profile.Name;
        _baseUrl = profile.BaseUrl;
        _model = profile.Model;
        _apiKeyEnv = profile.KeyEnvName;
        _temperatureText = profile.Temperature is { } temperature
            ? temperature.ToString(CultureInfo.InvariantCulture)
            : "";
        _timeoutText = profile.TimeoutSeconds is { } timeout
            ? timeout.ToString(CultureInfo.InvariantCulture)
            : "";
        _maxTokensText = profile.MaxTokens is { } maxTokens
            ? maxTokens.ToString(CultureInfo.InvariantCulture)
            : "";
        _thinkingEnabled = profile.ThinkingEnabled;
        _thinkingEffortText = profile.ThinkingEffort ?? "";
        ThinkingChoices =
        [
            new ThinkingChoice(null, strings[StudioStringKeys.ProfileThinkingProviderDefault]),
            new ThinkingChoice(true, strings[StudioStringKeys.ProfileThinkingOn]),
            new ThinkingChoice(false, strings[StudioStringKeys.ProfileThinkingOff]),
        ];
        _selectedProvider = providers.FirstOrDefault(p => string.Equals(p.Title, profile.Provider, StringComparison.Ordinal));

        SaveCommand = new RelayCommand(Save, () => CanSave);
        CancelCommand = new RelayCommand(() => _owner.CancelEdit());
        SelectProviderCommand = new RelayCommand(p => SelectedProvider = p as LlmPresetInfo);
        TestConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync(CancellationToken.None));
        StoreKeyCommand = new RelayCommand(StoreKey, () => _apiKeyInput.Trim().Length > 0);
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
            var previous = _selectedProvider;
            if (!SetProperty(ref _selectedProvider, value) || value is null)
                return;

            BaseUrl = value.DefaultBaseUrl;
            Model = value.DefaultModel;
            _apiKeyEnv = value.DefaultApiKeyEnv;
            SeedTimeoutFor(value, previous);
            OnPropertyChanged(nameof(TimeoutHint));
            ConnectionTestResult = null;
            OnPropertyChanged(nameof(RequiresApiKey));
            OnPropertyChanged(nameof(ApiKeyEnvName));
            OnPropertyChanged(nameof(HasStoredKey));
            OnPropertyChanged(nameof(KeyStatusText));
            OnPropertyChanged(nameof(KeyBlockTitle));
            OnPropertyChanged(nameof(KeyConsoleUrl));
            OnPropertyChanged(nameof(IsNone));
            OnPropertyChanged(nameof(ShowFields));
            OnPropertyChanged(nameof(UrlAlwaysVisible));
            OnPropertyChanged(nameof(ShowLocalNote));
            OnPropertyChanged(nameof(ShowNoneNote));
            OnPropertyChanged(nameof(ShowTestRow));
            OnPropertyChanged(nameof(MaxTokensHint));   // an entry can hold on one provider only (LLM-10)
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Endpoint base URL (expert field).</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "User-typed form value round-tripped verbatim into a JSON string field; " +
                        "a half-typed URL must be carried and re-shown, which System.Uri cannot do.")]
    public string? BaseUrl
    {
        get => _baseUrl;
        set
        {
            // The catch-all provider gates Save on this field: the button must wake up
            // as the URL is typed, not on the next unrelated notification.
            if (SetProperty(ref _baseUrl, value))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Model identifier (expert field).</summary>
    public string? Model
    {
        get => _model;
        set
        {
            if (!SetProperty(ref _model, value))
                return;

            OnPropertyChanged(nameof(MaxTokensHint));   // the hint follows the model, not the provider
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>True when the picked provider authenticates requests.</summary>
    public bool RequiresApiKey => _selectedProvider?.RequiresApiKey == true;

    /// <summary>
    /// Name of the environment variable the key lives in — the profile's own when set,
    /// else the provider's conventional one, else the runtime's native variable.
    /// </summary>
    public string ApiKeyEnvName =>
        _apiKeyEnv is { Length: > 0 } explicitName
            ? explicitName
            : _selectedProvider?.DefaultApiKeyEnv ?? LlmPresets.DefaultApiKeyEnv;

    /// <summary>
    /// The pasted key, held only until it is remembered. Never pre-filled from the stored
    /// value — the editor shows whether a key is in place, not what it is.
    /// </summary>
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set
        {
            if (SetProperty(ref _apiKeyInput, value))
                StoreKeyCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The provider's card groups, for the three sections of the design.</summary>
    public IEnumerable<LlmPresetInfo> LocalProviders => Providers.Where(p => p.Kind == LlmPresetKind.Local);

    /// <inheritdoc cref="LocalProviders" />
    public IEnumerable<LlmPresetInfo> CloudProviders => Providers.Where(p => p.Kind == LlmPresetKind.Cloud);

    /// <inheritdoc cref="LocalProviders" />
    public IEnumerable<LlmPresetInfo> OtherProviders =>
        Providers.Where(p => p.Kind is LlmPresetKind.Other or LlmPresetKind.None);

    /// <summary>True when the picked card is the echo fallback.</summary>
    public bool IsNone => _selectedProvider?.Kind == LlmPresetKind.None;

    /// <summary>URL/model fields are pointless without a model.</summary>
    public bool ShowFields => !IsNone;

    /// <summary>The catch-all card needs the URL from every user, not only experts.</summary>
    public bool UrlAlwaysVisible => _selectedProvider?.Kind == LlmPresetKind.Other;

    /// <summary>"No key needed — the model runs on your machine."</summary>
    public bool ShowLocalNote => _selectedProvider?.Kind == LlmPresetKind.Local;

    /// <summary>"Without a model, runs answer as an echo."</summary>
    public bool ShowNoneNote => IsNone;

    /// <summary>The probe row makes no sense for the echo fallback.</summary>
    public bool ShowTestRow => _selectedProvider is not null && !IsNone;

    /// <summary>True when a key is already in place under the profile's variable.</summary>
    public bool HasStoredKey => _keyStore.Peek(ApiKeyEnvName) is not null;

    /// <summary>Status chip of the key block: remembered, or not detected yet.</summary>
    public string KeyStatusText =>
        _strings[HasStoredKey ? StudioStringKeys.ProfileKeyStatusSet : StudioStringKeys.ProfileKeyStatusMissing];

    /// <summary>"API key {provider}" — or the generic service wording for the catch-all.</summary>
    public string KeyBlockTitle =>
        _selectedProvider?.Kind == LlmPresetKind.Other
            ? _strings[StudioStringKeys.ProfileKeyTitleService]
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.ProfileKeyTitleFor],
                _selectedProvider?.Title ?? "");

    /// <summary>Where to get a key, when the vendor has a console we can name.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Display text: a bare host/path shown as a hint, or a localized " +
                        "'on the provider's site' fallback — not a navigable Uri.")]
    public string KeyConsoleUrl =>
        _selectedProvider?.KeyConsoleUrl ?? _strings[StudioStringKeys.ProfileKeyOnVendorSite];

    /// <summary>
    /// A profile needs a name of its own; the catch-all additionally needs the endpoint
    /// and the model typed in.
    /// </summary>
    public bool CanSave =>
        _name.Trim().Length > 0
        && !NameCollision
        && !(UrlAlwaysVisible
             && (string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_model)))
        // A typed tuning value that does not parse must block the save, not vanish silently.
        && (_temperatureText.Trim().Length == 0 || ParsedTemperature is not null)
        && (_timeoutText.Trim().Length == 0 || ParsedTimeoutSeconds is not null)
        && (_maxTokensText.Trim().Length == 0 || ParsedMaxTokens is not null);

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

    /// <summary>The remember-the-key action — stores the draft under the profile's variable, now.</summary>
    public RelayCommand StoreKeyCommand { get; }

    private void StoreKey()
    {
        if (_apiKeyInput.Trim() is not { Length: > 0 } pastedKey)
            return;

        // The key goes into the user environment under the profile's variable —
        // never into the profile store nor any settings file.
        _keyStore.Save(ApiKeyEnvName, pastedKey);
        ApiKeyInput = "";
        ConnectionTestResult = null;
        OnPropertyChanged(nameof(HasStoredKey));
        OnPropertyChanged(nameof(KeyStatusText));
    }

    /// <summary>
    /// The pinned temperature as typed — empty for "let the engine decide". Tolerant of
    /// both decimal separators; an unparseable text simply pins nothing. Some vendors
    /// mandate a value per model (Kimi K3 accepts only 1): pinning it here makes the
    /// first request right, instead of paying the provider's adaptive retry every call.
    /// </summary>
    public string TemperatureText
    {
        get => _temperatureText;
        set
        {
            if (SetProperty(ref _temperatureText, value ?? ""))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// The pinned HTTP timeout in seconds, as typed — empty for the engine's default (30 s).
    /// Reasoning models (Kimi K3, thinking modes) need more than the default to answer.
    /// </summary>
    public string TimeoutText
    {
        get => _timeoutText;
        set
        {
            if (SetProperty(ref _timeoutText, value ?? ""))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// What the timeout field means for the picked provider (LLM-11): the engine's 30 s when
    /// empty, or the pre-filled recommendation and why — the provider's default model thinks
    /// before it answers, and a run that hits the timeout gets no answer at all.
    /// </summary>
    public string TimeoutHint =>
        _selectedProvider?.RecommendedTimeoutSeconds is { } recommended
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ProfileTimeoutHintReasoning],
                recommended.ToString(CultureInfo.CurrentCulture), _selectedProvider.Title)
            : _strings[StudioStringKeys.ProfileTimeoutHint];

    /// <summary>
    /// Pre-fills the timeout the picked provider recommends, and only over a field the user
    /// has not made theirs: empty, or still holding the previous card's own recommendation. A
    /// value the user typed stays; a recommendation leaves with the card that brought it.
    /// </summary>
    private void SeedTimeoutFor(LlmPresetInfo picked, LlmPresetInfo? previous)
    {
        var typed = _timeoutText.Trim();
        var previousSeed = previous?.RecommendedTimeoutSeconds?.ToString(CultureInfo.InvariantCulture);
        var untouched = typed.Length == 0 || string.Equals(typed, previousSeed, StringComparison.Ordinal);
        if (!untouched)
            return;

        TimeoutText = picked.RecommendedTimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>The three positions of the thinking switch: provider default, on, off.</summary>
    public IReadOnlyList<ThinkingChoice> ThinkingChoices { get; }

    /// <summary>
    /// The thinking switch this profile pins (LLM-11). Provider default lets the model decide
    /// — on for Kimi K2.6, DeepSeek V4 and GLM; off is the way out of a timeout when the task
    /// does not need the reasoning pass. Travels as <c>ORKEON_Llm__Thinking__Enabled</c>.
    /// </summary>
    public ThinkingChoice SelectedThinking
    {
        get => ThinkingChoices.First(c => c.Value == _thinkingEnabled);
        set
        {
            if (value is null || value.Value == _thinkingEnabled)
                return;

            _thinkingEnabled = value.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>What the switch does, under the field.</summary>
    public string ThinkingHint => _strings[StudioStringKeys.ProfileThinkingHint];

    /// <summary>The reasoning-effort hint as typed — empty for the provider's default.</summary>
    public string ThinkingEffortText
    {
        get => _thinkingEffortText;
        set => SetProperty(ref _thinkingEffortText, value ?? "");
    }

    /// <summary>The typed timeout, or null when empty, unparseable, or non-positive.</summary>
    public int? ParsedTimeoutSeconds =>
        int.TryParse(_timeoutText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;

    /// <summary>
    /// The pinned maximum response length in tokens, as typed — empty leaves the cap to the
    /// engine, which sends the model's documented maximum (LLM-10); <see cref="MaxTokensHint"/>
    /// says what that is for this model. Before LLM-10 the engine sent 4096 for every model, a
    /// budget a reasoning model spends thinking before it writes a word (STUDIO-12 C5b).
    /// </summary>
    public string MaxTokensText
    {
        get => _maxTokensText;
        set
        {
            if (SetProperty(ref _maxTokensText, value ?? ""))
                SaveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// What an empty response-budget field means for this profile (LLM-10): the model's
    /// documented maximum when the catalogue knows it, no cap at all for a vendor that
    /// documents none or for a local runtime, and the 4096 fallback — with the invitation to
    /// pin — for a model the catalogue does not know. Provider-aware: an entry can hold on one
    /// endpoint only (Together clamps to its window; HuggingFace's router does not).
    /// </summary>
    public string MaxTokensHint
    {
        get
        {
            var providerKey = _selectedProvider?.Name;
            if (string.Equals(providerKey, LlmProviderKeys.Ollama, StringComparison.OrdinalIgnoreCase))
                return _strings[StudioStringKeys.ProfileMaxTokensHintLocal];

            return LlmModelOutputLimits.MaxOutputTokens(_model, providerKey) switch
            {
                null => _strings[StudioStringKeys.ProfileMaxTokensHintUnknown],
                LlmModelOutputLimits.Unbounded => _strings[StudioStringKeys.ProfileMaxTokensHintUnbounded],
                { } documented => string.Format(
                    CultureInfo.CurrentCulture,
                    _strings[StudioStringKeys.ProfileMaxTokensHintKnown],
                    documented.ToString("N0", CultureInfo.CurrentCulture)),
            };
        }
    }

    /// <summary>The typed token budget, or null when empty, unparseable, or non-positive.</summary>
    public int? ParsedMaxTokens =>
        int.TryParse(_maxTokensText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;

    /// <summary>The typed temperature, or null when empty or unparseable.</summary>
    public double? ParsedTemperature =>
        double.TryParse(_temperatureText.Trim().Replace(',', '.'),
            System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private void Save()
    {
        if (!CanSave)
            return;

        if (RequiresApiKey && _apiKeyInput.Trim().Length > 0)
            StoreKey(); // a pasted-but-not-yet-remembered key must not be lost on save

        _owner.CommitEdit(new ModelProfile
        {
            Name = _name.Trim(),
            Provider = _selectedProvider?.Title,
            Model = _model,
            BaseUrl = _baseUrl,
            Temperature = ParsedTemperature,
            TimeoutSeconds = ParsedTimeoutSeconds,
            MaxTokens = ParsedMaxTokens,
            ThinkingEnabled = _thinkingEnabled,
            ThinkingEffort = string.IsNullOrWhiteSpace(_thinkingEffortText) ? null : _thinkingEffortText.Trim(),
            KeyEnvName = RequiresApiKey ? ApiKeyEnvName : null,
        }, PreviousName);
    }

    /// <summary>Probes the endpoint; public so tests can await it with a token.</summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyInput.Trim() is { Length: > 0 } typed
            ? typed
            : _keyStore.Peek(ApiKeyEnvName) ?? LlmApiKeyResolver.Resolve(null);

        if (RequiresApiKey && apiKey is null)
        {
            // The design refuses to probe into a guaranteed 401: name the missing step.
            ConnectionTestResult = _strings[StudioStringKeys.ProfileKeyMissingTest];
            return;
        }

        var result = await _probe.ProbeAsync(
            new LlmProbeRequest { BaseUrl = BaseUrl, ApiKey = apiKey },
            cancellationToken).ConfigureAwait(true);

        ConnectionTestResult = result.Message;
    }
}

/// <summary>
/// The model-settings tab: the named, reusable model settings of this machine, the default
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
    private readonly IApiKeyStore _keyStore;
    private ModelProfileSet _set = ModelProfileSet.Empty;
    private readonly Func<IReadOnlyList<TeamSummary>>? _loadTeams;
    private ModelProfileEditorViewModel? _editor;
    private string? _loadError;

    /// <summary>Builds the tab over its seams.</summary>
    public ModelProfilesViewModel(
        IModelProfileStore? store,
        LlmSectionViewModel llm,
        IStudioStrings? strings = null,
        ILlmEndpointProbe? probe = null,
        IApiKeyStore? keyStore = null,
        Func<IReadOnlyList<TeamSummary>>? loadTeams = null)
    {
        ArgumentNullException.ThrowIfNull(llm);

        _loadTeams = loadTeams;

        _store = store ?? new InMemoryModelProfileStore();
        _llm = llm;
        _strings = strings ?? EnglishStudioStrings.Instance;
        // Lives as long as the tab, which lives as long as the window.
        _probe = probe ?? HttpLlmEndpointProbe.ForCurrentMachine();
        _keyStore = keyStore ?? new EnvironmentApiKeyStore();
        NewProfileCommand = new RelayCommand(BeginCreate);
    }

    /// <summary>The profile cards, rebuilt after every mutation.</summary>
    public ObservableCollection<ModelProfileItemViewModel> Profiles { get; } = [];

    /// <summary>The profile names, for the assistant picker.</summary>
    public ObservableCollection<string> ProfileNames { get; } = [];

    /// <summary>
    /// The settings screen's API-keys rows (one per distinct environment-variable name
    /// the profiles resolve), rebuilt with the profile list.
    /// </summary>
    public ObservableCollection<SecretRowViewModel> Secrets { get; } = [];

    /// <summary>Whether the secrets card shows at all.</summary>
    public bool HasSecrets => Secrets.Count > 0;

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

    /// <summary>
    /// Why the profile file could not be read, when it exists and could not be; null after a
    /// clean load. A file that fails to parse used to load as the empty set — the same screen
    /// as a first run, over a file the user had hand-written (STUDIO-12 C6). The next change
    /// overwrites that file, so the line says so.
    /// </summary>
    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
                OnPropertyChanged(nameof(HasLoadError));
        }
    }

    /// <summary>Whether the unreadable-file line shows.</summary>
    public bool HasLoadError => _loadError is not null;

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
        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);
        _set = loaded.Set;
        LoadError = loaded.Error is { } error
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ProfileFileUnreadable], error)
            : null;
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
        Editor = new ModelProfileEditorViewModel(this, ProviderCatalog(), profile, profile.Name, _probe, _keyStore, _strings);

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
            _probe,
            _keyStore,
            _strings);
    }

    private IReadOnlyList<LlmPresetInfo> ProviderCatalog() => LlmPresets.ProviderCatalogFor(_strings);

    private Task _persist = Task.CompletedTask;

    private void Mutate(ModelProfileSet set)
    {
        _set = set;
        Rebuild();
        // Writes are chained so two rapid mutations can never interleave on the file; the
        // store itself is tolerant (a refused write is a lost convenience, said nowhere by
        // design — the profile set lives on in memory for the session).
        _persist = Persist(_persist, set);
    }

    private async Task Persist(Task previous, ModelProfileSet set)
    {
        await previous.ConfigureAwait(false);
        await _store.SaveAsync(set).ConfigureAwait(false);
    }

    private void ApplyDefaultToSettings()
    {
        if (_set.Default is not { } profile)
            return;

        // Mirrored into the Llm section so that a bare orkeon run follows the same election.
        // The write lands in the settings document and travels through the screen's own edit
        // then save cycle: Studio never saves the settings file behind the user's back.
        _llm.Model = profile.Model;
        _llm.BaseUrl = profile.BaseUrl;
    }

    private void Rebuild()
    {
        var usage = TeamsByProfile();

        Profiles.Clear();
        foreach (var profile in _set.Profiles)
        {
            Profiles.Add(new ModelProfileItemViewModel(
                profile,
                isDefault: string.Equals(profile.Name, _set.DefaultProfile, StringComparison.Ordinal),
                isStudio: string.Equals(profile.Name, _set.StudioProfile, StringComparison.Ordinal),
                this,
                usage.TryGetValue(profile.Name, out var usedBy) ? usedBy : []));
        }

        RebuildProfileNames();
        RebuildSecrets();

        OnPropertyChanged(nameof(HasSecrets));

        OnPropertiesChanged(
            nameof(StudioProfileName), nameof(HasStudioProfile),
            nameof(DefaultProfileName), nameof(IsEmpty), nameof(CanDelete), nameof(Set));
    }

    /// <summary>
    /// The "used by" chips of the audit of 07/16: which adopted teams name each profile in
    /// their sidecar. Best-effort — an unreadable teams root simply yields no chips.
    /// </summary>
    private Dictionary<string, List<string>> TeamsByProfile()
    {
        var usage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (_loadTeams is null)
            return usage;

        foreach (var team in _loadTeams())
        {
            if (team.Profile is not { Length: > 0 } profileName)
                continue;

            if (!usage.TryGetValue(profileName, out var teams))
                usage[profileName] = teams = [];

            teams.Add(team.Name);
        }

        return usage;
    }

    /// <summary>
    /// The names feed ComboBoxes with a TwoWay SelectedItem (the assistant picker, the
    /// wizard's adopt step). Electing a profile changes no name, and clearing the list
    /// mid-write makes WPF null the selection and swallow the correcting PropertyChanged
    /// (re-entrancy guard) — the election then LOOKS unsaved. So the list is only touched
    /// when the names actually changed.
    /// </summary>
    private void RebuildProfileNames()
    {
        if (ProfileNames.SequenceEqual(_set.Profiles.Select(p => p.Name), StringComparer.Ordinal))
            return;

        ProfileNames.Clear();
        foreach (var profile in _set.Profiles)
            ProfileNames.Add(profile.Name);
    }

    /// <summary>
    /// The secrets card: one row per distinct key variable the profiles name. The value is
    /// peeked from the environment, never read from any file — there is nothing to read.
    /// </summary>
    private void RebuildSecrets()
    {
        Secrets.Clear();
        foreach (var group in _set.Profiles
                     .Where(p => p.KeyEnvName is { Length: > 0 })
                     .GroupBy(p => p.KeyEnvName!, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Secrets.Add(new SecretRowViewModel(
                group.Key,
                string.Join(", ", group.Select(p => p.Name)),
                _keyStore,
                _strings));
        }
    }
}
