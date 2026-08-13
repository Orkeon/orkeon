using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>Llm</c> form (spec §4.1). There is deliberately no provider field: the provider is derived
/// from the host of <see cref="BaseUrl"/> and shown read-only, and the API key carries the
/// recommendation to use the <c>ORKEON_Llm__ApiKey</c> environment variable instead of clear text.
/// </summary>
public sealed class LlmSectionViewModel : DocumentSectionViewModel
{
    /// <summary>Binds the form to the <c>Llm</c> section of the document.</summary>
    public LlmSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
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
        LlmProviderDetector.None => "No base URL — the runtime falls back to the echo provider.",
        LlmProviderDetector.Custom => "custom (host not in the known-endpoint table)",
        var provider => provider,
    };

    /// <summary>The environment variable the UI recommends over an inline key.</summary>
    public static string ApiKeyEnvironmentVariable => LlmPresets.DefaultApiKeyEnv;

    /// <summary>The advice shown under the API key box.</summary>
    public static string ApiKeyRecommendation =>
        $"Prefer the {LlmPresets.DefaultApiKeyEnv} environment variable: the runtime reads it with "
        + "precedence over this file, so the key never has to be stored in clear text.";

    /// <summary>Whether a key is currently stored in the file, which the validator reports.</summary>
    public bool HasInlineApiKey => ApiKey is { Length: > 0 };

    /// <summary>Removes the whole section, which is how the "None / offline" preset is expressed.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }

    /// <inheritdoc />
    protected override void OnSectionChanged() => OnPropertiesChanged(
        nameof(Exists),
        nameof(DetectedProvider),
        nameof(DetectedProviderDisplay),
        nameof(HasInlineApiKey));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
