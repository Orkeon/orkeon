using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using System.Globalization;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// Payload of <see cref="ModelProfilesViewModel.HostProfilesChanged"/> (STUDIO-50): the setting
/// names whose host profile the change moved (<see cref="HostLlmProfiles.MovedNames"/>).
/// </summary>
public sealed class HostProfilesChangedEventArgs(IReadOnlySet<string> names) : EventArgs
{
    /// <summary>The names, as a team's companion file holds them.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public IReadOnlySet<string> Names { get; } = names;
}

/// <summary>
/// One profile card on the model-settings tab. A thin projection over
/// <see cref="ModelProfile"/>; the commands are handed in by the list so every mutation goes
/// through one place.
/// </summary>
public sealed class ModelProfileItemViewModel : ObservableObject
{
    private readonly BalanceReadings? _balances;
    private readonly IStudioStrings _strings;
    private readonly HostProfileCheck? _host;

    internal ModelProfileItemViewModel(
        ModelProfile profile,
        bool isDefault,
        bool isStudio,
        ModelProfilesViewModel owner,
        IReadOnlyList<string>? usedByTeams = null,
        BalanceReadings? balances = null,
        IStudioStrings? strings = null,
        HostProfileCheck? host = null)
    {
        Profile = profile;
        IsDefault = isDefault;
        IsStudio = isStudio;
        UsedByTeams = usedByTeams ?? [];
        _balances = balances;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _host = host;
        SetDefaultCommand = new RelayCommand(() => owner.SetDefault(profile.Name));
        EditCommand = new RelayCommand(() => owner.BeginEdit(profile));
        DuplicateCommand = new RelayCommand(() => owner.Duplicate(profile));
        DeleteCommand = new RelayCommand(() => owner.Delete(profile.Name), () => owner.CanDelete);
    }

    /// <summary>The profile being shown.</summary>
    public ModelProfile Profile { get; }

    /// <summary>Display name.</summary>
    public string Name => Profile.Name;

    /// <summary>"Provider · model" one-liner — the card's title in the interface's language (STUDIO-54).</summary>
    public string Summary => Profile.Summary(_strings);

    /// <summary>Endpoint, shown in expert mode only.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Presentation of the profile's endpoint text, shown verbatim in a mono label.")]
    public string? Endpoint => Profile.BaseUrl;

    /// <summary>
    /// The title of the setting's card for the non-default badge, in the interface's language: the
    /// setting holds the card's name, never a title (STUDIO-54).
    /// </summary>
    public string Provider => LlmPresets.TitleFor(LlmPresets.CardOf(Profile), _strings);

    /// <summary>True for the elected default.</summary>
    public bool IsDefault { get; }

    /// <summary>True for the profile Studio's assistant uses.</summary>
    public bool IsStudio { get; }

    /// <summary>The adopted teams whose sidecar names this profile (audit 07/16 chips).</summary>
    public IReadOnlyList<string> UsedByTeams { get; }

    /// <summary>Whether the "used by" row shows.</summary>
    public bool IsUsedByTeams => UsedByTeams.Count > 0;

    /// <summary>
    /// The host profile this setting is in the settings file — what a crew writes,
    /// <c>profile: &lt;id&gt;</c> (STUDIO-48); null when no crew can name it.
    /// </summary>
    public string? HostProfileId => _host is { IsOffered: true } host ? host.Id : null;

    /// <summary>The line under the name: the name a crew writes, or why no crew can write one; null for a setting without a model.</summary>
    public string? HostIdText => HostProfileText.Describe(_host, _strings);

    /// <summary>Whether the line shows.</summary>
    public bool HasHostIdText => HostIdText is not null;

    /// <summary>Whether the line is a problem — no crew can name this setting — shown in the warning tone.</summary>
    public bool IsHostIdIssue => HostProfileText.IsIssue(_host);

    /// <summary>
    /// The expert line of a setting that keeps its key in <c>ORKEON_Llm__ApiKey</c> without being
    /// the default (STUDIO-49, decision 7): that variable is the runtime's own key of the default,
    /// so every run of the user reads this key as its default's, whatever the endpoint — and how to
    /// take it back. The « Compatible OpenAI » card used that variable before; a setting created
    /// since keeps its key in <see cref="LlmPresets.CustomApiKeyEnv"/>. Null otherwise.
    /// </summary>
    public string? DefaultKeyVariableWarning =>
        !IsDefault && string.Equals(Profile.KeyEnvName?.Trim(), LlmPresets.DefaultApiKeyEnv, StringComparison.OrdinalIgnoreCase)
            ? string.Format(
                CultureInfo.CurrentCulture,
                _strings[StudioStringKeys.ProfileDefaultKeyVariableWarning],
                LlmPresets.DefaultApiKeyEnv,
                LlmPresets.CustomApiKeyEnv)
            : null;

    /// <summary>Whether that line shows (in expert mode).</summary>
    public bool HasDefaultKeyVariableWarning => DefaultKeyVariableWarning is not null;

    /// <summary>
    /// What the profile's account has left, when a read of this session told it (STUDIO-35
    /// D-04): the amounts as the provider returned them; null when nothing was read, or the
    /// provider does not tell.
    /// </summary>
    public string? Balance => Reading is { Status: ProviderBalanceStatus.Available } reading
        ? BalanceText.Amounts(reading)
        : null;

    /// <summary>Whether the balance chip shows.</summary>
    public bool HasBalance => Balance is not null;

    /// <summary>Whether the balance is under its provider's alert threshold — the chip's warning tone (D-03).</summary>
    public bool IsBalanceLow => Reading is { } reading && _balances is not null && _balances.IsUnderThreshold(reading);

    /// <summary>The chip on hover: what the amount is, and when it was read.</summary>
    public string? BalanceTip => Reading is { } reading && _balances is not null
        ? BalanceText.Summary(reading, _balances, _strings)
        : null;

    /// <summary>Elects this profile as the machine default.</summary>
    public RelayCommand SetDefaultCommand { get; }

    /// <summary>Opens the editor over this profile.</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>Adds a copy of this profile.</summary>
    public RelayCommand DuplicateCommand { get; }

    /// <summary>Removes this profile (disabled on the last one).</summary>
    public RelayCommand DeleteCommand { get; }

    private ProviderBalanceResult? Reading =>
        _balances is not null && ProviderBalanceAccount.HasAccount(Profile.BaseUrl)
            ? _balances.Of(ProviderBalanceAccount.For(Profile))
            : null;

    /// <summary>A read landed, or a threshold moved: the chip says it again.</summary>
    internal void RefreshBalance() =>
        OnPropertiesChanged(nameof(Balance), nameof(HasBalance), nameof(IsBalanceLow), nameof(BalanceTip));

    /// <summary>The language switched: the card's title, the host profile line and the key warning say it again.</summary>
    internal void RefreshTexts() =>
        OnPropertiesChanged(
            nameof(Summary), nameof(Provider), nameof(HostIdText), nameof(HasHostIdText), nameof(DefaultKeyVariableWarning));
}

/// <summary>
/// An entry of <c>Llm:Profiles</c> no setting owns — written by hand in the settings file
/// (STUDIO-48). Read-only by design: a crew can name it, Studio shows it and never changes it,
/// and the row offers no gesture.
/// </summary>
public sealed class HandWrittenProfileViewModel
{
    internal HandWrittenProfileViewModel(LlmProfileEntry entry, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(strings);

        Id = entry.Id;
        Summary = entry.Summary;
        HostIdText = HostProfileText.Describe(new HostProfileCheck(HostProfileStatus.Offered, entry.Id), strings)!;
    }

    /// <summary>The entry's key — the name a crew writes.</summary>
    public string Id { get; }

    /// <summary>The model and the endpoint the entry names.</summary>
    public string Summary { get; }

    /// <summary>Whether the summary line shows.</summary>
    public bool HasSummary => Summary.Length > 0;

    /// <summary>"In a crew file: profile: &lt;id&gt;".</summary>
    public string HostIdText { get; }
}

/// <summary>
/// One choice of the RAG's model picker (<c>Orkeon:Rag:LlmProfile</c>, STUDIO-48): a host profile,
/// or the default — <paramref name="Id"/> null.
/// </summary>
/// <param name="Id">The profile the RAG calls; null for the default profile.</param>
/// <param name="Label">What the picker shows.</param>
public sealed record RagProfileChoice(string? Id, string Label);

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
    private readonly BalanceReadings? _balances;
    private readonly IShellOpener? _opener;
    private LlmPresetInfo? _selectedProvider;
    private ProviderBalanceResult? _balanceReading;
    private string? _balanceResult;
    private string _name;
    private string? _baseUrl;
    private string? _model;
    private string? _apiKeyEnv;
    private string _apiKeyInput = "";
    private string? _connectionTestResult;
    private string? _keyStoreError;
    private bool _isTestingConnection;
    private int _settingsGeneration;
    private string _temperatureText = "";
    private string _timeoutText = "";
    private string _maxTokensText = "";
    private bool? _thinkingEnabled;
    private string _thinkingEffortText = "";
    private readonly string? _originalHostId;

    internal ModelProfileEditorViewModel(
        ModelProfilesViewModel owner,
        IReadOnlyList<LlmPresetInfo> providers,
        ModelProfile profile,
        string? previousName,
        ILlmEndpointProbe probe,
        IApiKeyStore keyStore,
        IStudioStrings strings,
        BalanceReadings? balances = null,
        IShellOpener? opener = null)
    {
        _owner = owner;
        _probe = probe;
        _keyStore = keyStore;
        _strings = strings;
        _balances = balances;
        _opener = opener;
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
        // STUDIO-54: the card by its name — a title changes with the language —, recognised from
        // whatever an older setting holds; a setting always has one.
        var card = LlmPresets.CardOf(profile);
        _selectedProvider = providers.FirstOrDefault(p => string.Equals(p.Name, card, StringComparison.Ordinal));
        // The id crews may already write: the one this setting was offered under when it opened.
        _originalHostId = previousName is null ? null : owner.OfferedHostId(previousName);

        SaveCommand = new RelayCommand(Save, () => CanSave);
        CancelCommand = new RelayCommand(() => _owner.CancelEdit());
        SelectProviderCommand = new RelayCommand(p => SelectedProvider = p as LlmPresetInfo);
        TestConnectionCommand = new AsyncRelayCommand(
            () => TestConnectionAsync(CancellationToken.None), () => !_isTestingConnection);
        StoreKeyCommand = new AsyncRelayCommand(StoreKeyAsync, () => _apiKeyInput.Trim().Length > 0);
        ReadBalanceCommand = new AsyncRelayCommand(() => ReadBalanceAsync(CancellationToken.None));
        OpenBalanceConsoleCommand = new RelayCommand(
            () => _opener?.Open(_balanceReading!.ConsoleUrl!.AbsoluteUri),
            () => BalanceOffersConsole && _opener is not null);

        // The line opens on what this session already read for the profile's account, if anything.
        if (balances is not null && ProviderBalanceAccount.HasAccount(profile.BaseUrl))
            ShowBalance(balances.Of(ProviderBalanceAccount.For(profile)));
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
                OnHostProfileChanged();
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
            ShowBalance(null);
            OnPropertyChanged(nameof(RequiresApiKey));
            OnPropertyChanged(nameof(ApiKeyEnvName));
            OnPropertyChanged(nameof(HostKeyHint));     // the card's own variable (STUDIO-49)
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
            OnPropertyChanged(nameof(ShowBalanceRow));
            OnPropertyChanged(nameof(MaxTokensHint));   // an entry can hold on one provider only (LLM-10)
            OnHostProfileChanged();                     // « no model » is offered to no crew
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
            if (!SetProperty(ref _baseUrl, value))
                return;

            OnHostProfileChanged();
            SaveCommand.RaiseCanExecuteChanged();
            ClearConnectionTestResult();   // the verdict was about the previous endpoint
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

            OnHostProfileChanged();
            OnPropertyChanged(nameof(MaxTokensHint));   // the hint follows the model, not the provider
            SaveCommand.RaiseCanExecuteChanged();
            ClearConnectionTestResult();   // the test completion ran on the previous model
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

    /// <summary>
    /// The balance line beside « Test » (STUDIO-35 D-04): for a card with a key, hence an
    /// account to ask, and only when a balance probe is wired.
    /// </summary>
    public bool ShowBalanceRow => ShowTestRow && RequiresApiKey && _balances is { CanRead: true };

    /// <summary>What the last balance read said for this endpoint — never the key; null before any.</summary>
    public string? BalanceResult
    {
        get => _balanceResult;
        private set => SetProperty(ref _balanceResult, value);
    }

    /// <summary>The probe's own line on hover, in English: it is the raw evidence (STUDIO-33), like the cause quoted in the connection test's line.</summary>
    public string? BalanceDetail => _balanceReading?.Detail;

    /// <summary>Whether the balance read is under its provider's alert threshold (D-03).</summary>
    public bool IsBalanceLow => _balanceReading is { } reading && _balances is not null && _balances.IsUnderThreshold(reading);

    /// <summary>Whether the provider shows the balance in its console only — the line then offers to open it (D-01).</summary>
    public bool BalanceOffersConsole => _balanceReading is { } reading && BalanceText.OffersConsole(reading);

    /// <summary>True when a key is already in place under the profile's variable.</summary>
    public bool HasStoredKey => _keyStore.Peek(ApiKeyEnvName) is not null;

    /// <summary>
    /// Why the last remembered key was not kept for the next sessions (STUDIO-44): the key is in
    /// place for this one all the same. Null when the last write went through.
    /// </summary>
    public string? KeyStoreError
    {
        get => _keyStoreError;
        private set => SetProperty(ref _keyStoreError, value);
    }

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
        // STUDIO-48: « default » is the Llm section's own name, and an id another setting or a
        // hand-written entry holds would make two profiles one.
        && !HostProfile.BlocksSave
        && !(UrlAlwaysVisible
             && (string.IsNullOrWhiteSpace(_baseUrl) || string.IsNullOrWhiteSpace(_model)))
        // A typed tuning value that does not parse must block the save, not vanish silently.
        && (_temperatureText.Trim().Length == 0 || ParsedTemperature is not null)
        && (_timeoutText.Trim().Length == 0 || ParsedTimeoutSeconds is not null)
        && (_maxTokensText.Trim().Length == 0 || ParsedMaxTokens is not null);

    /// <summary>True while the typed name already belongs to another profile.</summary>
    public bool NameCollision => _owner.IsNameTaken(_name.Trim(), PreviousName);

    /// <summary>
    /// What the typed name would make of the setting as a host profile (STUDIO-48): the id a crew
    /// writes, or why no crew could write one — against the other settings and the entries the
    /// settings file holds by hand.
    /// </summary>
    public HostProfileCheck HostProfile =>
        _owner.CheckHostProfile(_name, PreviousName, !string.IsNullOrWhiteSpace(_model) || !string.IsNullOrWhiteSpace(_baseUrl));

    /// <summary>
    /// The line under the name: <c>profile: &lt;id&gt;</c> as typed, or why it cannot be; null for a
    /// setting without a model, and while the name itself is taken (that line says it).
    /// </summary>
    public string? HostIdText => NameCollision ? null : HostProfileText.Describe(HostProfile, _strings);

    /// <summary>Whether that line is a problem rather than the name to write.</summary>
    public bool IsHostIdIssue => !NameCollision && HostProfileText.IsIssue(HostProfile);

    /// <summary>
    /// Said when a setting crews could name is renamed under another id: the crews that write the
    /// old one stop loading. Null otherwise.
    /// </summary>
    public string? HostIdRenamedText =>
        _originalHostId is { } previous
        && !string.Equals(HostProfile.Id, previous, StringComparison.OrdinalIgnoreCase)
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ProfileHostIdRenamed], previous)
            : null;

    /// <summary>
    /// The expert line of the key block: the variable a run outside Studio — a terminal, a
    /// scheduled team — reads this setting's key from: the one Studio remembers it in, which the
    /// settings file names (<c>ApiKeyEnvVar</c>, STUDIO-49) — never the key. Null when no crew can
    /// name the setting, or when it needs no key.
    /// </summary>
    public string? HostKeyHint =>
        RequiresApiKey && HostProfile is { IsOffered: true }
            ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.ProfileHostKeyHint], ApiKeyEnvName)
            : null;

    private void OnHostProfileChanged() =>
        OnPropertiesChanged(
            nameof(HostProfile), nameof(HostIdText), nameof(IsHostIdIssue), nameof(HostIdRenamedText), nameof(HostKeyHint),
            nameof(CanSave));

    /// <summary>
    /// Outcome line of the last connection probe, in the interface's language: the step that
    /// failed, the URL, the time waited and the cause (STUDIO-43). Cleared whenever the endpoint,
    /// the model or the thinking switch changes, since the verdict was about the previous ones.
    /// </summary>
    public string? ConnectionTestResult
    {
        get => _connectionTestResult;
        private set => SetProperty(ref _connectionTestResult, value);
    }

    /// <summary>True while a connection test runs: the button is disabled and the line says so.</summary>
    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            if (SetProperty(ref _isTestingConnection, value))
                TestConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Forgets the shown verdict, and any verdict still in flight for the old settings.</summary>
    private void ClearConnectionTestResult()
    {
        _settingsGeneration++;
        ConnectionTestResult = null;
    }

    /// <summary>Commits the profile to the set.</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>Closes the editor without touching the set.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Picks the provider row carried as the command parameter.</summary>
    public RelayCommand SelectProviderCommand { get; }

    /// <summary>Probes the endpoint with the setting's own key — never the default's (GAP-36).</summary>
    public AsyncRelayCommand TestConnectionCommand { get; }

    /// <summary>The remember-the-key action — stores the draft under the profile's variable, now.</summary>
    public AsyncRelayCommand StoreKeyCommand { get; }

    /// <summary>Reads the balance of the account behind the endpoint and the key (STUDIO-35 D-04).</summary>
    public AsyncRelayCommand ReadBalanceCommand { get; }

    /// <summary>Opens the vendor's console, where a balance no inference key reads is shown.</summary>
    public RelayCommand OpenBalanceConsoleCommand { get; }

    /// <summary>
    /// Remembers the pasted key under the profile's variable (STUDIO-44): in place for the
    /// session as soon as this starts, kept for the next sessions off the interface thread, and
    /// a failure of that second half shown in the editor. Public so tests can await it.
    /// </summary>
    public Task StoreKeyAsync() => StoreKeyCoreAsync(editorClosing: false);

    [SuppressMessage("Design", "CA1031",
        Justification = "Whatever the user scope throws (access denied, a failed broadcast), the key is " +
                        "already in place for the session: the reason belongs on a line of the screen, " +
                        "not in the command's fault handler.")]
    private async Task StoreKeyCoreAsync(bool editorClosing)
    {
        if (_apiKeyInput.Trim() is not { Length: > 0 } pastedKey)
            return;

        // The key goes into the environment under the profile's variable — never into the
        // profile store nor any settings file. The process half is written before this returns.
        var persisted = _keyStore.SaveAsync(ApiKeyEnvName, pastedKey);
        ApiKeyInput = "";
        ConnectionTestResult = null;
        KeyStoreError = null;
        OnPropertyChanged(nameof(HasStoredKey));
        OnPropertyChanged(nameof(KeyStatusText));

        try
        {
            await persisted.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            var message = string.Format(
                CultureInfo.CurrentCulture, _strings[StudioStringKeys.ProfileKeyPersistFailed], ex.Message);
            if (editorClosing || !ReferenceEquals(_owner.Editor, this))
                _owner.ReportKeyStoreError(message);
            else
                KeyStoreError = message;
        }

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
            // STUDIO-43: the verdict shown was earned under the previous switch — the test
            // completion carries it — so it must not stay under the new one.
            ClearConnectionTestResult();
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
            // A pasted-but-not-yet-remembered key must not be lost on save; the editor closes,
            // so a failure to keep it for the next sessions is said on the profile list.
            _ = StoreKeyCoreAsync(editorClosing: true);

        _owner.CommitEdit(new ModelProfile
        {
            Name = _name.Trim(),
            Provider = _selectedProvider?.Name,
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

    /// <summary>
    /// Probes the endpoint, then runs a minimal completion on the profile's model with its
    /// thinking switch (STUDIO-43), under a deadline of 30 s or the profile's own when shorter —
    /// with the setting's key and it alone (GAP-36): a setting that needs one and has none is
    /// refused without a request. Public so tests can await it with a token.
    /// </summary>
    [SuppressMessage("Design", "CA1031",
        Justification = "The test is a convenience that must never fault the command: any unexpected " +
                        "failure is reported in the result line like every other unreachable endpoint.")]
    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (_isTestingConnection)
            return;

        var apiKey = SettingKey;
        if (RequiresApiKey && apiKey is null)
        {
            // The design refuses to probe into a guaranteed 401: name the missing step.
            ConnectionTestResult = _strings[StudioStringKeys.ProfileKeyMissingTest];
            return;
        }

        var request = new LlmProbeRequest
        {
            BaseUrl = BaseUrl,
            ApiKey = apiKey,
            Model = _model,
            ThinkingEnabled = _thinkingEnabled,
            CheckCompletion = true,
            Timeout = LlmProbeRequest.TimeoutFor(ParsedTimeoutSeconds),
        };

        IsTestingConnection = true;
        ConnectionTestResult = _strings[StudioStringKeys.LlmTesting];
        var generation = _settingsGeneration;
        LlmProbeResult result;
        try
        {
            result = await _probe.ProbeAsync(request, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsTestingConnection = false;
            ConnectionTestResult = null;
            throw;
        }
        catch (Exception ex)
        {
            result = LlmProbeResult.Unreachable(ex.Message);
        }

        IsTestingConnection = false;
        // A setting changed while the probe ran: its verdict is about settings no longer shown.
        if (generation == _settingsGeneration)
            ConnectionTestResult = LlmProbeText.Describe(result, _strings);
    }

    /// <summary>
    /// The key this setting presents to a probe (GAP-36): the one typed here, else the one
    /// remembered under its variable — and, for a setting that needs none, its card's placeholder,
    /// which a run presents too: Docker Model Runner's <c>not-needed</c>, nothing for Ollama
    /// (STUDIO-54) —, never a draft typed for another card. Never the runtime's own variable in place
    /// of a missing key: that is the default's key, which no run of another setting reads.
    /// </summary>
    private string? SettingKey =>
        !RequiresApiKey ? LlmPresets.PlaceholderKeyOf(_selectedProvider?.Name)
        : _apiKeyInput.Trim() is { Length: > 0 } typed ? typed
        : _keyStore.Peek(ApiKeyEnvName);

    /// <summary>
    /// Reads the balance of the endpoint being edited, with the setting's key and it alone — the
    /// one typed here first, then the remembered one (GAP-36) — and, like the connection test,
    /// refuses to ask without a key rather than earn a certain refusal. Public so tests can await it.
    /// </summary>
    public async Task ReadBalanceAsync(CancellationToken cancellationToken)
    {
        if (_balances is not { CanRead: true } balances)
            return;

        var target = new ProviderBalanceTarget(
            ProviderBalanceAccount.For(BaseUrl, RequiresApiKey ? ApiKeyEnvName : null), BaseUrl, [_name.Trim()]);
        var typed = RequiresApiKey ? _apiKeyInput.Trim() : "";
        if (RequiresApiKey && target.RequestWith(_keyStore, typed).ApiKey is null)
        {
            ShowBalance(null);
            BalanceResult = _strings[StudioStringKeys.ProfileKeyMissingTest];
            return;
        }

        ShowBalance(await balances.ReadEndpointAsync(target, typed.Length > 0 ? typed : null, cancellationToken).ConfigureAwait(true));
    }

    private void ShowBalance(ProviderBalanceResult? reading)
    {
        _balanceReading = reading;
        BalanceResult = reading is not null && _balances is not null
            ? BalanceText.Summary(reading, _balances, _strings)
            : null;
        OnPropertiesChanged(nameof(BalanceDetail), nameof(IsBalanceLow), nameof(BalanceOffersConsole));
        OpenBalanceConsoleCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// The model-settings tab: the named, reusable model settings of this machine, the default
/// election, and the profile Studio's own assistant runs on. Every mutation is persisted to
/// the store immediately (its file is Studio state, like the history); electing a default
/// additionally mirrors it — whole — into the settings document's <c>Llm</c> section, which is
/// what the CLI reads when it runs outside Studio, and every setting that names a provider is
/// mirrored into <c>Llm:Profiles:&lt;id&gt;</c> — the host profile a crew names with
/// <c>profile: &lt;id&gt;</c> (STUDIO-48). Neither carries a key: each names the variable that
/// holds it (<c>ApiKeyEnvVar</c>, STUDIO-49), which a run outside Studio reads. Both writes go through the ordinary dirty/save cycle of
/// the settings screen. The entries of <c>Llm:Profiles</c> no setting owns are listed read-only,
/// and the RAG's model (<c>Orkeon:Rag:LlmProfile</c>) is chosen among all of them.
/// </summary>
public sealed class ModelProfilesViewModel : ObservableObject
{
    private readonly IModelProfileStore _store;
    private readonly LlmSectionViewModel _llm;
    private readonly IStudioStrings _strings;
    private readonly ILlmEndpointProbe _probe;
    private readonly IApiKeyStore _keyStore;
    private readonly BalanceReadings? _balances;
    private readonly IShellOpener? _opener;
    private ModelProfileSet _set = ModelProfileSet.Empty;
    private readonly Func<IReadOnlyList<TeamSummary>>? _loadTeams;
    private ModelProfileEditorViewModel? _editor;
    private string? _loadError;
    private string? _keyStoreError;

    /// <summary>
    /// Builds the tab over its seams. <paramref name="balances"/> are the provider balances read
    /// this session (STUDIO-35): each row shows its account's, and the editor reads one; left
    /// out, neither shows. <paramref name="shellOpener"/> opens a vendor's console from the editor.
    /// </summary>
    public ModelProfilesViewModel(
        IModelProfileStore? store,
        LlmSectionViewModel llm,
        IStudioStrings? strings = null,
        ILlmEndpointProbe? probe = null,
        IApiKeyStore? keyStore = null,
        Func<IReadOnlyList<TeamSummary>>? loadTeams = null,
        BalanceReadings? balances = null,
        IShellOpener? shellOpener = null)
    {
        ArgumentNullException.ThrowIfNull(llm);

        _loadTeams = loadTeams;

        _store = store ?? new InMemoryModelProfileStore();
        _llm = llm;
        _strings = strings ?? EnglishStudioStrings.Instance;
        // Lives as long as the tab, which lives as long as the window.
        _probe = probe ?? HttpLlmEndpointProbe.ForCurrentMachine();
        _keyStore = keyStore ?? new EnvironmentApiKeyStore();
        _balances = balances;
        _opener = shellOpener;
        NewProfileCommand = new RelayCommand(BeginCreate);

        // A read landed, a threshold moved or the language switched: every row's chip says it again.
        if (balances is not null)
        {
            balances.Changed += (_, _) => RefreshBalances();
            _strings.CultureChanged += (_, _) => RefreshBalances();
        }

        // STUDIO-48: the host profile lines speak the interface's language, and a settings file
        // opened in place of the previous one brings its own entries and its own RAG choice.
        _strings.CultureChanged += (_, _) => RefreshHostTexts();
        _llm.PropertyChanged += OnSettingsDocumentChanged;
    }

    /// <summary>The profile cards, rebuilt after every mutation.</summary>
    public ObservableCollection<ModelProfileItemViewModel> Profiles { get; } = [];

    /// <summary>
    /// Raised after a change of the settings moved the host profile of some setting names — one
    /// created, removed, renamed, switched to « no model » (STUDIO-50): a team whose companion file
    /// names one of them runs elsewhere outside Studio, and the window writes its launchers again.
    /// </summary>
    public event EventHandler<HostProfilesChangedEventArgs>? HostProfilesChanged;

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

    /// <summary>
    /// The <c>ORKEON_Llm__*</c> overrides a launch under <paramref name="profile"/> lays over its
    /// child process — the key resolved through the key store (STUDIO-44), so a key held only in
    /// the user scope reaches the child as well as one in Studio's own environment — on top of
    /// <see cref="LaunchEnvironment"/>, every setting as the host profile a crew may name. A
    /// profile that runs in place of the elected default lays every field, blank when it leaves it
    /// unset (STUDIO-49, decision 4): nothing of the default — its key, the variable holding it —
    /// reaches another endpoint. The elected default itself lays what it sets: the section already
    /// is that setting, and a key the file holds there is its own.
    /// </summary>
    public IReadOnlyDictionary<string, string> LaunchEnvironmentOf(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var isDefault = string.Equals(profile.Name, _set.DefaultProfile, StringComparison.Ordinal);
        var environment = new Dictionary<string, string>(LaunchEnvironment(), StringComparer.Ordinal);
        foreach (var (key, value) in profile.EnvironmentOverrides(_keyStore.Peek))
        {
            if (!isDefault || value.Length > 0)
                environment[key] = value;
        }

        return environment;
    }

    /// <summary>
    /// What every launch lays over its child, whatever profile it runs on (STUDIO-48): each
    /// setting offered to crews as <c>ORKEON_Llm__Profiles__&lt;id&gt;__*</c>, its key resolved
    /// through the key store — so a crew that writes <c>profile: claude</c> runs on « Claude »,
    /// whether or not the settings file was saved since, and whichever file the launch reads.
    /// </summary>
    public IReadOnlyDictionary<string, string> LaunchEnvironment() =>
        HostLlmProfiles.LaunchEnvironment(_set, _keyStore.Peek);

    /// <summary>
    /// The entries of <c>Llm:Profiles</c> no setting owns — written by hand in the settings file,
    /// shown read-only (STUDIO-48): Studio never rewrites nor removes them.
    /// </summary>
    public ObservableCollection<HandWrittenProfileViewModel> HandWrittenProfiles { get; } = [];

    /// <summary>Whether the « written in the settings file » block shows.</summary>
    public bool HasHandWrittenProfiles => HandWrittenProfiles.Count > 0;

    /// <summary>
    /// The choices of the RAG's model (<c>Orkeon:Rag:LlmProfile</c>, GAP-19): the default, every
    /// setting offered to crews, every entry written by hand — and a name the file holds without
    /// defining it, so the picker shows what is there (the validation warns about it).
    /// </summary>
    public ObservableCollection<RagProfileChoice> RagProfileChoices { get; } = [];

    /// <summary>
    /// The profile the RAG calls. The default removes the key; a null selection — what a picker
    /// writes while its list is being rebuilt — changes nothing.
    /// </summary>
    public RagProfileChoice? SelectedRagProfile
    {
        get
        {
            var current = _llm.RagLlmProfile;
            return LlmProfilesSection.IsDefault(current)
                ? RagProfileChoices.FirstOrDefault(choice => choice.Id is null)
                : RagProfileChoices.FirstOrDefault(choice =>
                    string.Equals(choice.Id, current!.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (value is null)
                return;

            _llm.RagLlmProfile = value.Id;
        }
    }

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

    /// <summary>
    /// Why a key remembered by an editor that has closed since — a key pasted and saved with the
    /// profile — was not kept for the next sessions (STUDIO-44). It is in place for this one.
    /// Cleared when an editor opens.
    /// </summary>
    public string? KeyStoreError
    {
        get => _keyStoreError;
        private set
        {
            if (SetProperty(ref _keyStoreError, value))
                OnPropertyChanged(nameof(HasKeyStoreError));
        }
    }

    /// <summary>Whether the key-not-kept line shows on the profile list.</summary>
    public bool HasKeyStoreError => _keyStoreError is not null;

    internal void ReportKeyStoreError(string message) => KeyStoreError = message;

    /// <summary>Deleting is allowed only while more than one profile remains.</summary>
    public bool CanDelete => _set.Profiles.Count > 1;

    /// <summary>The editor overlay; null while closed.</summary>
    public ModelProfileEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (!SetProperty(ref _editor, value))
                return;

            OnPropertyChanged(nameof(IsEditorOpen));
            if (value is not null)
                KeyStoreError = null;
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
        Editor = new ModelProfileEditorViewModel(
            this, ProviderCatalog(), profile, profile.Name, _probe, _keyStore, _strings, _balances, _opener);

    internal void Duplicate(ModelProfile profile)
    {
        // A copy is a host profile of its own: its name must not give an id already answered to.
        var name = _set.CopyNameFor(
            profile.Name,
            _strings[StudioStringKeys.ProfileCopySuffix],
            candidate => CheckHostProfile(candidate, previousName: null, profile.DescribesProvider).BlocksSave);
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

        Mutate(_set.Upsert(profile, previousName), renamedFrom: previousName, renamedTo: profile.Name);
        Editor = null;

        if (wasDefault || string.Equals(_set.DefaultProfile, profile.Name, StringComparison.Ordinal))
            ApplyDefaultToSettings();
    }

    internal void CancelEdit() => Editor = null;

    private void RefreshBalances()
    {
        foreach (var row in Profiles)
            row.RefreshBalance();
    }

    /// <summary>Whether <paramref name="name"/> already belongs to a profile other than the one being edited.</summary>
    internal bool IsNameTaken(string name, string? previousName) =>
        _set.Profiles.Any(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal)
            && !string.Equals(p.Name, previousName, StringComparison.Ordinal));

    /// <summary>The standing <paramref name="name"/> would have as a host profile (STUDIO-48).</summary>
    internal HostProfileCheck CheckHostProfile(string name, string? previousName, bool describesProvider) =>
        _llm.CheckHostProfile(name, previousName, describesProvider, _set);

    /// <summary>The id the setting <paramref name="name"/> is offered to crews under; null when it is not.</summary>
    internal string? OfferedHostId(string name) =>
        HostLlmProfiles.Offered(_set).TryGetValue(name, out var id) ? id : null;

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
                Provider = seed?.Name,
                Model = seed?.DefaultModel,
                BaseUrl = seed?.DefaultBaseUrl,
            },
            previousName: null,
            _probe,
            _keyStore,
            _strings,
            _balances,
            _opener);
    }

    private IReadOnlyList<LlmPresetInfo> ProviderCatalog() => LlmPresets.ProviderCatalogFor(_strings);

    private Task _persist = Task.CompletedTask;

    private void Mutate(ModelProfileSet set, string? renamedFrom = null, string? renamedTo = null)
    {
        var before = _set;
        _set = set;
        // STUDIO-48: the settings are the host's profiles — the settings file follows every change
        // (a rename moves the entry, a removal takes it out, the key never goes in).
        _llm.MirrorModelProfiles(before, set, renamedFrom, renamedTo);
        Rebuild();
        // STUDIO-50: the launchers of the teams naming a moved setting say it again.
        if (HostLlmProfiles.MovedNames(before, set) is { Count: > 0 } moved)
            HostProfilesChanged?.Invoke(this, new HostProfilesChangedEventArgs(moved));
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

        // Mirrored into the Llm section so that a bare orkeon run follows the same election —
        // whole (STUDIO-49): its timeout, its thinking switch and the variable holding its key,
        // not the model and the endpoint alone. The write lands in the settings document and
        // travels through the screen's own edit then save cycle: Studio never saves the settings
        // file behind the user's back.
        _llm.ElectDefault(profile);
    }

    private void Rebuild()
    {
        var usage = TeamsByProfile();
        var standing = HostLlmProfiles.Classify(_set);

        Profiles.Clear();
        foreach (var profile in _set.Profiles)
        {
            Profiles.Add(new ModelProfileItemViewModel(
                profile,
                isDefault: string.Equals(profile.Name, _set.DefaultProfile, StringComparison.Ordinal),
                isStudio: string.Equals(profile.Name, _set.StudioProfile, StringComparison.Ordinal),
                this,
                usage.TryGetValue(profile.Name, out var usedBy) ? usedBy : [],
                _balances,
                _strings,
                standing.GetValueOrDefault(profile.Name)));
        }

        RebuildProfileNames();
        RebuildSecrets();
        RebuildHostProfiles();

        OnPropertyChanged(nameof(HasSecrets));

        OnPropertiesChanged(
            nameof(StudioProfileName), nameof(HasStudioProfile),
            nameof(DefaultProfileName), nameof(IsEmpty), nameof(CanDelete), nameof(Set));
    }

    /// <summary>
    /// The entries written by hand and the RAG's choices, read from the settings document as it
    /// stands. The choices are replaced only when they changed: the picker's selection is TwoWay,
    /// and a list cleared under it is a selection WPF nulls (see <see cref="RebuildProfileNames"/>).
    /// </summary>
    private void RebuildHostProfiles()
    {
        var handWritten = _llm.HandWrittenProfiles(_set);
        HandWrittenProfiles.Clear();
        foreach (var entry in handWritten)
            HandWrittenProfiles.Add(new HandWrittenProfileViewModel(entry, _strings));

        var choices = new List<RagProfileChoice> { new(null, _strings[StudioStringKeys.ProfileRagLlmDefault]) };
        choices.AddRange(HostLlmProfiles.Offered(_set).Select(offered =>
            new RagProfileChoice(offered.Value, string.Create(CultureInfo.InvariantCulture, $"{offered.Value} — {offered.Key}"))));
        choices.AddRange(handWritten.Select(entry =>
            new RagProfileChoice(entry.Id, entry.Model is { Length: > 0 } model
                ? string.Create(CultureInfo.InvariantCulture, $"{entry.Id} — {model}")
                : entry.Id)));
        if (_llm.RagLlmProfile is { } current
            && !LlmProfilesSection.IsDefault(current)
            && !choices.Any(choice => string.Equals(choice.Id, current.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new RagProfileChoice(current.Trim(), current.Trim()));
        }

        if (!RagProfileChoices.SequenceEqual(choices))
        {
            RagProfileChoices.Clear();
            foreach (var choice in choices)
                RagProfileChoices.Add(choice);
        }

        OnPropertiesChanged(nameof(HasHandWrittenProfiles), nameof(SelectedRagProfile));
    }

    /// <summary>
    /// The language switched: the cards' titles (STUDIO-54), the host profile lines and the default
    /// choice say it again.
    /// </summary>
    private void RefreshHostTexts()
    {
        foreach (var row in Profiles)
            row.RefreshTexts();
        RebuildHostProfiles();
    }

    /// <summary>
    /// The settings document was replaced — a file opened, a new one started —: what the screen
    /// reads from it is read again. The RAG's profile moved: the picker says it — without touching
    /// its list, which this very selection may be writing through.
    /// </summary>
    private void OnSettingsDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
            Rebuild();
        else if (e.PropertyName == nameof(LlmSectionViewModel.RagLlmProfile))
            OnPropertyChanged(nameof(SelectedRagProfile));
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
