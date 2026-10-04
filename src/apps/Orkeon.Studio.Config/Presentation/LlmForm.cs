using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// The <c>Llm</c> section as text fields. There is no provider field to edit: the runtime
/// infers the dialect from the endpoint, so <see cref="DetectedProvider"/> is shown
/// read-only next to the base URL. The named profiles of <c>Llm:Profiles</c> and the one the
/// RAG calls are shown read-only too (STUDIO-48): Studio's model settings write the first, its
/// model screen chooses the second, and this form writes neither — applying it touches the
/// default provider's keys alone, so every profile, written by hand or not, stays as it is.
/// An election made in Studio afterwards rewrites the fields it owns (STUDIO-49).
/// </summary>
internal sealed class LlmForm : ISettingsForm
{
    /// <inheritdoc />
    public string Title => "LLM";

    /// <summary>Model identifier.</summary>
    public string Model { get; set; } = "";

    /// <summary>Endpoint base URL, exactly as typed.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Form text on its way to the JSON 'Llm:BaseUrl' string field: a half-typed " +
                        "or malformed URL must survive editing, which System.Uri cannot represent.")]
    public string BaseUrl { get; set; } = "";

    /// <summary>API key stored in the file — discouraged, see <see cref="ApiKeyRecommendation"/>.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// The environment variable holding the key (<c>Llm:ApiKeyEnvVar</c>, STUDIO-49): its name,
    /// never the key. Every run reads it when the configuration holds no key.
    /// </summary>
    public string ApiKeyEnvVar { get; set; } = "";

    /// <summary>Sampling temperature.</summary>
    public string Temperature { get; set; } = "";

    /// <summary>Maximum response tokens.</summary>
    public string MaxTokens { get; set; } = "";

    /// <summary>Request timeout in seconds.</summary>
    public string TimeoutSeconds { get; set; } = "";

    /// <summary>
    /// Thinking switch (<c>Llm:Thinking:Enabled</c>): "true", "false", or blank for the
    /// provider's default — on for Kimi K2.6, DeepSeek V4 and GLM (LLM-11).
    /// </summary>
    public string ThinkingEnabled { get; set; } = "";

    /// <summary>Provider inferred from <see cref="BaseUrl"/>; never written to the file.</summary>
    public string DetectedProvider => LlmProviderDetector.Detect(BaseUrl);

    /// <summary>
    /// The named profiles of <c>Llm:Profiles</c>, one line each — <c>profile: &lt;id&gt; — model ·
    /// endpoint — key: &lt;variable&gt;</c>, what a crew writes, what it gets and where a run reads its
    /// key (STUDIO-49). Read-only.
    /// </summary>
    public IReadOnlyList<string> Profiles { get; private set; } = [];

    /// <summary>The profile <c>Orkeon:Rag:LlmProfile</c> names; empty for the default. Read-only.</summary>
    public string RagLlmProfile { get; private set; } = "";

    /// <summary>The line saying which profile the document search calls.</summary>
    public string RagLlmProfileLine => LlmProfilesSection.IsDefault(RagLlmProfile)
        ? "Document search (RAG) answers on the default profile (the section above)"
        : string.Create(CultureInfo.InvariantCulture, $"Document search (RAG) answers on profile: {RagLlmProfile}");

    /// <summary>
    /// The connectivity probe for what the fields currently hold (SPEC §4.2). The key is the one a
    /// run presents, in the run's order and under every spelling a run reads
    /// (<see cref="LlmApiKeyResolver"/>, STUDIO-54, STUDIO-56): the <c>ORKEON_</c> variables first —
    /// <c>ORKEON_Llm__ApiKey</c>, <c>ORKEON_LLM__APIKEY</c>…, the environment wins over this file —,
    /// then the key typed here, then <c>Llm__ApiKey</c>, and only then the variable
    /// <see cref="ApiKeyEnvVar"/> names. A user who took the standing advice and left the key in the
    /// environment can test the connection, and a key typed here that the environment overrides is not
    /// the one tested. Two spellings with different values present no key: see <see cref="ResolveKey"/>.
    /// </summary>
    /// <param name="environment">The process environment and the user scope.</param>
    /// <remarks>
    /// Like Studio's profile editor (STUDIO-43), the probe then runs a minimal completion on the
    /// typed model with the typed thinking switch, under 30 s or the typed timeout when shorter.
    /// A field that does not parse is left out: the validator reports it, the probe does not.
    /// </remarks>
    public LlmProbeRequest ToProbeRequest(IEnvironmentVariables environment) =>
        Request(ResolveKey(environment).Key);

    /// <summary>The key the probe presents, none, or the conflict of two spellings (STUDIO-56).</summary>
    /// <param name="environment">The process environment and the user scope.</param>
    public LlmApiKeyResolution ResolveKey(IEnvironmentVariables environment) =>
        LlmApiKeyResolver.Resolve(ApiKey, FieldText.ToStringOrNull(ApiKeyEnvVar), environment);

    /// <summary>
    /// What the screen says, without a request, when two variables set the key with different values
    /// (STUDIO-56): a run reads either one. The line names the setting and the variables, never their
    /// values.
    /// </summary>
    /// <param name="resolution">A conflict.</param>
    public static string KeyConflictLine(LlmApiKeyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var names = resolution.ConflictingVariables;
        var listed = names.Count <= 2
            ? string.Join(" and ", names)
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        var which = names.Count == 2 ? "both set" : "all set";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"API key ambiguous — {listed} {which} {resolution.Setting}, with different values: a run reads either one. Keep one.");
    }

    /// <summary>
    /// What the screen says, without a request, when the endpoint needs a key and none resolves
    /// (STUDIO-54): a run refuses every endpoint but Ollama's without one. The TUI remembers no
    /// key: the remedy names the variable, or the one the runtime reads.
    /// </summary>
    public static string KeyMissingLine { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"API key missing — name the variable that holds it (API key variable), or set {LlmPresets.DefaultApiKeyEnv}.");

    private LlmProbeRequest Request(string? apiKey)
    {
        FieldText.TryReadBoolean(ThinkingEnabled, "Thinking:Enabled", out var thinking, out _);
        FieldText.TryReadInt32(TimeoutSeconds, "TimeoutSeconds", out var timeout, out _);

        return new LlmProbeRequest
        {
            BaseUrl = FieldText.ToStringOrNull(BaseUrl),
            ApiKey = apiKey,
            Model = FieldText.ToStringOrNull(Model),
            ThinkingEnabled = thinking,
            CheckCompletion = true,
            Timeout = LlmProbeRequest.TimeoutFor(timeout),
        };
    }

    /// <summary>The label shown under the API key fields.</summary>
    public static string ApiKeyRecommendation { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"Prefer naming the variable that holds the key (Llm:ApiKeyEnvVar) — every run reads it, " +
        $"and the key stays out of the JSON — or set {LlmPresets.DefaultApiKeyEnv}, which the runtime " +
        $"reads with precedence over this file.");

    /// <summary>True when a real key would be written in clear text on save.</summary>
    public bool StoresApiKeyInClearText =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.Equals(ApiKey.Trim(), LlmPresets.DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal);

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.Llm;
        Model = FieldText.FromString(section.Model);
        BaseUrl = FieldText.FromString(section.BaseUrl);
        ApiKey = FieldText.FromString(section.ApiKey);
        ApiKeyEnvVar = FieldText.FromString(section.ApiKeyEnvVar);
        // STUDIO-55: as the file writes them — a value the field cannot read is shown, and refused
        // when applied, never loaded empty and erased.
        Temperature = document.GetWritten($"{LlmSection.SectionPath}:Temperature");
        MaxTokens = document.GetWritten($"{LlmSection.SectionPath}:MaxTokens");
        TimeoutSeconds = document.GetWritten($"{LlmSection.SectionPath}:TimeoutSeconds");
        ThinkingEnabled = document.GetWritten($"{LlmSection.SectionPath}:Thinking:Enabled");
        Profiles =
        [
            .. section.Profiles.Ids
                .Select(section.Profiles.Get)
                .OfType<LlmProfileEntry>()
                .Select(ProfileLine),
        ];
        RagLlmProfile = FieldText.FromString(document.Rag.LlmProfile);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        var errors = new List<string>();

        if (!FieldText.TryReadDouble(Temperature, "Llm:Temperature", out var temperature, out var temperatureError))
            errors.Add(temperatureError!);

        if (!FieldText.TryReadInt32(MaxTokens, "Llm:MaxTokens", out var maxTokens, out var maxTokensError))
            errors.Add(maxTokensError!);

        if (!FieldText.TryReadInt32(TimeoutSeconds, "Llm:TimeoutSeconds", out var timeout, out var timeoutError))
            errors.Add(timeoutError!);

        if (!FieldText.TryReadBoolean(ThinkingEnabled, "Llm:Thinking:Enabled", out var thinking, out var thinkingError))
            errors.Add(thinkingError!);

        // The runtime refuses to start on a reference that cannot be a variable's name — say it
        // here, by its path: the value may be a key pasted in the wrong field.
        if (FieldText.ToStringOrNull(ApiKeyEnvVar) is { } reference && !LlmSection.IsVariableName(reference))
        {
            errors.Add("Llm:ApiKeyEnvVar must be the name of an environment variable (no '=', space or line break) — never the key itself.");
        }

        if (errors.Count > 0)
            return errors;

        var section = document.Llm;
        section.Model = FieldText.ToStringOrNull(Model);
        section.BaseUrl = FieldText.ToStringOrNull(BaseUrl);
        section.ApiKey = FieldText.ToStringOrNull(ApiKey);
        section.ApiKeyEnvVar = FieldText.ToStringOrNull(ApiKeyEnvVar);
        section.Temperature = temperature;
        section.MaxTokens = maxTokens;
        section.TimeoutSeconds = timeout;
        section.ThinkingEnabled = thinking;

        return [];
    }

    /// <summary>One read-only line per profile: what a crew writes, what it gets, where its key comes from.</summary>
    private static string ProfileLine(LlmProfileEntry entry)
    {
        var line = entry.Summary.Length > 0
            ? string.Create(CultureInfo.InvariantCulture, $"profile: {entry.Id} — {entry.Summary}")
            : string.Create(CultureInfo.InvariantCulture, $"profile: {entry.Id}");
        return entry.ApiKeyEnvVar is { Length: > 0 } variable
            ? string.Create(CultureInfo.InvariantCulture, $"{line} — key: {variable}")
            : line;
    }
}
