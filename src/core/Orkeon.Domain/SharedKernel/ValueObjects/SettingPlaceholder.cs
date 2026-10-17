using System.Text.RegularExpressions;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Recognises a setting value that is a variable reference.</summary>
public static class SettingPlaceholder
{
    private static readonly Regex Braced = new(
        @"\$\{(?:env:)?(?<name>(?:[A-Za-z_][A-Za-z0-9_]*)?)(?::[-?=+][^}]*)?\}|\$\{[^}]*\}", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Percent = new(
        @"^%(?<name>[A-Za-z_][A-Za-z0-9_()]*)%$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Tries to read a variable reference in <paramref name="value"/>.</summary>
    /// <param name="value">The setting value.</param>
    /// <param name="name">The designated name, or null when the reference is a <c>${prefix:...}</c> form that designates none.</param>
    /// <returns>True when the value is a reference.</returns>
    public static bool TryMatch(string? value, out string? name)
    {
        name = null;
        if (string.IsNullOrEmpty(value))
            return false;
        var match = Braced.Match(value);
        if (!match.Success)
            match = Percent.Match(value.Trim());
        if (!match.Success)
            return false;
        var group = match.Groups["name"];
        name = group.Success ? group.Value : null;
        return true;
    }
}
