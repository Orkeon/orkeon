using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// The <c>Llm</c> section as text fields. There is no provider field to edit: the runtime
/// infers the dialect from the endpoint, so <see cref="DetectedProvider"/> is shown
/// read-only next to the base URL.
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

    /// <summary>Provider inferred from <see cref="BaseUrl"/>; never written to the file.</summary>
    public string DetectedProvider => LlmProviderDetector.Detect(BaseUrl);

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

        if (errors.Count > 0)
            return errors;

        var section = document.Llm;
        section.Model = FieldText.ToStringOrNull(Model);
        section.BaseUrl = FieldText.ToStringOrNull(BaseUrl);
        section.ApiKey = FieldText.ToStringOrNull(ApiKey);
        section.Temperature = temperature;
        section.MaxTokens = maxTokens;
        section.TimeoutSeconds = timeout;

        return [];
    }
}
