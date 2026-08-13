using System.Diagnostics.CodeAnalysis;

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

    internal LlmSection(AppSettingsDocument document) => _document = document;

    /// <summary>
    /// True when the section carries at least one key. False is the WIN-01 condition:
    /// the runtime silently falls back to the <c>&lt;undefined-llm&gt;</c> echo provider.
    /// </summary>
    public bool Exists => _document.SectionExists(SectionPath);

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
    /// Provider inferred from <see cref="BaseUrl"/> — informational only, never written
    /// to the file. See <see cref="LlmProviderDetector"/>.
    /// </summary>
    public string DetectedProvider => LlmProviderDetector.Detect(BaseUrl);

    /// <summary>Removes the whole section (the WIN-01 state).</summary>
    public void Remove() => _document.Remove(SectionPath);
}
