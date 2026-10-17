using Orkeon.Constants.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// The refusal of a setting written as a variable reference that was never expanded: copied as is,
/// the text would go out as the key, the address or the name. One owner for the match, the setting's
/// correction and the phrase, never the value.
/// </summary>
public static class SettingReference
{
    /// <summary>The refusal phrase of <paramref name="value"/> at <paramref name="settingPath"/>, or null when it is no reference or the setting is not on the list.</summary>
    /// <param name="settingPath">The setting's configuration path.</param>
    /// <param name="value">The value as written.</param>
    /// <returns>The phrase, or null.</returns>
    public static string? Phrase(string settingPath, string? value)
    {
        ArgumentNullException.ThrowIfNull(settingPath);
        if (!SettingPlaceholder.TryMatch(value, out var name)
            || SettingPlaceholders.CorrectionFor(settingPath) is not { } correction)
        {
            return null;
        }

        var phrase = OperatorMessages.SettingPlaceholderRefused(settingPath, correction);

        // An Llm ApiKey (Llm:ApiKey, Llm:Profiles:*:ApiKey) has an ApiKeyEnvVar sibling: the correction is
        // written out for the variable it meant. Other ApiKey settings have none, nothing would read it.
        if (settingPath.StartsWith(ConfigurationKeys.LlmSection + ":", StringComparison.OrdinalIgnoreCase)
            && settingPath.EndsWith(":ApiKey", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(name) && !name.Any(c => c == '=' || char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            phrase += $" That is: \"{ConfigurationKeys.LlmApiKeyEnvVar}\": \"{name}\".";
        }

        return phrase;
    }

    /// <summary>Throws the refusal of <paramref name="value"/> at <paramref name="settingPath"/> when it is a reference.</summary>
    /// <param name="settingPath">The setting's configuration path.</param>
    /// <param name="value">The value as written.</param>
    public static void Refuse(string settingPath, string? value)
    {
        if (Phrase(settingPath, value) is { } phrase)
            throw new InvalidOperationException(phrase);
    }
}
