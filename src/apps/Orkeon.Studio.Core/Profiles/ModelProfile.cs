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
    public IReadOnlyDictionary<string, string> EnvironmentOverrides()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Model is { Length: > 0 })
            overrides["ORKEON_Llm__Model"] = Model;
        if (BaseUrl is { Length: > 0 })
            overrides["ORKEON_Llm__BaseUrl"] = BaseUrl;
        return overrides;
    }
}
