using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Llm;

/// <summary>
/// Resolves the API key a probe should present. Studio recommends keeping the key out of the
/// file, in <see cref="LlmPresets.DefaultApiKeyEnv"/> — so a "Test connection" that only ever
/// read the file would fail for exactly the users who followed that advice.
/// </summary>
public static class LlmApiKeyResolver
{
    /// <summary>
    /// The inline key when the file carries one, otherwise the key held in
    /// <see cref="LlmPresets.DefaultApiKeyEnv"/>. Null when neither is set.
    /// </summary>
    public static string? Resolve(string? inlineApiKey) =>
        Resolve(inlineApiKey, Environment.GetEnvironmentVariable);

    /// <summary>
    /// Same resolution over an explicit environment lookup, so the fallback can be exercised
    /// without touching the process environment.
    /// </summary>
    /// <param name="inlineApiKey">The key held in the settings file, if any.</param>
    /// <param name="environment">Reads an environment variable by name.</param>
    public static string? Resolve(string? inlineApiKey, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(inlineApiKey))
            return inlineApiKey.Trim();

        var fromEnvironment = environment(LlmPresets.DefaultApiKeyEnv);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }
}
