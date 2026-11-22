using System.Text.RegularExpressions;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>
/// Where the key of a System One provider is read, in one place: the <c>ApiKey</c> setting, then the variable
/// <c>ApiKeyEnvVar</c> names (the provider's own by default) in the process environment, then in the user's
/// persistent scope. <see cref="SystemOneDecisionModel"/> reads it at each call, and the host's decision
/// circuit asks that same model when it is resolved (#451): one environment (the container's
/// <see cref="LlmKeyEnvironment"/>, else the machine's) and one predicate (the provider's
/// <c>KeyRequired</c>), so the two cannot disagree on whether a key is missing.
/// </summary>
internal static partial class DecisionApiKey
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
        $"no {provider.DisplayName} API key: {Remedy(provider, options)}";

    /// <summary>
    /// The remedy after <c>API key: </c>: the variable to set and the setting, or, when <c>ApiKeyEnvVar</c> does
    /// not hold an environment variable name (a key pasted there by mistake), the setting path alone. The value
    /// itself is never repeated: the sentence lands in <c>run.decision</c>, the forge's events and stderr (#467).
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">Its bound options.</param>
    /// <returns>The remedy, without a capital or a full stop.</returns>
    public static string Remedy(DecisionProviderDescriptor provider, SystemOneProviderOptions options)
    {
        var variable = Variable(provider, options);
        return VariableName().IsMatch(variable)
            ? $"set the environment variable {variable}, or {provider.Section}:ApiKey"
            : $"{provider.Section}:ApiKeyEnvVar does not hold an environment variable name; set it to one, or set {provider.Section}:ApiKey";
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VariableName();
}
