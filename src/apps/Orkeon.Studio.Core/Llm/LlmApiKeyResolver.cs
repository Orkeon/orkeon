using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Resolves the API key a probe of the <c>Llm</c> section should present — the one a run would
/// use. Studio recommends keeping the key out of the file, so a "Test connection" that only ever
/// read the file would fail for exactly the users who followed that advice. The order is the
/// runtime's (STUDIO-49): a key the configuration resolves — the file's <c>ApiKey</c>, then
/// <see cref="LlmPresets.DefaultApiKeyEnv"/> — then the variable <c>Llm:ApiKeyEnvVar</c> names, in
/// the process environment, then in the user's scope (read, never copied).
/// </summary>
public static class LlmApiKeyResolver
{
    /// <summary>
    /// The key over the machine's own environment: the inline key when the file carries one,
    /// else <see cref="LlmPresets.DefaultApiKeyEnv"/>, else the variable
    /// <paramref name="apiKeyEnvVar"/> names. Null when none holds a key.
    /// </summary>
    /// <param name="inlineApiKey">The key held in the settings file, if any.</param>
    /// <param name="apiKeyEnvVar">The file's <c>Llm:ApiKeyEnvVar</c>, if any.</param>
    public static string? Resolve(string? inlineApiKey, string? apiKeyEnvVar = null) =>
        Resolve(inlineApiKey, apiKeyEnvVar, SystemEnvironmentVariables.Instance);

    /// <summary>
    /// Same resolution over an explicit environment, both scopes, so the order can be exercised
    /// without touching the machine.
    /// </summary>
    /// <param name="inlineApiKey">The key held in the settings file, if any.</param>
    /// <param name="apiKeyEnvVar">The file's <c>Llm:ApiKeyEnvVar</c>, if any.</param>
    /// <param name="environment">The process environment and the user scope.</param>
    public static string? Resolve(string? inlineApiKey, string? apiKeyEnvVar, IEnvironmentVariables environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return Resolve(
            inlineApiKey,
            apiKeyEnvVar,
            name => environment.Read(name, EnvironmentVariableTarget.Process),
            name => ReadUserScope(environment, name));
    }

    /// <summary>
    /// Same resolution over a single lookup of the process environment — no user scope — for a
    /// caller whose seam is a function.
    /// </summary>
    /// <param name="inlineApiKey">The key held in the settings file, if any.</param>
    /// <param name="apiKeyEnvVar">The file's <c>Llm:ApiKeyEnvVar</c>, if any.</param>
    /// <param name="environment">Reads an environment variable by name.</param>
    public static string? Resolve(string? inlineApiKey, string? apiKeyEnvVar, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Resolve(inlineApiKey, apiKeyEnvVar, environment, static _ => null);
    }

    private static string? Resolve(
        string? inlineApiKey,
        string? apiKeyEnvVar,
        Func<string, string?> process,
        Func<string, string?> user)
    {
        if (Clean(inlineApiKey) is { } inline)
            return inline;

        if (Clean(process(LlmPresets.DefaultApiKeyEnv)) is { } native)
            return native;

        if (string.IsNullOrWhiteSpace(apiKeyEnvVar))
            return null;

        var name = apiKeyEnvVar.Trim();
        return Clean(process(name)) ?? Clean(user(name));
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "A user scope that cannot be read (registry access denied) is a key not found, exactly " +
                        "like an absent one — as the runtime reads it.")]
    private static string? ReadUserScope(IEnvironmentVariables environment, string name)
    {
        try
        {
            return environment.Read(name, EnvironmentVariableTarget.User);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
