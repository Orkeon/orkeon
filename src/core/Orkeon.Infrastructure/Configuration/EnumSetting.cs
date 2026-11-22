namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Reads an enumeration a setting writes, by its name only (#455). <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
/// also takes a number, which reads as whichever member holds it — <c>"1"</c> as the second one — and
/// a comma-separated list, as a combination of flags: neither is a name a file means.
/// </summary>
internal static class EnumSetting
{
    /// <summary>
    /// The member <paramref name="value"/> names, any case, surrounding blanks ignored; false for a
    /// number, a list or a name the enumeration does not hold.
    /// </summary>
    /// <param name="value">The value as the configuration holds it.</param>
    /// <param name="parsed">The member named, or the default when none is.</param>
    /// <typeparam name="TEnum">The enumeration.</typeparam>
    public static bool TryParseName<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var name = value?.Trim();
        foreach (var member in Enum.GetNames<TEnum>())
        {
            if (string.Equals(member, name, StringComparison.OrdinalIgnoreCase))
            {
                parsed = Enum.Parse<TEnum>(member);
                return true;
            }
        }

        parsed = default;
        return false;
    }
}
