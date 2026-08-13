using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Presets;

/// <summary>Values the user may override on top of a preset.</summary>
public sealed record LlmPresetOverrides
{
    /// <summary>Endpoint base URL; required by the <c>custom</c> preset.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "User-typed form value on its way to a JSON string field; a half-typed or " +
                        "malformed URL must be carried and reported, which System.Uri cannot do.")]
    public string? BaseUrl { get; init; }

    /// <summary>Model identifier; required by the <c>custom</c> preset.</summary>
    public string? Model { get; init; }

    /// <summary>API key to store <b>inline</b> — discouraged; prefer <see cref="ApiKeyEnv"/>.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Environment variable that will hold the API key; the key stays out of the file.</summary>
    public string? ApiKeyEnv { get; init; }
}

/// <summary>A resolved preset: exactly what will be written, and how the key is provided.</summary>
public sealed record LlmPresetPlan
{
    /// <summary>Preset name (see <see cref="LlmPresets.Names"/>).</summary>
    public required string Preset { get; init; }

    /// <summary>Endpoint base URL, or <see langword="null"/> for the <c>none</c> preset.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "The value is written verbatim into the JSON 'Llm:BaseUrl' string field; " +
                        "round-tripping the user's exact text is the point.")]
    public string? BaseUrl { get; init; }

    /// <summary>Model identifier, or <see langword="null"/> for the <c>none</c> preset.</summary>
    public string? Model { get; init; }

    /// <summary>API key written into the file, when the user chose that over an environment variable.</summary>
    public string? InlineApiKey { get; init; }

    /// <summary>Environment variable the key is read from; nothing is written to the file.</summary>
    public string? ApiKeyEnvName { get; init; }
}

/// <summary>Presentation metadata for one preset, so the UIs need no preset table of their own.</summary>
/// <param name="Name">Preset name.</param>
/// <param name="Title">Short label.</param>
/// <param name="Description">One-line description.</param>
/// <param name="DefaultBaseUrl">Base URL applied when the user overrides nothing.</param>
/// <param name="DefaultModel">Model applied when the user overrides nothing.</param>
/// <param name="RequiresApiKey">True when the endpoint authenticates requests.</param>
[SuppressMessage("Design", "CA1054",
    Justification = "Presentation metadata: the default endpoint is shown in, and edited from, a " +
                    "text field before it becomes a JSON string field.")]
[SuppressMessage("Design", "CA1056",
    Justification = "Presentation metadata: the default endpoint is shown in, and edited from, a " +
                    "text field before it becomes a JSON string field.")]
public sealed record LlmPresetInfo(
    string Name,
    string Title,
    string Description,
    string? DefaultBaseUrl,
    string? DefaultModel,
    bool RequiresApiKey);

/// <summary>
/// The <c>orkeon init</c> presets, reproduced for Studio: same names, same defaults,
/// same generated JSON, same policy of referencing the API key from
/// <see cref="DefaultApiKeyEnv"/> instead of writing it into the file.
/// </summary>
public static class LlmPresets
{
    /// <summary>Local Ollama server.</summary>
    public const string Ollama = "ollama";

    /// <summary>Docker Model Runner (llama.cpp, OpenAI-compatible).</summary>
    public const string DockerModelRunner = "docker-model-runner";

    /// <summary>OpenAI cloud API.</summary>
    public const string OpenAI = "openai";

    /// <summary>Any other OpenAI-compatible endpoint.</summary>
    public const string Custom = "custom";

    /// <summary>No LLM: the runtime falls back to the echo provider.</summary>
    public const string None = "none";

    /// <summary>The environment variable the runtime reads natively (<c>AddEnvironmentVariables("ORKEON_")</c>).</summary>
    public const string DefaultApiKeyEnv = "ORKEON_Llm__ApiKey";

    /// <summary>Docker Model Runner llama.cpp OpenAI-compatible endpoint.</summary>
    public const string DockerModelRunnerBaseUrl = OrkeonCliDefaults.DockerModelRunner;

    /// <summary>Docker Model Runner default model (parity with <c>examples/appsettings/appsettings.json</c>).</summary>
    public const string DockerModelRunnerDefaultModel = OrkeonCliDefaults.DockerModelRunnerDefaultModel;

    /// <summary>The endpoint needs no auth; the committed template ships this same placeholder.</summary>
    public const string DockerModelRunnerApiKeyPlaceholder = OrkeonCliDefaults.DockerModelRunnerApiKeyPlaceholder;

    /// <summary>
    /// Note written under <c>_comment</c> by the <c>none</c> preset. JSON has no comment
    /// syntax, so the note travels in a harmless extra key.
    /// </summary>
    public const string NoLlmComment =
        "No LLM configured: Orkeon falls back to the <undefined-llm> echo provider. " +
        "Run `orkeon init` again to configure one.";

    /// <summary>Key holding <see cref="NoLlmComment"/>.</summary>
    public const string CommentKey = "_comment";

    /// <summary>Preset names, in the order the wizard lists them.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [Ollama, DockerModelRunner, OpenAI, Custom, None];

    /// <summary>The presets with their labels and defaults, for a drop-down or a wizard.</summary>
    public static IReadOnlyList<LlmPresetInfo> Catalog { get; } =
    [
        new(Ollama, "Ollama", "Local Ollama server.",
            OrkeonCliDefaults.OllamaDefault, OrkeonCliDefaults.OllamaDefaultModel, RequiresApiKey: false),
        new(DockerModelRunner, "Docker Model Runner", "Local llama.cpp engine served by Docker Desktop.",
            DockerModelRunnerBaseUrl, DockerModelRunnerDefaultModel, RequiresApiKey: false),
        new(OpenAI, "OpenAI", "OpenAI cloud API.",
            OrkeonCliDefaults.OpenAI, OrkeonCliDefaults.OpenAIDefaultModel, RequiresApiKey: true),
        new(Custom, "Other OpenAI-compatible", "DeepSeek, GLM, Mistral, … — base URL and model required.",
            null, null, RequiresApiKey: true),
        new(None, "None / offline", "No LLM: runs use the <undefined-llm> echo provider.",
            null, null, RequiresApiKey: false),
    ];

    /// <summary>Returns the catalog entry of a preset, or <see langword="null"/> when unknown.</summary>
    public static LlmPresetInfo? Describe(string? preset) =>
        Catalog.FirstOrDefault(p => string.Equals(p.Name, Normalize(preset), StringComparison.Ordinal));

    /// <summary>
    /// Resolves a preset and the user's overrides into the plan that will be written.
    /// Mirrors the flag path of <c>orkeon init</c>, including its one hard requirement:
    /// <c>custom</c> needs both a base URL and a model.
    /// </summary>
    public static bool TryCreatePlan(
        string? preset,
        LlmPresetOverrides? overrides,
        [NotNullWhen(true)] out LlmPresetPlan? plan,
        out string? error)
    {
        plan = null;
        error = null;

        var name = Normalize(preset);
        var baseUrl = Blank(overrides?.BaseUrl);
        var model = Blank(overrides?.Model);
        var apiKey = Blank(overrides?.ApiKey);
        var apiKeyEnv = Blank(overrides?.ApiKeyEnv);

        switch (name)
        {
            case Ollama:
                plan = new LlmPresetPlan
                {
                    Preset = name,
                    BaseUrl = baseUrl ?? OrkeonCliDefaults.OllamaDefault,
                    Model = model ?? OrkeonCliDefaults.OllamaDefaultModel,
                };
                return true;

            case DockerModelRunner:
                plan = new LlmPresetPlan
                {
                    Preset = name,
                    BaseUrl = baseUrl ?? DockerModelRunnerBaseUrl,
                    Model = model ?? DockerModelRunnerDefaultModel,
                    InlineApiKey = apiKey ?? DockerModelRunnerApiKeyPlaceholder,
                };
                return true;

            case OpenAI:
                plan = new LlmPresetPlan
                {
                    Preset = name,
                    BaseUrl = baseUrl ?? OrkeonCliDefaults.OpenAI,
                    Model = model ?? OrkeonCliDefaults.OpenAIDefaultModel,
                    InlineApiKey = apiKey,
                    ApiKeyEnvName = apiKey is null ? apiKeyEnv ?? DefaultApiKeyEnv : null,
                };
                return true;

            case Custom:
                if (baseUrl is null || model is null)
                {
                    error = "The custom preset requires both a base URL and a model.";
                    return false;
                }

                plan = new LlmPresetPlan
                {
                    Preset = name,
                    BaseUrl = baseUrl,
                    Model = model,
                    InlineApiKey = apiKey,
                    ApiKeyEnvName = apiKey is null ? apiKeyEnv ?? DefaultApiKeyEnv : null,
                };
                return true;

            case None:
                plan = new LlmPresetPlan { Preset = name };
                return true;

            default:
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unknown preset '{preset}'. Supported: {string.Join(", ", Names)}.");
                return false;
        }
    }

    /// <summary>
    /// Builds a fresh document from a plan — byte-for-byte what <c>orkeon init</c>
    /// writes for the same preset.
    /// </summary>
    public static AppSettingsDocument BuildDocument(LlmPresetPlan plan)
    {
        var document = AppSettingsDocument.CreateEmpty();
        Apply(document, plan);
        return document;
    }

    /// <summary>Serializes <see cref="BuildDocument"/> — the text of the generated file.</summary>
    public static string BuildJson(LlmPresetPlan plan) => BuildDocument(plan).ToJson();

    /// <summary>
    /// Applies a plan onto an existing document, touching only the <c>Llm</c> section
    /// (and the <c>_comment</c> note of the <c>none</c> preset). Every other key —
    /// including ones Studio does not model — is left exactly where it was.
    /// </summary>
    public static void Apply(AppSettingsDocument document, LlmPresetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        if (string.Equals(plan.Preset, None, StringComparison.Ordinal))
        {
            document.Llm.Remove();
            document.SetString(CommentKey, NoLlmComment);
            return;
        }

        // Drop our own note (and only ours) when an LLM is configured again.
        if (string.Equals(document.GetString(CommentKey), NoLlmComment, StringComparison.Ordinal))
            document.Remove(CommentKey);

        // Insertion order matches `orkeon init`: Model, BaseUrl, then ApiKey if any.
        document.Llm.Model = plan.Model;
        document.Llm.BaseUrl = plan.BaseUrl;
        document.Llm.ApiKey = plan.InlineApiKey;
    }

    /// <summary>
    /// The guidance <c>orkeon init</c> prints after writing, as messages a UI can show:
    /// how to provide the key, and a warning when it was stored in clear text.
    /// </summary>
    public static IReadOnlyList<string> Guidance(LlmPresetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (string.Equals(plan.Preset, None, StringComparison.Ordinal))
        {
            return
            [
                "No LLM configured: runs will use the <undefined-llm> echo provider. " +
                "Configure one when you are ready.",
            ];
        }

        if (plan.ApiKeyEnvName is { } envName)
        {
            var messages = new List<string>
            {
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"API key: referenced from the environment — set it with: export {envName}=<your-key>"),
            };

            if (!string.Equals(envName, DefaultApiKeyEnv, StringComparison.Ordinal))
            {
                messages.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Note: the Orkeon runtime reads `{DefaultApiKeyEnv}` natively; " +
                    $"`{envName}` is only read by the `orkeon init` / `orkeon llm` probes."));
            }

            return messages;
        }

        if (plan.InlineApiKey is not null
            && !string.Equals(plan.InlineApiKey, DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal))
        {
            return
            [
                "WARNING: the API key is stored in plain text in the generated file. " +
                $"Prefer referencing it from the {DefaultApiKeyEnv} environment variable.",
            ];
        }

        return [];
    }

    private static string Normalize(string? preset) =>
#pragma warning disable CA1308 // lowercase is the canonical preset-name form, not a comparison normalization
        (preset ?? "").Trim().ToLowerInvariant();
#pragma warning restore CA1308

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
