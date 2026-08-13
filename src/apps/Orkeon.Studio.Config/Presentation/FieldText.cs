using System.Globalization;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// Conversions between the text a terminal field holds and the typed values the
/// <c>appsettings.json</c> sections store. Blank always means "no key": clearing a field
/// removes the setting rather than writing a zero the runtime would then bind.
/// </summary>
internal static class FieldText
{
    /// <summary>Renders an optional integer; null becomes an empty field.</summary>
    public static string FromInt32(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>Renders an optional floating-point value; null becomes an empty field.</summary>
    public static string FromDouble(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>Renders an optional string; null becomes an empty field.</summary>
    public static string FromString(string? value) => value ?? "";

    /// <summary>Trims a field; a blank field reads back as null.</summary>
    public static string? ToStringOrNull(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Reads an integer field. A blank field yields null with no error; anything else that
    /// is not an integer is reported under <paramref name="fieldName"/>.
    /// </summary>
    public static bool TryReadInt32(string? text, string fieldName, out int? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        error = string.Create(CultureInfo.InvariantCulture, $"{fieldName}: '{text.Trim()}' is not a whole number.");
        return false;
    }

    /// <summary>Reads a floating-point field, with the same blank-is-null rule.</summary>
    public static bool TryReadDouble(string? text, string fieldName, out double? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        error = string.Create(CultureInfo.InvariantCulture, $"{fieldName}: '{text.Trim()}' is not a number.");
        return false;
    }
}
