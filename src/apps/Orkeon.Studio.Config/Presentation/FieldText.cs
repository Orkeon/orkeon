using System.Globalization;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// Conversions between the text a terminal field holds and the typed values the
/// <c>appsettings.json</c> sections store. Blank always means "no key": clearing a field
/// removes the setting rather than writing a zero the runtime would then bind. A field loads the
/// value as the file writes it (<c>AppSettingsDocument.GetWritten</c>, STUDIO-55), so a value it
/// cannot read is shown and refused, never loaded empty and erased.
/// </summary>
internal static class FieldText
{
    /// <summary>Renders an optional string; null becomes an empty field.</summary>
    public static string FromString(string? value) => value ?? "";

    /// <summary>
    /// Reads an optional switch typed as text. A blank field yields null with no error;
    /// anything but true / false is reported under <paramref name="fieldName"/>.
    /// </summary>
    public static bool TryReadBoolean(string? text, string fieldName, out bool? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (bool.TryParse(text.Trim(), out var parsed))
        {
            value = parsed;
            return true;
        }

        error = string.Create(CultureInfo.InvariantCulture, $"{fieldName}: '{text.Trim()}' is not true or false.");
        return false;
    }

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

    /// <summary>
    /// Reads a floating-point field, with the same blank-is-null rule. Only a finite number reads
    /// (STUDIO-55): « NaN », « Infinity » and <c>1e400</c>, read as an infinity, are numbers JSON has
    /// no text for, and the run refuses them at start.
    /// </summary>
    public static bool TryReadDouble(string? text, string fieldName, out double? value, out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed))
        {
            value = parsed;
            return true;
        }

        error = string.Create(CultureInfo.InvariantCulture, $"{fieldName}: '{text.Trim()}' is not a number.");
        return false;
    }

    /// <summary>
    /// The text a switch's key holds when it reads as no boolean — <c>"yes"</c>, <c>1</c>, <c>"on"</c> —,
    /// else null: absent, blank — unset to the run — and readable alike (STUDIO-55).
    /// </summary>
    public static string? UnreadableSwitch(AppSettingsDocument document, string path) =>
        document.GetBoolean(path) is null && document.GetWritten(path) is { Length: > 0 } written && !string.IsNullOrWhiteSpace(written)
            ? written
            : null;

    /// <summary>Adds the field error of an unreadable switch's text, if any, as <see cref="TryReadBoolean"/> words it.</summary>
    public static void RefuseUnreadableSwitch(string? asWritten, string path, List<string> errors)
    {
        if (asWritten is null)
            return;

        if (!TryReadBoolean(asWritten, path, out _, out var error))
            errors.Add(error!);
    }
}
