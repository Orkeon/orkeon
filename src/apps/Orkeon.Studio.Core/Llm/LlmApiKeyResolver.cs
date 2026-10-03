using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Resolves the API key a probe of the <c>Llm</c> section should present — the one a run presents,
/// read in the run's order (STUDIO-54). Studio recommends keeping the key out of the file, so a
/// "Test connection" that only ever read the file would fail for exactly the users who followed
/// that advice. First the key the configuration resolves, its layers composed as every runner
/// composes them (GAP-36): <see cref="LlmPresets.DefaultApiKeyEnv"/> — the <c>ORKEON_</c> variables,
/// which win —, then the file's <c>ApiKey</c>, then <c>Llm__ApiKey</c>, the unprefixed variables
/// beneath the file. A layer that sets the key hides those under it, and a blank value reads as
/// absent. Only then the variable <c>Llm:ApiKeyEnvVar</c> names — that reference composed the same
/// way —, in the process environment, then in the user's scope (read, never copied; STUDIO-49).
/// </summary>
public static class LlmApiKeyResolver
{
    /// <summary>The default's key in the unprefixed variables — the lowest layer.</summary>
    private const string UnprefixedApiKey = "Llm__ApiKey";

    /// <summary>The reference in the <c>ORKEON_</c> variables, over the file's.</summary>
    private const string OrkeonApiKeyEnvVar = "ORKEON_Llm__ApiKeyEnvVar";

    /// <summary>The reference in the unprefixed variables, beneath the file's.</summary>
    private const string UnprefixedApiKeyEnvVar = "Llm__ApiKeyEnvVar";

    /// <summary>
    /// The key over the machine's own environment, both scopes, in the run's order: the
    /// configuration's key — <see cref="LlmPresets.DefaultApiKeyEnv"/>, else
    /// <paramref name="inlineApiKey"/>, else <c>Llm__ApiKey</c> — then the variable the reference
    /// names. Null when none holds a key.
    /// </summary>
    /// <param name="inlineApiKey">The key held in the settings file, if any.</param>
    /// <param name="apiKeyEnvVar">The file's <c>Llm:ApiKeyEnvVar</c>, if any.</param>
    public static string? Resolve(string? inlineApiKey, string? apiKeyEnvVar = null) =>
        Resolve(inlineApiKey, apiKeyEnvVar, SystemEnvironmentVariables.Instance);

    /// <summary>
    /// Same resolution over an explicit environment, both scopes — the variables of the configuration
    /// read in its process block, the referenced one there and then in the user scope —, so the order
    /// can be exercised without touching the machine.
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
        if (Clean(Composed(process(LlmPresets.DefaultApiKeyEnv), inlineApiKey, process(UnprefixedApiKey))) is { } configured)
            return configured;

        if (Clean(Composed(process(OrkeonApiKeyEnvVar), apiKeyEnvVar, process(UnprefixedApiKeyEnvVar))) is not { } name)
            return null;

        return Clean(process(name)) ?? Clean(user(name));
    }

    /// <summary>
    /// A key of the <c>Llm</c> section as the configuration composes it: the <c>ORKEON_</c> variable
    /// when it is set — blank or not, it hides the layers under it —, else the file's value, else the
    /// unprefixed variable. A blank field of the file is no value: the form removes it when it saves.
    /// </summary>
    private static string? Composed(string? orkeon, string? file, string? unprefixed) =>
        orkeon ?? (string.IsNullOrWhiteSpace(file) ? null : file) ?? unprefixed;

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
