using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Mock;

/// <summary>
/// How a section written <c>Provider: &lt;key&gt;</c> travels from the settings reader to the factory:
/// entries of <see cref="LlmConfig.CustomParameters"/>, so <see cref="LlmConfig"/> itself stays
/// vendor-agnostic. <c>mock</c> carries its responses file as well; any other key names a provider
/// module (<see cref="Orkeon.Application.Interfaces.Ports.ILlmProviderModule"/>).
/// </summary>
internal static class MockProviderSettings
{
    /// <summary>The key of the provider choice in the settings section.</summary>
    internal const string ProviderKey = "Provider";

    /// <summary>The key of the responses file in the settings section.</summary>
    internal const string ResponsesKey = "Responses";

    private const string ProviderParameter = "provider";
    private const string ResponsesParameter = "responses";
    private const string ProfileParameter = "profile";

    /// <summary>
    /// Marks <paramref name="config"/> as the mock provider reading <paramref name="responsesPath"/>,
    /// for the profile named <paramref name="profile"/> (<c>default</c> for the <c>Llm</c> section).
    /// </summary>
    internal static LlmConfig Apply(LlmConfig config, string responsesPath, string profile)
    {
        var parameters = new Dictionary<string, object>(config.CustomParameters)
        {
            [ProviderParameter] = LlmProviderKeys.Mock,
            [ResponsesParameter] = responsesPath,
            [ProfileParameter] = profile,
        };
        return config with { CustomParameters = parameters };
    }

    /// <summary>
    /// Marks <paramref name="config"/> as built by the provider module answering <paramref name="key"/>,
    /// for the profile named <paramref name="profile"/>.
    /// </summary>
    internal static LlmConfig ApplyModule(LlmConfig config, string key, string profile)
    {
        var parameters = new Dictionary<string, object>(config.CustomParameters)
        {
            [ProviderParameter] = key,
            [ProfileParameter] = profile,
        };
        return config with { CustomParameters = parameters };
    }

    /// <summary>
    /// Whether <paramref name="key"/> (lowercase) is one the factory builds itself: a vendor of the
    /// catalogue, one of its aliases, or a generic OpenAI-compatible key. A section never names one
    /// in <c>Provider</c>, and a provider module never answers one.
    /// </summary>
    internal static bool IsBuiltInKey(string key) =>
        LlmProviderKeys.All.Contains(key, StringComparer.Ordinal)
        || LlmProviderKeys.Aliases.ContainsKey(key)
        || key is LlmProviderKeys.Custom or LlmProviderKeys.DockerModelRunner or LlmProviderKeys.AzureShort;

    /// <summary>The provider key the section named, or null when it named none.</summary>
    internal static string? ProviderKeyOf(LlmConfig config) =>
        config.CustomParameters.TryGetValue(ProviderParameter, out var value) && value is string key && key.Length > 0 ? key : null;

    /// <summary>Whether <paramref name="config"/> names the mock provider.</summary>
    internal static bool IsMock(LlmConfig config) =>
        string.Equals(ProviderKeyOf(config), LlmProviderKeys.Mock, StringComparison.Ordinal);

    /// <summary>The name of the profile that chose the provider, or <c>default</c>.</summary>
    internal static string ProfileOf(LlmConfig config) =>
        config.CustomParameters.TryGetValue(ProfileParameter, out var value) && value is string name ? name : "default";

    /// <summary>The virtual path of the responses file, or null.</summary>
    internal static string? ResponsesOf(LlmConfig config) =>
        config.CustomParameters.TryGetValue(ResponsesParameter, out var value) ? value as string : null;
}
