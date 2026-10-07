using Orkeon.Constants.Llm;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Profiles;

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
/// <param name="DefaultApiKeyEnv">Conventional environment-variable name holding the key (cloud cards).</param>
/// <param name="Kind">Which group of the editor the card sits in (local, cloud, other, none).</param>
/// <param name="KeyConsoleUrl">Where the provider hands out API keys, shown as plain help text.</param>
/// <param name="RecommendedTimeoutSeconds">
/// The HTTP timeout the profile editor pre-fills when this card is picked, or null to leave the
/// field to the engine's 30 s. Set for the clouds whose default model thinks before it answers
/// (LLM-11): a thinking answer routinely outlasts 30 s, and a run that hits the timeout gets no
/// answer at all.
/// </param>
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
    bool RequiresApiKey,
    string? DefaultApiKeyEnv = null,
    LlmPresetKind Kind = LlmPresetKind.Other,
    string? KeyConsoleUrl = null,
    int? RecommendedTimeoutSeconds = null)
{
    /// <summary>
    /// <see cref="KeyConsoleUrl"/> as a link a UI can open. The catalogue keeps the scheme-less
    /// text the editor shows as help, and every vendor console is served over https — so the
    /// link is that text behind <c>https://</c>, with no second copy to keep in step (STUDIO-33
    /// D-03). Null when the card names no console.
    /// </summary>
    public Uri? KeyConsoleUri =>
        string.IsNullOrWhiteSpace(KeyConsoleUrl)
            ? null
            : new Uri(Uri.UriSchemeHttps + Uri.SchemeDelimiter + KeyConsoleUrl.Trim(), UriKind.Absolute);
}

/// <summary>How the profile editor groups a provider card (design v3, "volets" rev. 2).</summary>
public enum LlmPresetKind
{
    /// <summary>Runs on this machine — free, keyless.</summary>
    Local,

    /// <summary>Cloud API — an API key is required.</summary>
    Cloud,

    /// <summary>The OpenAI-compatible catch-all: URL and model typed by hand.</summary>
    Other,

    /// <summary>No model: runs answer through the echo provider.</summary>
    None,
}

/// <summary>
/// The <c>orkeon init</c> presets, reproduced for Studio: same names, same defaults,
/// same generated JSON, same policy of referencing the API key from
/// <see cref="DefaultApiKeyEnv"/> instead of writing it into the file.
/// </summary>
public static class LlmPresets
{
    /// <summary>Local Ollama server.</summary>
    public const string Ollama = LlmProviderKeys.Ollama;

    /// <summary>Docker Model Runner (llama.cpp, OpenAI-compatible).</summary>
    public const string DockerModelRunner = LlmProviderKeys.DockerModelRunner;

    /// <summary>OpenAI cloud API.</summary>
    public const string OpenAI = LlmProviderKeys.OpenAI;

    /// <summary>Any other OpenAI-compatible endpoint.</summary>
    public const string Custom = LlmProviderKeys.Custom;

    /// <summary>No LLM: the runtime falls back to the echo provider.</summary>
    public const string None = LlmProviderKeys.None;

    /// <summary>The environment variable the runtime reads natively (<c>AddEnvironmentVariables("ORKEON_")</c>).</summary>
    public const string DefaultApiKeyEnv = "ORKEON_Llm__ApiKey";

    /// <summary>
    /// The variable a « Compatible OpenAI » model setting keeps its key in (STUDIO-49), shared by
    /// the settings of that card as two DeepSeek settings share <c>DEEPSEEK_API_KEY</c>. It used to
    /// be <see cref="DefaultApiKeyEnv"/> — the runtime's own key of the <em>default</em>: remembered
    /// in the user scope, the card's key became the default key of every run of the user, whatever
    /// its setting or endpoint. A setting created before keeps its variable; its card says so.
    /// </summary>
    public const string CustomApiKeyEnv = "ORKEON_CUSTOM_LLM_API_KEY";

    /// <summary>
    /// The timeout the profile editor pre-fills for a provider whose default model reasons
    /// before it answers — Kimi (K2.6 thinks unless told not to, K3 always), DeepSeek (V4
    /// thinking by default), Z.AI (the GLM-5 family) and MiniMax (an inline reasoning trace
    /// that is always on). 600 s is the value the repository's own settings templates
    /// carry for these vendors; the engine's 30 s default lost a run on 2026-09-20 to two
    /// Kimi timeouts reported as an empty answer (LLM-11).
    /// </summary>
    public const int ReasoningTimeoutSeconds = 600;

    /// <summary>Docker Model Runner llama.cpp OpenAI-compatible endpoint.</summary>
    public const string DockerModelRunnerBaseUrl = LlmProviderEndpoints.DockerModelRunner;

    /// <summary>Docker Model Runner default model (parity with <c>examples/appsettings/appsettings.json</c>).</summary>
    public const string DockerModelRunnerDefaultModel = LlmProviderDefaultModels.DockerModelRunner;

    /// <summary>The endpoint needs no auth; the committed template ships this same placeholder.</summary>
    public const string DockerModelRunnerApiKeyPlaceholder = LlmProviderDefaultModels.DockerModelRunnerApiKeyPlaceholder;

    /// <summary>
    /// Note written under <c>_comment</c> by the <c>none</c> preset. JSON has no comment
    /// syntax, so the note travels in a harmless extra key.
    /// </summary>
    public const string NoLlmComment =
        "No LLM configured: Orkeon falls back to the <undefined-llm> echo provider. " +
        "Run `orkeon init` again to configure one.";

    /// <summary>Key holding <see cref="NoLlmComment"/>.</summary>
    public const string CommentKey = "_comment";

    // ── Provider ids of the editor catalogue (ProviderCatalogFor) ─────────────────
    // These are LlmProviderFactory keys; the runtime detects the dialect from the URL,
    // so a profile only carries the endpoint and the model.

    /// <summary>Anthropic cloud API.</summary>
    public const string Anthropic = LlmProviderKeys.Anthropic;

    /// <summary>Azure OpenAI (per-resource endpoint — no default URL exists).</summary>
    public const string AzureOpenAI = LlmProviderKeys.AzureOpenAI;

    /// <summary>DeepSeek cloud API (OpenAI-compatible).</summary>
    public const string DeepSeek = LlmProviderKeys.DeepSeek;

    /// <summary>Google Gemini (OpenAI-compatible endpoint).</summary>
    public const string Gemini = LlmProviderKeys.Gemini;

    /// <summary>Canonical key of the Grok (x.AI) provider.</summary>
    public const string Grok = LlmProviderKeys.Grok;

    /// <summary>Canonical key of the MiniMax provider.</summary>
    public const string MiniMax = LlmProviderKeys.MiniMax;

    /// <summary>Canonical key of the OpenRouter aggregator (LLM-09).</summary>
    public const string OpenRouter = LlmProviderKeys.OpenRouter;

    /// <summary>Canonical key of the Mammouth AI aggregator (LLM-09).</summary>
    public const string Mammouth = LlmProviderKeys.Mammouth;

    /// <summary>HuggingFace Inference Providers router.</summary>
    public const string HuggingFace = LlmProviderKeys.HuggingFace;

    /// <summary>Kimi (Moonshot AI) cloud API.</summary>
    public const string Kimi = LlmProviderKeys.Kimi;

    /// <summary>Mistral AI cloud API.</summary>
    public const string Mistral = LlmProviderKeys.Mistral;

    /// <summary>Qwen (Alibaba DashScope) cloud API.</summary>
    public const string Qwen = LlmProviderKeys.Qwen;

    /// <summary>Together AI cloud API.</summary>
    public const string Together = LlmProviderKeys.Together;

    /// <summary>Z.AI (Zhipu GLM) cloud API.</summary>
    public const string Zai = LlmProviderKeys.Zai;

    /// <summary>Preset names, in the order the wizard lists them.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [Ollama, DockerModelRunner, OpenAI, Custom, None];

    /// <summary>The presets with their labels and defaults, for a drop-down or a wizard (English).</summary>
    public static IReadOnlyList<LlmPresetInfo> Catalog { get; } = CatalogFor(EnglishStudioStrings.Instance);

    /// <summary>The catalogue with its labels resolved through a culture port (STUDIO-11).</summary>
    public static IReadOnlyList<LlmPresetInfo> CatalogFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            new(Ollama, strings[StudioStringKeys.PresetOllamaTitle], strings[StudioStringKeys.PresetOllamaDescription],
                LlmProviderEndpoints.OllamaDefault, LlmProviderDefaultModels.Ollama, RequiresApiKey: false),
            new(DockerModelRunner, strings[StudioStringKeys.PresetDmrTitle], strings[StudioStringKeys.PresetDmrDescription],
                DockerModelRunnerBaseUrl, DockerModelRunnerDefaultModel, RequiresApiKey: false),
            new(OpenAI, strings[StudioStringKeys.PresetOpenAITitle], strings[StudioStringKeys.PresetOpenAIDescription],
                LlmProviderEndpoints.OpenAI, LlmProviderDefaultModels.OpenAI, RequiresApiKey: true),
            new(Custom, strings[StudioStringKeys.PresetCustomTitle], strings[StudioStringKeys.PresetCustomDescription],
                null, null, RequiresApiKey: true),
            new(None, strings[StudioStringKeys.PresetNoneTitle], strings[StudioStringKeys.PresetNoneDescription],
                null, null, RequiresApiKey: false),
        ];
    }

    /// <summary>
    /// The provider catalogue of the model-profile editor (design v3, "volets" rev. 2):
    /// the clouds the framework ships a provider for, the two local runtimes, the
    /// OpenAI-compatible catch-all and the echo fallback, grouped by
    /// <see cref="LlmPresetKind"/>. Azure OpenAI has no card by design — its per-resource
    /// endpoint makes it a "Compatible OpenAI" entry. Wider
    /// than <see cref="Catalog"/> on purpose — <see cref="Catalog"/> stays the byte-for-byte
    /// mirror of the five <c>orkeon init</c> presets, while a model profile may point at any
    /// of the runtime's 16 providers (the runtime detects the dialect from the URL, so the
    /// profile only needs the endpoint and the model). Clouds carry the vendor's conventional
    /// key variable in <see cref="LlmPresetInfo.DefaultApiKeyEnv"/>, the catch-all its own
    /// (<see cref="CustomApiKeyEnv"/>) — the key itself never enters a file: Studio stores it in
    /// that user environment variable, names the variable in the settings file
    /// (<c>ApiKeyEnvVar</c>, which a run outside Studio reads) and lays the key over each launch
    /// as <c>ORKEON_Llm__ApiKey</c>.
    /// </summary>
    public static IReadOnlyList<LlmPresetInfo> ProviderCatalogFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return
        [
            new(Ollama, strings[StudioStringKeys.PresetOllamaTitle], strings[StudioStringKeys.PresetOllamaDescription],
                LlmProviderEndpoints.OllamaDefault, LlmProviderDefaultModels.Ollama, RequiresApiKey: false,
                Kind: LlmPresetKind.Local),
            new(DockerModelRunner, strings[StudioStringKeys.PresetDmrTitle], strings[StudioStringKeys.PresetDmrDescription],
                DockerModelRunnerBaseUrl, DockerModelRunnerDefaultModel, RequiresApiKey: false,
                Kind: LlmPresetKind.Local),
            new(OpenAI, strings[StudioStringKeys.PresetOpenAITitle], strings[StudioStringKeys.ProviderOpenAIShortDescription],
                LlmProviderEndpoints.OpenAI, LlmProviderDefaultModels.OpenAI, RequiresApiKey: true,
                "OPENAI_API_KEY", LlmPresetKind.Cloud, "platform.openai.com/api-keys"),
            new(Anthropic, strings[StudioStringKeys.ProviderAnthropicTitle], strings[StudioStringKeys.ProviderAnthropicDescription],
                LlmProviderEndpoints.Anthropic, LlmProviderDefaultModels.Anthropic, RequiresApiKey: true,
                "ANTHROPIC_API_KEY", LlmPresetKind.Cloud, "console.anthropic.com"),
            new(DeepSeek, strings[StudioStringKeys.ProviderDeepSeekTitle], strings[StudioStringKeys.ProviderDeepSeekDescription],
                LlmProviderEndpoints.DeepSeek, LlmProviderDefaultModels.DeepSeek, RequiresApiKey: true,
                "DEEPSEEK_API_KEY", LlmPresetKind.Cloud, "platform.deepseek.com",
                RecommendedTimeoutSeconds: ReasoningTimeoutSeconds),
            new(Mistral, strings[StudioStringKeys.ProviderMistralTitle], strings[StudioStringKeys.ProviderMistralDescription],
                LlmProviderEndpoints.Mistral, LlmProviderDefaultModels.Mistral, RequiresApiKey: true,
                "MISTRAL_API_KEY", LlmPresetKind.Cloud, "console.mistral.ai"),
            new(Gemini, strings[StudioStringKeys.ProviderGeminiTitle], strings[StudioStringKeys.ProviderGeminiDescription],
                LlmProviderEndpoints.Gemini, LlmProviderDefaultModels.Gemini, RequiresApiKey: true,
                "GEMINI_API_KEY", LlmPresetKind.Cloud, "aistudio.google.com/apikey"),
            new(Grok, strings[StudioStringKeys.ProviderGrokTitle], strings[StudioStringKeys.ProviderGrokDescription],
                LlmProviderEndpoints.Grok, LlmProviderDefaultModels.Grok, RequiresApiKey: true,
                "XAI_API_KEY", LlmPresetKind.Cloud, "console.x.ai"),
            new(MiniMax, strings[StudioStringKeys.ProviderMiniMaxTitle], strings[StudioStringKeys.ProviderMiniMaxDescription],
                LlmProviderEndpoints.MiniMax, LlmProviderDefaultModels.MiniMax, RequiresApiKey: true,
                "MINIMAX_API_KEY", LlmPresetKind.Cloud, "platform.minimax.io",
                RecommendedTimeoutSeconds: ReasoningTimeoutSeconds),
            new(Together, strings[StudioStringKeys.ProviderTogetherTitle], strings[StudioStringKeys.ProviderTogetherDescription],
                LlmProviderEndpoints.Together, LlmProviderDefaultModels.Together, RequiresApiKey: true,
                "TOGETHER_API_KEY", LlmPresetKind.Cloud, "api.together.ai"),
            new(Qwen, strings[StudioStringKeys.ProviderQwenTitle], strings[StudioStringKeys.ProviderQwenDescription],
                LlmProviderEndpoints.Qwen, LlmProviderDefaultModels.Qwen, RequiresApiKey: true,
                "DASHSCOPE_API_KEY", LlmPresetKind.Cloud, "dashscope.console.aliyun.com"),
            new(Kimi, strings[StudioStringKeys.ProviderKimiTitle], strings[StudioStringKeys.ProviderKimiDescription],
                LlmProviderEndpoints.Kimi, LlmProviderDefaultModels.Kimi, RequiresApiKey: true,
                "MOONSHOT_API_KEY", LlmPresetKind.Cloud, "platform.moonshot.ai",
                RecommendedTimeoutSeconds: ReasoningTimeoutSeconds),
            new(HuggingFace, strings[StudioStringKeys.ProviderHuggingFaceTitle], strings[StudioStringKeys.ProviderHuggingFaceDescription],
                LlmProviderEndpoints.HuggingFace, LlmProviderDefaultModels.HuggingFace, RequiresApiKey: true,
                "HF_TOKEN", LlmPresetKind.Cloud, "huggingface.co/settings/tokens"),
            new(Zai, strings[StudioStringKeys.ProviderZaiTitle], strings[StudioStringKeys.ProviderZaiDescription],
                LlmProviderEndpoints.Zai, LlmProviderDefaultModels.Zai, RequiresApiKey: true,
                "ZAI_API_KEY", LlmPresetKind.Cloud, "z.ai/manage-apikey",
                RecommendedTimeoutSeconds: ReasoningTimeoutSeconds),
            // LLM-09: the two aggregators. Mammouth's exact API-settings page is to be
            // confirmed with the first key; the vendor documents "from the API settings".
            new(OpenRouter, strings[StudioStringKeys.ProviderOpenRouterTitle], strings[StudioStringKeys.ProviderOpenRouterDescription],
                LlmProviderEndpoints.OpenRouter, LlmProviderDefaultModels.OpenRouter, RequiresApiKey: true,
                "OPENROUTER_API_KEY", LlmPresetKind.Cloud, "openrouter.ai/keys"),
            new(Mammouth, strings[StudioStringKeys.ProviderMammouthTitle], strings[StudioStringKeys.ProviderMammouthDescription],
                LlmProviderEndpoints.Mammouth, LlmProviderDefaultModels.Mammouth, RequiresApiKey: true,
                "MAMMOUTH_API_KEY", LlmPresetKind.Cloud, "mammouth.ai"),
            new(Custom, strings[StudioStringKeys.PresetCustomTitle], strings[StudioStringKeys.ProviderCustomShortDescription],
                null, null, RequiresApiKey: true, CustomApiKeyEnv, LlmPresetKind.Other),
            new(None, strings[StudioStringKeys.PresetNoneTitle], strings[StudioStringKeys.ProviderNoneShortDescription],
                null, null, RequiresApiKey: false, null, LlmPresetKind.None),
        ];
    }

    /// <summary>
    /// The editor's catalogue in English, built once: the names and the English titles
    /// <see cref="CardOf"/> recognises.
    /// </summary>
    private static readonly IReadOnlyList<LlmPresetInfo> EnglishCards = ProviderCatalogFor(EnglishStudioStrings.Instance);

    /// <summary>
    /// The key console of the provider behind <paramref name="baseUrl"/>, as a link: the
    /// <see cref="LlmPresetInfo.KeyConsoleUri"/> of its card in <see cref="ProviderCatalogFor"/>.
    /// Null when no card matches the endpoint, when the card names no console, or when the
    /// endpoint's host is not the card's — Kimi's .cn and MiniMax's mainland twins keep their
    /// accounts on a platform the card's console does not serve.
    /// </summary>
    [SuppressMessage("Design", "CA1054",
        Justification = "The input is the raw 'Llm:BaseUrl' field of a profile, which may be half-typed; " +
                        "an endpoint that does not parse simply has no console.")]
    public static Uri? KeyConsoleFor(string? baseUrl)
    {
        var provider = LlmProviderDetector.Detect(baseUrl);
        var card = ProviderCatalogFor(EnglishStudioStrings.Instance)
            .FirstOrDefault(c => string.Equals(c.Name, provider, StringComparison.Ordinal));

        if (card?.KeyConsoleUri is not { } console
            || !Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var endpoint)
            || !Uri.TryCreate(card.DefaultBaseUrl, UriKind.Absolute, out var home)
            || !string.Equals(endpoint.Host, home.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return console;
    }

    /// <summary>
    /// The card a model setting is on (STUDIO-54): its stable name in
    /// <see cref="ProviderCatalogFor"/> — <c>deepseek</c>, <c>custom</c>, <c>none</c> —, never a title,
    /// which the language of the moment changes. A setting keeps that name
    /// (<see cref="ModelProfile.Provider"/>); any other value is recognised here, in this order: a
    /// card's name, whatever its case; a card's English title — sixteen of the eighteen are brand
    /// names, the same in every language —; the endpoint, through <see cref="LlmProviderDetector"/>.
    /// An endpoint no card carries — Azure OpenAI's among them — is « Other OpenAI-compatible », and
    /// neither an endpoint nor a model is « no model ». A setting therefore always has a card: one
    /// written in another language, or whose provider was blanked, opens on the card its endpoint
    /// says, with that card's key variable.
    /// </summary>
    /// <param name="profile">The setting, as the store holds it.</param>
    public static string CardOf(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Provider?.Trim() is { Length: > 0 } label)
        {
            if (EnglishCards.FirstOrDefault(card => string.Equals(card.Name, label, StringComparison.OrdinalIgnoreCase)) is { } named)
                return named.Name;
            if (EnglishCards.FirstOrDefault(card => string.Equals(card.Title, label, StringComparison.OrdinalIgnoreCase)) is { } titled)
                return titled.Name;
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl))
            return string.IsNullOrWhiteSpace(profile.Model) ? None : Custom;

        var detected = LlmProviderDetector.Detect(profile.BaseUrl);
        return EnglishCards.Any(card => string.Equals(card.Name, detected, StringComparison.Ordinal)) ? detected : Custom;
    }

    /// <summary>
    /// The title of the card named <paramref name="card"/> in the language of
    /// <paramref name="strings"/> — what the profile list, the creation assistant and the status bar
    /// show for a setting, said again when the language switches (STUDIO-54) —, or
    /// <paramref name="card"/> as it is when no card bears that name (a provider key such as
    /// <c>azure-openai</c>); empty for none.
    /// </summary>
    /// <param name="card">A card's name, compared without case.</param>
    /// <param name="strings">The interface's language.</param>
    public static string TitleFor(string? card, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        if (card?.Trim() is not { Length: > 0 } name)
            return card ?? "";

        return ProviderCatalogFor(strings)
            .FirstOrDefault(info => string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))?.Title
            ?? card;
    }

    /// <summary>
    /// Whether a run on <paramref name="baseUrl"/> needs an API key (STUDIO-54): on every endpoint but
    /// Ollama's, as the runtime decides — Ollama's provider alone calls without a key, and every other
    /// answers « API key is required » without one, a local Docker Model Runner included (read as
    /// OpenAI: its card writes a placeholder, <see cref="PlaceholderKeyOf"/>). False for no endpoint, or
    /// one that is no absolute http(s) URL: there is nothing a run could call, and a probe of it says why.
    /// </summary>
    /// <param name="baseUrl">The <c>BaseUrl</c> field, as typed.</param>
    [SuppressMessage("Design", "CA1054",
        Justification = "The input is the raw 'Llm:BaseUrl' field, which may be half-typed; an endpoint that " +
                        "does not parse is one a run cannot call, which this answer must say rather than throw.")]
    public static bool NeedsApiKey(string? baseUrl) =>
        Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var endpoint)
        && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
        && !string.Equals(LlmProviderDetector.Detect(baseUrl), Ollama, StringComparison.Ordinal);

    /// <summary>
    /// The placeholder key a setting on the card named <paramref name="card"/> carries where it holds
    /// no key (STUDIO-54): <see cref="DockerModelRunnerApiKeyPlaceholder"/> for Docker Model Runner —
    /// its server checks no key, but a run reads it as OpenAI, whose dialect refuses to call without
    /// one; <c>orkeon init</c> and the TUI write the same —, null for every other card, Ollama's
    /// included: its provider needs no key. Derived from the card, never stored with the setting.
    /// </summary>
    /// <param name="card">A card's name (<see cref="CardOf"/>).</param>
    public static string? PlaceholderKeyOf(string? card) =>
        string.Equals(card?.Trim(), DockerModelRunner, StringComparison.OrdinalIgnoreCase)
            ? DockerModelRunnerApiKeyPlaceholder
            : null;

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
        out string? error) =>
        TryCreatePlan(preset, overrides, EnglishStudioStrings.Instance, out plan, out error);

    /// <summary>
    /// Same as <see cref="TryCreatePlan(string?, LlmPresetOverrides?, out LlmPresetPlan?, out string?)"/>,
    /// with the error message resolved through a culture port (STUDIO-11).
    /// </summary>
    public static bool TryCreatePlan(
        string? preset,
        LlmPresetOverrides? overrides,
        IStudioStrings strings,
        [NotNullWhen(true)] out LlmPresetPlan? plan,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(strings);

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
                    BaseUrl = baseUrl ?? LlmProviderEndpoints.OllamaDefault,
                    Model = model ?? LlmProviderDefaultModels.Ollama,
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
                    BaseUrl = baseUrl ?? LlmProviderEndpoints.OpenAI,
                    Model = model ?? LlmProviderDefaultModels.OpenAI,
                    InlineApiKey = apiKey,
                    ApiKeyEnvName = apiKey is null ? apiKeyEnv ?? DefaultApiKeyEnv : null,
                };
                return true;

            case Custom:
                if (baseUrl is null || model is null)
                {
                    error = strings[StudioStringKeys.PresetErrorCustomIncomplete];
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
                error = strings.Format(
                    CultureInfo.InvariantCulture,
                    StudioStringKeys.PresetErrorUnknown,
                    preset,
                    string.Join(", ", Names));
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

        // Insertion order matches `orkeon init`: Model, BaseUrl, then the reference to the key's
        // variable or the key itself — never both. A preset without a key removes the reference.
        document.Llm.Model = plan.Model;
        document.Llm.BaseUrl = plan.BaseUrl;
        document.Llm.ApiKeyEnvVar = ReferenceFor(plan.ApiKeyEnvName);
        document.Llm.ApiKey = plan.InlineApiKey;
    }

    /// <summary>
    /// The <c>Llm:ApiKeyEnvVar</c> a plan writes (STUDIO-49), as <c>orkeon init --api-key-env</c>
    /// does: the variable it names, unless that is <see cref="DefaultApiKeyEnv"/>, which the
    /// runtime reads natively — compared as the configuration compares keys — or none.
    /// </summary>
    /// <param name="apiKeyEnvName">The variable the plan keeps the key in, or null.</param>
    public static string? ReferenceFor(string? apiKeyEnvName) =>
        Blank(apiKeyEnvName) is { } name && !string.Equals(name, DefaultApiKeyEnv, StringComparison.OrdinalIgnoreCase)
            ? name
            : null;

    /// <summary>
    /// The guidance <c>orkeon init</c> prints after writing, as messages a UI can show:
    /// how to provide the key, and a warning when it was stored in clear text.
    /// </summary>
    public static IReadOnlyList<string> Guidance(LlmPresetPlan plan) =>
        Guidance(plan, EnglishStudioStrings.Instance);

    /// <summary>
    /// Same as <see cref="Guidance(LlmPresetPlan)"/>, with the messages resolved through a
    /// culture port (STUDIO-11).
    /// </summary>
    public static IReadOnlyList<string> Guidance(LlmPresetPlan plan, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(strings);

        if (string.Equals(plan.Preset, None, StringComparison.Ordinal))
            return [strings[StudioStringKeys.PresetGuidanceNone]];

        if (plan.ApiKeyEnvName is { } envName)
        {
            var messages = new List<string>
            {
                strings.Format(
                    CultureInfo.InvariantCulture,
                    StudioStringKeys.PresetGuidanceApiKeyEnv,
                    envName),
            };

            if (ReferenceFor(envName) is not null)
            {
                messages.Add(strings.Format(
                    CultureInfo.InvariantCulture,
                    StudioStringKeys.PresetGuidanceNonDefaultEnv,
                    envName));
            }

            return messages;
        }

        if (plan.InlineApiKey is not null
            && !string.Equals(plan.InlineApiKey, DockerModelRunnerApiKeyPlaceholder, StringComparison.Ordinal))
        {
            return
            [
                strings.Format(
                    CultureInfo.InvariantCulture,
                    StudioStringKeys.PresetGuidanceInlineKeyWarning,
                    DefaultApiKeyEnv),
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
