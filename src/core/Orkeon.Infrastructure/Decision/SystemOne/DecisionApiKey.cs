using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>
/// Where the key of a System One provider is read, in one place: the <c>ApiKey</c> setting, then the variable
/// <c>ApiKeyEnvVar</c> names (the provider's own by default) in the process environment, then in the user's
/// persistent scope. <see cref="SystemOneDecisionModel"/> reads it at each call, and the host's decision
/// circuit when it is resolved (#451), so the two cannot disagree on whether a key exists.
/// </summary>
internal static class DecisionApiKey
{
    /// <summary>The environment variable the key is read from.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">Its bound options.</param>
    /// <returns>The variable's name.</returns>
    public static string Variable(DecisionProviderDescriptor provider, SystemOneProviderOptions options) =>
        string.IsNullOrWhiteSpace(options.ApiKeyEnvVar) ? provider.DefaultApiKeyVariable : options.ApiKeyEnvVar.Trim();

    /// <summary>The key, trimmed; null when neither the setting nor the variable holds one.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">Its bound options.</param>
    /// <param name="environment">Where the variable is read.</param>
    /// <returns>The key, or null.</returns>
    public static string? Resolve(DecisionProviderDescriptor provider, SystemOneProviderOptions options, LlmKeyEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            return options.ApiKey.Trim();
        var variable = Variable(provider, options);
        var key = environment.Process(variable);
        if (string.IsNullOrWhiteSpace(key))
        {
            try
            {
                key = environment.User(variable);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or PlatformNotSupportedException or IOException)
            {
                key = null;
            }
        }

        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    /// <summary>What is missing, as an error and the circuit say it: <c>no Liquid API key: set the environment variable LIQUID_API_KEY, or Orkeon:Liquid:ApiKey</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">Its bound options.</param>
    /// <returns>The sentence, without a capital or a full stop.</returns>
    public static string Missing(DecisionProviderDescriptor provider, SystemOneProviderOptions options) =>
        $"no {provider.DisplayName} API key: set the environment variable {Variable(provider, options)}, or {provider.Section}:ApiKey";
}
