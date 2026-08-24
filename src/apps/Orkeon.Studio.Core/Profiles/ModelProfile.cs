using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>
/// A named model setting ("réglage de modèle", design v3): a provider, a model and its
/// endpoint, reusable by several teams and by Studio's own assistant. Profiles are Studio
/// state — the CLI never reads them; the one marked default is <em>written into</em> the
/// <c>Llm</c> section of <c>appsettings.json</c>, and a team that chose another profile gets
/// it as environment overrides on its own run. The API key is deliberately absent: it stays
/// in the environment, never in this file.
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
    /// <c>ORKEON_Llm__ApiKey</c>; the store file only ever carries this name. (Named without
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
    public IReadOnlyDictionary<string, string> EnvironmentOverrides(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Model is { Length: > 0 })
            overrides["ORKEON_Llm__Model"] = Model;
        if (BaseUrl is { Length: > 0 })
            overrides["ORKEON_Llm__BaseUrl"] = BaseUrl;
        if (Temperature is { } temperature)
            overrides["ORKEON_Llm__Temperature"] = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (TimeoutSeconds is { } timeout and > 0)
            overrides["ORKEON_Llm__TimeoutSeconds"] = timeout.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (KeyEnvName is { Length: > 0 } name
            && environment(name) is { } key
            && !string.IsNullOrWhiteSpace(key))
        {
            overrides["ORKEON_Llm__ApiKey"] = key.Trim();
        }
        return overrides;
    }
}
