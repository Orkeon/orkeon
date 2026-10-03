using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>Llm</c> form (spec §4.1). There is deliberately no provider field: the provider is derived
/// from the host of <see cref="BaseUrl"/> and shown read-only, and the API key carries the
/// recommendation to name the variable holding it (<c>ApiKeyEnvVar</c>) instead of clear text.
/// <para>
/// <see cref="TestConnectionCommand"/> is the optional connectivity probe of spec §4.2: it reports
/// what the endpoint answered and gates nothing — a failed test never stops a save.
/// </para>
/// </summary>
public sealed class LlmSectionViewModel : DocumentSectionViewModel
{
    private readonly ILlmEndpointProbe _probe;
    private readonly IUiDispatcher _dispatcher;
    private readonly IStudioStrings _strings;
    private string? _connectionTestResult;

    /// <summary>Binds the form to the <c>Llm</c> section of the document.</summary>
    /// <param name="document">Supplies the document currently being edited.</param>
    /// <param name="onChanged">Called whenever a field writes into the document.</param>
    /// <param name="probe">Runs the connectivity test; defaults to a real HTTP probe.</param>
    /// <param name="dispatcher">Marshals the probe's answer back to the UI thread.</param>
    /// <param name="strings">Localization port; defaults to the English strings (STUDIO-11).</param>
    public LlmSectionViewModel(
        Func<AppSettingsDocument> document,
        Action onChanged,
        ILlmEndpointProbe? probe = null,
        IUiDispatcher? dispatcher = null,
        IStudioStrings? strings = null)
        : base(document, onChanged)
    {
        // Lives as long as the tab, which lives as long as the window.
        _probe = probe ?? HttpLlmEndpointProbe.ForCurrentMachine();
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) =>
            OnPropertiesChanged(nameof(DetectedProviderDisplay), nameof(ApiKeyRecommendation));
        TestConnectionCommand = new AsyncRelayCommand(() => TestConnectionAsync());
    }

    private LlmSection Section => Document.Llm;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>The model name sent to the provider.</summary>
    public string? Model
    {
        get => Section.Model;
        set => SetValue(Section.Model, Blank(value), v => Section.Model = v);
    }

    /// <summary>The OpenAI-compatible endpoint. Its host is what identifies the provider.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Mirrors LlmSection.BaseUrl: this is the JSON field as the user types it, so a "
                        + "half-typed or malformed URL must survive the round-trip and be reported by the "
                        + "validator. System.Uri cannot represent one.")]
    public string? BaseUrl
    {
        get => Section.BaseUrl;
        set => SetValue(Section.BaseUrl, Blank(value), v => Section.BaseUrl = v);
    }

    /// <summary>The API key written in clear text in the file — discouraged, see <see cref="ApiKeyRecommendation"/>.</summary>
    public string? ApiKey
    {
        get => Section.ApiKey;
        set => SetValue(Section.ApiKey, Blank(value), v => Section.ApiKey = v);
    }

    /// <summary>
    /// The variable holding the default's key (<c>Llm:ApiKeyEnvVar</c>, STUDIO-49) — its name, never
    /// the key —, which every run reads; the election of a model setting writes it.
    /// </summary>
    public string? ApiKeyEnvVar
    {
        get => Section.ApiKeyEnvVar;
        set => SetValue(Section.ApiKeyEnvVar, Blank(value), v => Section.ApiKeyEnvVar = v);
    }

    /// <summary>Sampling temperature.</summary>
    public double? Temperature
    {
        get => Section.Temperature;
        set => SetValue(Section.Temperature, value, v => Section.Temperature = v);
    }

    /// <summary>Maximum tokens per completion.</summary>
    public int? MaxTokens
    {
        get => Section.MaxTokens;
        set => SetValue(Section.MaxTokens, value, v => Section.MaxTokens = v);
    }

    /// <summary>HTTP timeout, in seconds.</summary>
    public int? TimeoutSeconds
    {
        get => Section.TimeoutSeconds;
        set => SetValue(Section.TimeoutSeconds, value, v => Section.TimeoutSeconds = v);
    }

    /// <summary>
    /// The provider inferred from <see cref="BaseUrl"/>. Read-only on purpose: no <c>Provider</c> key
    /// exists in the schema, so offering to edit one would invent configuration the runtime ignores.
    /// </summary>
    public string DetectedProvider => Section.DetectedProvider;

    /// <summary>The detected provider, phrased for the read-only label next to the base URL.</summary>
    public string DetectedProviderDisplay => DetectedProvider switch
    {
        LlmProviderDetector.None => _strings[StudioStringKeys.LlmNoBaseUrl],
        LlmProviderDetector.Custom => _strings[StudioStringKeys.LlmCustomProvider],
        var provider => provider,
    };

    /// <summary>The environment variable the UI recommends over an inline key.</summary>
    public static string ApiKeyEnvironmentVariable => LlmPresets.DefaultApiKeyEnv;

    /// <summary>The advice shown under the API key box.</summary>
    public string ApiKeyRecommendation => string.Format(
        CultureInfo.InvariantCulture,
        _strings[StudioStringKeys.LlmApiKeyRecommendation],
        LlmPresets.DefaultApiKeyEnv);

    /// <summary>Whether a key is currently stored in the file, which the validator reports.</summary>
    public bool HasInlineApiKey => ApiKey is { Length: > 0 };

    /// <summary>Runs the optional connectivity test against the configured endpoint.</summary>
    public AsyncRelayCommand TestConnectionCommand { get; }

    /// <summary>
    /// What the last connectivity test reported, or <see langword="null"/> when none has run.
    /// Purely informational — nothing in the tab reads it back.
    /// </summary>
    public string? ConnectionTestResult
    {
        get => _connectionTestResult;
        private set => SetProperty(ref _connectionTestResult, value);
    }

    /// <summary>True while a test is in flight, so the view can show it is working.</summary>
    public bool IsTestingConnection => TestConnectionCommand.IsRunning;

    /// <summary>
    /// Probes the configured endpoint and publishes the verdict on
    /// <see cref="ConnectionTestResult"/>. The key follows <see cref="LlmApiKeyResolver"/>, so a
    /// user who took the advice above and kept the key in the environment can still test.
    /// </summary>
    /// <param name="cancellationToken">Abandons the test.</param>
    /// <returns>The probe's verdict, so callers can assert on it.</returns>
    [SuppressMessage("Design", "CA1031",
        Justification = "The test is a convenience that must never fault the command: any unexpected " +
                        "failure is reported in the result line like every other unreachable endpoint.")]
    public async Task<LlmProbeResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        ConnectionTestResult = _strings[StudioStringKeys.LlmTesting];
        OnPropertyChanged(nameof(IsTestingConnection));

        LlmProbeResult result;
        try
        {
            // STUDIO-43: the same two steps as the profile editor — the catalogue, then a
            // minimal completion on the configured model with its thinking switch.
            result = await _probe.ProbeAsync(
                new LlmProbeRequest
                {
                    BaseUrl = BaseUrl,
                    ApiKey = LlmApiKeyResolver.Resolve(ApiKey, ApiKeyEnvVar),
                    Model = Model,
                    ThinkingEnabled = Section.ThinkingEnabled,
                    CheckCompletion = true,
                    Timeout = LlmProbeRequest.TimeoutFor(TimeoutSeconds),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = LlmProbeResult.Unreachable(ex.Message);
        }

        _dispatcher.Post(() =>
        {
            ConnectionTestResult = LlmProbeText.Describe(result, _strings);
            OnPropertyChanged(nameof(IsTestingConnection));
        });

        return result;
    }

    /// <summary>
    /// Removes the default provider, which is how the "None / offline" preset is expressed. The
    /// named profiles of <c>Llm:Profiles</c> stay (STUDIO-48): « no model » for the default says
    /// nothing about them.
    /// </summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }

    // ── the host LLM profiles (STUDIO-48): Llm:Profiles, and the one the RAG calls ──

    /// <summary>
    /// The host LLM profile the RAG subsystem calls (<c>Orkeon:Rag:LlmProfile</c>, GAP-19) — one
    /// of <c>Llm:Profiles</c>, null for the default. It lives under <c>Orkeon:Rag</c> but is chosen
    /// among this section's profiles, on the model-settings screen; writing the default removes
    /// the key.
    /// </summary>
    public string? RagLlmProfile
    {
        get => Document.Rag.LlmProfile;
        set => SetValue(Document.Rag.LlmProfile, value is { } name && !LlmProfilesSection.IsDefault(name) ? name.Trim() : null, v => Document.Rag.LlmProfile = v);
    }

    /// <summary>
    /// Brings <c>Llm:Profiles</c> in line with a change of Studio's model settings (STUDIO-48,
    /// <see cref="HostLlmProfiles.Mirror"/>), and heals a default the election wrote before it
    /// named its key's variable (STUDIO-49, <see cref="HostLlmProfiles.HealDefault"/>): one edit of
    /// the document when anything changed, so the write travels through the screen's own
    /// edit-then-save cycle like the election.
    /// </summary>
    public void MirrorModelProfiles(ModelProfileSet before, ModelProfileSet after, string? renamedFrom = null, string? renamedTo = null)
    {
        ArgumentNullException.ThrowIfNull(after);

        var profilesChanged = HostLlmProfiles.Mirror(Document, before, after, renamedFrom, renamedTo);
        var defaultHealed = HostLlmProfiles.HealDefault(Document, after);
        if (!profilesChanged && !defaultHealed)
            return;

        // A rename or a removal may have moved the RAG's profile with it.
        OnPropertyChanged(nameof(RagLlmProfile));
        if (defaultHealed)
            OnDefaultRewritten();
        NotifyDocumentChanged();
    }

    /// <summary>
    /// Writes the elected setting into the section, whole (STUDIO-49, decision 5): every field it
    /// pins — its timeout and thinking switch among them — and the variable holding its key, so a
    /// run outside Studio follows the election. The keys Studio does not model stay. Through the
    /// screen's own edit-then-save cycle: Studio never saves the settings file behind the user's back.
    /// </summary>
    public void ElectDefault(ModelProfile profile)
    {
        if (!HostLlmProfiles.ElectDefault(Document, profile))
            return;

        OnDefaultRewritten();
        NotifyDocumentChanged();
    }

    /// <summary>Every field the election writes says it again — never the whole form, which would read as a new document.</summary>
    private void OnDefaultRewritten() =>
        OnPropertiesChanged(
            nameof(Model), nameof(BaseUrl), nameof(ApiKeyEnvVar), nameof(Temperature), nameof(MaxTokens), nameof(TimeoutSeconds));

    /// <summary>The entries of <c>Llm:Profiles</c> no setting of <paramref name="set"/> owns: written by hand, shown read-only.</summary>
    public IReadOnlyList<LlmProfileEntry> HandWrittenProfiles(ModelProfileSet set) => HostLlmProfiles.HandWritten(Document, set);

    /// <summary>The standing a name typed in the profile editor would have, against this document's entries.</summary>
    public HostProfileCheck CheckHostProfile(string name, string? previousName, bool describesProvider, ModelProfileSet set) =>
        HostLlmProfiles.Check(name, previousName, describesProvider, set, Document);

    /// <inheritdoc />
    protected override void OnSectionChanged() => OnPropertiesChanged(
        nameof(Exists),
        nameof(DetectedProvider),
        nameof(DetectedProviderDisplay),
        nameof(HasInlineApiKey));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
