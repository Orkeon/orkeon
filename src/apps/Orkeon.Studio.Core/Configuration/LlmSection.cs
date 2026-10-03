using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Orkeon.Constants.Configuration;

namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over the <c>Llm</c> section of an <see cref="AppSettingsDocument"/>.
/// There is deliberately no <c>Provider</c> key: the runtime infers the dialect from
/// the endpoint, so Studio exposes <see cref="DetectedProvider"/> read-only.
/// </summary>
public sealed class LlmSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = "Llm";

    private readonly AppSettingsDocument _document;

    internal LlmSection(AppSettingsDocument document)
    {
        _document = document;
        Profiles = new LlmProfilesSection(document);
    }

    /// <summary>
    /// The named profiles under <c>Llm:Profiles</c> (GAP-17, STUDIO-48) — part of this section in
    /// the file, but not part of the default provider it describes.
    /// </summary>
    public LlmProfilesSection Profiles { get; }

    /// <summary>
    /// True when the section describes the default provider: at least one key besides
    /// <c>Profiles</c> that holds a value, the runtime's own reading (<c>LlmSettings.HasDefault</c>,
    /// where a blank value reads as absent — STUDIO-49). False is the WIN-01 condition: the runtime
    /// silently falls back to the <c>&lt;undefined-llm&gt;</c> echo provider — a section holding
    /// profiles alone included.
    /// </summary>
    public bool Exists =>
        _document.GetNode(SectionPath) is JsonObject section
        && section.Any(property =>
            !string.Equals(property.Key, ConfigurationKeys.LlmProfiles, StringComparison.OrdinalIgnoreCase)
            && HoldsValue(property.Value));

    /// <summary>Model identifier (<c>Llm:Model</c>).</summary>
    public string? Model
    {
        get => _document.GetString($"{SectionPath}:Model");
        set => _document.SetString($"{SectionPath}:Model", value);
    }

    /// <summary>Endpoint base URL (<c>Llm:BaseUrl</c>).</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "This is the JSON field as typed by the user, not a resolved endpoint: an " +
                        "in-progress or malformed URL must survive load/edit/save, which System.Uri " +
                        "cannot represent. Validation reports it instead of rejecting the text.")]
    public string? BaseUrl
    {
        get => _document.GetString($"{SectionPath}:BaseUrl");
        set => _document.SetString($"{SectionPath}:BaseUrl", value);
    }

    /// <summary>
    /// Inline API key (<c>Llm:ApiKey</c>) — discouraged: prefer <see cref="ApiKeyEnvVar"/>, which
    /// names the variable holding the key. A key here masks that reference: the runtime uses a key
    /// the configuration resolves first. Docker Model Runner's placeholder <c>not-needed</c> is no
    /// key: its server checks none, and the run's OpenAI dialect wants one.
    /// </summary>
    public string? ApiKey
    {
        get => _document.GetString($"{SectionPath}:ApiKey");
        set => _document.SetString($"{SectionPath}:ApiKey", value);
    }

    /// <summary>
    /// The environment variable holding the default's key (<c>Llm:ApiKeyEnvVar</c>, STUDIO-49) —
    /// its name, never the key —, which every run reads, Studio's or not. Written by the election
    /// of a model setting, by <c>orkeon init --api-key-env</c> and by <c>orkeon-studio-config</c>.
    /// </summary>
    public string? ApiKeyEnvVar
    {
        get => _document.GetString($"{SectionPath}:{ConfigurationKeys.LlmApiKeyEnvVar}");
        set => _document.SetString($"{SectionPath}:{ConfigurationKeys.LlmApiKeyEnvVar}", value);
    }

    /// <summary>Sampling temperature (<c>Llm:Temperature</c>).</summary>
    public double? Temperature
    {
        get => _document.GetDouble($"{SectionPath}:Temperature");
        set => _document.SetDouble($"{SectionPath}:Temperature", value);
    }

    /// <summary>Maximum response tokens (<c>Llm:MaxTokens</c>).</summary>
    public int? MaxTokens
    {
        get => _document.GetInt32($"{SectionPath}:MaxTokens");
        set => _document.SetInt32($"{SectionPath}:MaxTokens", value);
    }

    /// <summary>Request timeout in seconds (<c>Llm:TimeoutSeconds</c>).</summary>
    public int? TimeoutSeconds
    {
        get => _document.GetInt32($"{SectionPath}:TimeoutSeconds");
        set => _document.SetInt32($"{SectionPath}:TimeoutSeconds", value);
    }

    /// <summary>
    /// Thinking switch (<c>Llm:Thinking:Enabled</c>): null leaves the provider's default —
    /// on for Kimi K2.6, DeepSeek V4 and GLM. The runner reads the same key (LLM-11).
    /// </summary>
    public bool? ThinkingEnabled
    {
        get => _document.GetBoolean($"{SectionPath}:Thinking:Enabled");
        set => _document.SetBoolean($"{SectionPath}:Thinking:Enabled", value);
    }

    /// <summary>Reasoning-effort hint (<c>Llm:Thinking:Effort</c>), for the models that take one.</summary>
    public string? ThinkingEffort
    {
        get => _document.GetString($"{SectionPath}:Thinking:Effort");
        set => _document.SetString($"{SectionPath}:Thinking:Effort", value);
    }

    /// <summary>
    /// Provider inferred from <see cref="BaseUrl"/> — informational only, never written
    /// to the file. See <see cref="LlmProviderDetector"/>.
    /// </summary>
    public string DetectedProvider => LlmProviderDetector.Detect(BaseUrl);

    /// <summary>
    /// Writes what a model setting pins into the default section, field by field (STUDIO-49,
    /// the election): every field <see cref="LlmProfileEntry"/> models, a null one removing its
    /// key, the reference to the key's variable included — so a run outside Studio follows the
    /// election, its timeout and its key included. The keys Studio does not model stay where they
    /// are: <c>MaxRetries</c>, <c>Grammar</c>, <c>AvailableModels</c>, <c>Profiles</c>, and an
    /// <c>ApiKey</c> — but Docker Model Runner's placeholder, which an elected Docker Model Runner
    /// setting writes where no key is set and any other takes out (<see cref="KeyAgrees"/>,
    /// STUDIO-54): left under another card, it would pass before the variable the elected setting
    /// names. The entry's <see cref="LlmProfileEntry.Id"/> is not read.
    /// </summary>
    /// <param name="entry">What the elected setting pins.</param>
    /// <returns>True when the document changed.</returns>
    public bool Set(LlmProfileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var before = _document.GetNode(SectionPath)?.ToJsonString();
        LlmProfilesSection.WriteEntry(_document, SectionPath, entry);
        if (_document.GetNode(SectionPath) is JsonObject { Count: 0 })
            _document.Remove(SectionPath);
        return !string.Equals(before, _document.GetNode(SectionPath)?.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the section's <c>ApiKey</c> agrees with the elected <paramref name="entry"/>
    /// (<see cref="LlmProfilesSection.KeyAgrees(AppSettingsDocument, string, LlmProfileEntry)"/>, STUDIO-54).
    /// </summary>
    /// <param name="entry">What the elected setting writes.</param>
    public bool KeyAgrees(LlmProfileEntry entry) => LlmProfilesSection.KeyAgrees(_document, SectionPath, entry);

    /// <summary>
    /// Whether a JSON node holds a value as the configuration reads it: a scalar that is not blank,
    /// or a container holding one. <c>null</c>, <c>""</c> and <c>{ }</c> read as absent.
    /// </summary>
    private static bool HoldsValue(JsonNode? node) => node switch
    {
        JsonObject container => container.Any(property => HoldsValue(property.Value)),
        JsonArray items => items.Any(HoldsValue),
        JsonValue value when value.TryGetValue<string>(out var text) => !string.IsNullOrWhiteSpace(text),
        JsonValue => true,
        _ => false,
    };

    /// <summary>
    /// Removes the default provider — every key of the section but <c>Profiles</c> — which is
    /// the WIN-01 state. The named profiles stay: choosing « no model » for the default says
    /// nothing about them, and an entry written by hand is never Studio's to delete (STUDIO-48).
    /// The section goes entirely only when no profile is left in it.
    /// </summary>
    public void Remove()
    {
        if (_document.GetNode(SectionPath) is not JsonObject section)
        {
            _document.Remove(SectionPath);
            return;
        }

        foreach (var key in section
                     .Select(property => property.Key)
                     .Where(key => !string.Equals(key, ConfigurationKeys.LlmProfiles, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            section.Remove(key);
        }

        if (section.Count == 0)
            _document.Remove(SectionPath);
    }
}
