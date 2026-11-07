using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Mock;

/// <summary>
/// How a profile written <c>Provider: mock</c> travels from the settings reader to the factory: two
/// entries of <see cref="LlmConfig.CustomParameters"/>, so <see cref="LlmConfig"/> itself stays
/// vendor-agnostic.
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

    /// <summary>Whether <paramref name="config"/> names the mock provider.</summary>
    internal static bool IsMock(LlmConfig config) =>
        config.CustomParameters.TryGetValue(ProviderParameter, out var value)
        && string.Equals(value as string, LlmProviderKeys.Mock, StringComparison.Ordinal);

    /// <summary>The name of the profile that chose the mock, or <c>default</c>.</summary>
    internal static string ProfileOf(LlmConfig config) =>
        config.CustomParameters.TryGetValue(ProfileParameter, out var value) && value is string name ? name : "default";

    /// <summary>The virtual path of the responses file, or null.</summary>
    internal static string? ResponsesOf(LlmConfig config) =>
        config.CustomParameters.TryGetValue(ResponsesParameter, out var value) ? value as string : null;
}
