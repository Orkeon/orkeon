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
    /// <c>Profiles</c>, the runtime's own reading (<c>LlmSettings.HasDefault</c>). False is the
    /// WIN-01 condition: the runtime silently falls back to the <c>&lt;undefined-llm&gt;</c> echo
    /// provider — a section holding profiles alone included.
    /// </summary>
    public bool Exists =>
        _document.GetNode(SectionPath) is JsonObject section
        && section.Any(property => !string.Equals(property.Key, ConfigurationKeys.LlmProfiles, StringComparison.OrdinalIgnoreCase));

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
    /// Inline API key (<c>Llm:ApiKey</c>). Prefer the <c>ORKEON_Llm__ApiKey</c>
    /// environment variable — see <see cref="Presets.LlmPresets.DefaultApiKeyEnv"/>;
    /// the runtime reads environment variables with precedence over the file.
    /// </summary>
    public string? ApiKey
    {
        get => _document.GetString($"{SectionPath}:ApiKey");
        set => _document.SetString($"{SectionPath}:ApiKey", value);
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
