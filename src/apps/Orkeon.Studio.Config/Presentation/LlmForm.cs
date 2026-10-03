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
    /// endpoint</c>, what a crew writes and what it gets. Read-only.
    /// </summary>
    public IReadOnlyList<string> Profiles { get; private set; } = [];

    /// <summary>The profile <c>Orkeon:Rag:LlmProfile</c> names; empty for the default. Read-only.</summary>
    public string RagLlmProfile { get; private set; } = "";

    /// <summary>The line saying which profile the document search calls.</summary>
    public string RagLlmProfileLine => LlmProfilesSection.IsDefault(RagLlmProfile)
        ? "Document search (RAG) answers on the default profile (the section above)"
        : string.Create(CultureInfo.InvariantCulture, $"Document search (RAG) answers on profile: {RagLlmProfile}");

    /// <summary>
    /// The connectivity probe for what the fields currently hold (SPEC §4.2). The key follows
    /// <see cref="LlmApiKeyResolver"/>: a user who took the standing advice and left the key in
    /// <c>ORKEON_Llm__ApiKey</c> must still be able to test the connection.
    /// </summary>
    /// <param name="environment">
    /// Reads an environment variable by name; defaults to the process environment.
    /// </param>
    /// <remarks>
    /// Like Studio's profile editor (STUDIO-43), the probe then runs a minimal completion on the
    /// typed model with the typed thinking switch, under 30 s or the typed timeout when shorter.
    /// A field that does not parse is left out: the validator reports it, the probe does not.
    /// </remarks>
    public LlmProbeRequest ToProbeRequest(Func<string, string?>? environment = null)
    {
        FieldText.TryReadBoolean(ThinkingEnabled, "Thinking:Enabled", out var thinking, out _);
        FieldText.TryReadInt32(TimeoutSeconds, "TimeoutSeconds", out var timeout, out _);

        return new LlmProbeRequest
        {
            BaseUrl = FieldText.ToStringOrNull(BaseUrl),
            ApiKey = environment is null
                ? LlmApiKeyResolver.Resolve(ApiKey)
                : LlmApiKeyResolver.Resolve(ApiKey, environment),
            Model = FieldText.ToStringOrNull(Model),
            ThinkingEnabled = thinking,
            CheckCompletion = true,
            Timeout = LlmProbeRequest.TimeoutFor(timeout),
        };
    }

    /// <summary>The label shown under the API key field.</summary>
    public static string ApiKeyRecommendation { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"Prefer the {LlmPresets.DefaultApiKeyEnv} environment variable — the runtime reads it " +
        $"with precedence over this file, and the key stays out of the JSON.");

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
        Temperature = FieldText.FromDouble(section.Temperature);
        MaxTokens = FieldText.FromInt32(section.MaxTokens);
        TimeoutSeconds = FieldText.FromInt32(section.TimeoutSeconds);
        ThinkingEnabled = FieldText.FromBoolean(section.ThinkingEnabled);
        Profiles =
        [
            .. section.Profiles.Ids
                .Select(section.Profiles.Get)
                .OfType<LlmProfileEntry>()
                .Select(entry => entry.Summary.Length > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"profile: {entry.Id} — {entry.Summary}")
                    : string.Create(CultureInfo.InvariantCulture, $"profile: {entry.Id}")),
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

        if (errors.Count > 0)
            return errors;

        var section = document.Llm;
        section.Model = FieldText.ToStringOrNull(Model);
        section.BaseUrl = FieldText.ToStringOrNull(BaseUrl);
        section.ApiKey = FieldText.ToStringOrNull(ApiKey);
        section.Temperature = temperature;
        section.MaxTokens = maxTokens;
        section.TimeoutSeconds = timeout;
        section.ThinkingEnabled = thinking;

        return [];
    }
}
