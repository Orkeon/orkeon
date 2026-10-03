using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>
/// A named model setting, as the design v3 settings screen calls it: a provider, a model and its
/// endpoint, reusable by several teams and by Studio's own assistant. Profiles are Studio
/// state — the CLI never reads this record; the one marked default is <em>written into</em> the
/// <c>Llm</c> section of <c>appsettings.json</c>, a team that chose another profile gets it as
/// environment overrides on its own run, and every profile that names a provider is the host
/// profile <c>Llm:Profiles:&lt;<see cref="HostProfileId"/>&gt;</c> a crew picks per agent or per task
/// (STUDIO-48, through <see cref="HostLlmProfiles"/>). The API key is deliberately absent: it
/// stays in the environment, never in this file nor in the settings file.
/// </summary>
public sealed record ModelProfile
{
    /// <summary>User-chosen display name — also the identity teams reference.</summary>
    public required string Name { get; init; }

    /// <summary>Provider label, from the preset catalogue ("Ollama", "OpenAI", …).</summary>
    public string? Provider { get; init; }

    /// <summary>Model identifier, as the endpoint expects it.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Name of the environment variable holding the API key — never the key itself. Studio
    /// resolves it at probe and launch time and lays the value over the child process as
    /// <c>ORKEON_Llm__ApiKey</c> — and as <c>ORKEON_Llm__Profiles__&lt;id&gt;__ApiKey</c> for the
    /// host profile (STUDIO-48); the store file only ever carries this name. (Named without
    /// the "ApiKey" substring on purpose: the store round-trip test forbids it, as a tripwire
    /// against a literal key ever landing in the file.)
    /// </summary>
    public string? KeyEnvName { get; init; }

    /// <summary>
    /// Sampling temperature this profile pins, or null to leave the engine's default.
    /// Some vendors mandate a value per model (Moonshot's K3 family accepts only 1) —
    /// pinning it here makes the first request right instead of relying on the
    /// provider's one-shot adaptive retry.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// HTTP timeout in seconds this profile pins, or null for the engine's default (30 s).
    /// Reasoning models (Kimi K3, thinking modes) routinely take longer than the default
    /// to produce their first byte — pin a larger value here.
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Maximum response length in tokens this profile pins, or null to leave the cap to the
    /// engine: the model's documented maximum, or 4096 for a model the catalogue does not
    /// know (LLM-10). Before that, the engine sent 4096 for every model — a budget a reasoning
    /// model spends thinking before it writes a word, which came back as an empty answer
    /// (STUDIO-12 C5b, owner recette 2026-09-19).
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// The thinking switch this profile pins: true forces the reasoning pass on, false turns
    /// it off, null leaves the provider's default — which, on Kimi K2.6, DeepSeek V4 and GLM,
    /// is on. Travels as <c>ORKEON_Llm__Thinking__Enabled</c>; the engine translates it into
    /// each provider's dialect and warns, never drops, when a provider has no switch. The
    /// knob existed in the crew YAML and the settings file but had no place in Studio, so the
    /// run of 2026-09-20 had nowhere to turn it off (LLM-11).
    /// </summary>
    public bool? ThinkingEnabled { get; init; }

    /// <summary>
    /// The reasoning-effort hint this profile pins (<c>low</c> / <c>medium</c> / <c>high</c>,
    /// <c>max</c> where the model offers it), or null for the provider's default. Travels as
    /// <c>ORKEON_Llm__Thinking__Effort</c>.
    /// </summary>
    public string? ThinkingEffort { get; init; }

    /// <summary>Endpoint base URL.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "User-typed form value round-tripped verbatim into a JSON string field; " +
                        "a half-typed URL must be carried and re-shown, which System.Uri cannot do.")]
    public string? BaseUrl { get; init; }

    /// <summary>One-line summary for cards and pickers: "Ollama · qwen2.5:14b".</summary>
    [JsonIgnore]
    public string Summary =>
        (Provider, Model) switch
        {
            ({ Length: > 0 } provider, { Length: > 0 } model) => $"{provider} · {model}",
            ({ Length: > 0 } provider, _) => provider,
            (_, { Length: > 0 } model) => model,
            _ => "",
        };

    /// <summary>
    /// The name a crew writes to run on this setting — <c>llm: { profile: &lt;id&gt; }</c> — and
    /// the key of its entry under <c>Llm:Profiles</c> (STUDIO-48): the name through the folder-name
    /// rule the teams follow (<see cref="FolderSlug"/>, STUDIO-24), so « Claude » is <c>claude</c>
    /// and « Z.AI » is <c>z-ai</c>. Null when the name keeps no ASCII letter or digit. Derived,
    /// never stored: the store carries the name, and the id follows a rename.
    /// </summary>
    [JsonIgnore]
    public string? HostProfileId => HostProfileIdOf(Name);

    /// <summary>
    /// Whether the setting names a provider at all — an endpoint or a model. The « no model »
    /// card names neither: its runs answer as an echo, which no host profile can be, so such a
    /// setting is offered to no crew.
    /// </summary>
    [JsonIgnore]
    public bool DescribesProvider => !string.IsNullOrWhiteSpace(Model) || !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>The host profile id a setting named <paramref name="name"/> answers to; null when there is none.</summary>
    public static string? HostProfileIdOf(string? name) => FolderSlug.From(name);

    /// <summary>
    /// The <c>Llm:Profiles</c> entry this setting becomes under <paramref name="id"/>: what it
    /// pins, as the <c>Llm</c> section spells it — never the key.
    /// </summary>
    public LlmProfileEntry ToHostEntry(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return new LlmProfileEntry
        {
            Id = id,
            BaseUrl = BaseUrl is { Length: > 0 } ? BaseUrl : null,
            Model = Model is { Length: > 0 } ? Model : null,
            Temperature = Temperature,
            TimeoutSeconds = TimeoutSeconds is > 0 ? TimeoutSeconds : null,
            MaxTokens = MaxTokens is > 0 ? MaxTokens : null,
            ThinkingEnabled = ThinkingEnabled,
            ThinkingEffort = string.IsNullOrWhiteSpace(ThinkingEffort) ? null : ThinkingEffort.Trim(),
        };
    }

    /// <summary>
    /// The environment overrides that make a child <c>orkeon run</c> use this profile instead
    /// of the settings file's <c>Llm</c> section — the standard .NET configuration variables,
    /// which the CLI's host already binds. Empty values are simply not overridden.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentOverrides() =>
        EnvironmentOverrides(static _ => null);

    /// <summary>
    /// Same overrides, plus <c>ORKEON_Llm__ApiKey</c> resolved from <see cref="KeyEnvName"/>
    /// through <paramref name="environment"/> when the profile names a key variable and the
    /// variable holds a value. The key transits only into the child process environment —
    /// never into a file.
    /// </summary>
    /// <param name="environment">Reads an environment variable by name.</param>
    public IReadOnlyDictionary<string, string> EnvironmentOverrides(Func<string, string?> environment) =>
        Overrides(DefaultSectionPrefix, environment);

    /// <summary>
    /// The same overrides for this setting as the host profile <paramref name="id"/>:
    /// <c>ORKEON_Llm__Profiles__&lt;id&gt;__*</c>, the key — <c>…__ApiKey</c> — resolved through
    /// <paramref name="environment"/> (STUDIO-48). A launch lays them over its child so a crew
    /// naming the profile runs on what the screen shows, whatever the settings file it reads.
    /// </summary>
    /// <param name="id">The host profile id, <see cref="HostProfileId"/> as offered.</param>
    /// <param name="environment">Reads an environment variable by name.</param>
    public IReadOnlyDictionary<string, string> HostEnvironment(string id, Func<string, string?> environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Overrides(HostProfilePrefix(id), environment);
    }

    /// <summary>
    /// The environment variable a terminal <c>orkeon run</c> reads the key of host profile
    /// <paramref name="id"/> from: the settings file never holds it.
    /// </summary>
    public static string HostKeyVariable(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return HostProfilePrefix(id) + "ApiKey";
    }

    private const string DefaultSectionPrefix = "ORKEON_Llm__";

    private static string HostProfilePrefix(string id) => $"{DefaultSectionPrefix}Profiles__{id.Trim()}__";

    private Dictionary<string, string> Overrides(string prefix, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Model is { Length: > 0 })
            overrides[prefix + "Model"] = Model;
        if (BaseUrl is { Length: > 0 })
            overrides[prefix + "BaseUrl"] = BaseUrl;
        if (Temperature is { } temperature)
            overrides[prefix + "Temperature"] = temperature.ToString(CultureInfo.InvariantCulture);
        if (TimeoutSeconds is { } timeout and > 0)
            overrides[prefix + "TimeoutSeconds"] = timeout.ToString(CultureInfo.InvariantCulture);
        if (MaxTokens is { } maxTokens and > 0)
            overrides[prefix + "MaxTokens"] = maxTokens.ToString(CultureInfo.InvariantCulture);
        if (ThinkingEnabled is { } thinking)
            overrides[prefix + "Thinking__Enabled"] = thinking ? "true" : "false";
        if (ThinkingEffort is { } effort && !string.IsNullOrWhiteSpace(effort))
            overrides[prefix + "Thinking__Effort"] = effort.Trim();
        if (KeyEnvName is { Length: > 0 } name
            && environment(name) is { } key
            && !string.IsNullOrWhiteSpace(key))
        {
            overrides[prefix + "ApiKey"] = key.Trim();
        }
        return overrides;
    }
}
