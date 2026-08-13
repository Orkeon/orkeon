using System.Globalization;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// The multi-line spelling of a mount's sub-path rights overrides — one
/// <c>relative/path:rights</c> per line — used by the form fields that edit them as free text.
/// <para>
/// Shared rather than restated per front-end: the separator is the last <c>:</c>, not the
/// first, because a Windows sub-path may carry a drive letter, and an editor that split on
/// the first colon would silently produce a different mount from the one the user typed.
/// </para>
/// </summary>
public static class SubPathOverrideText
{
    /// <summary>The overrides as the form's multi-line field spells them.</summary>
    public static string Format(IEnumerable<SubPathRightsOverride>? overrides) =>
        overrides is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                overrides.Select(item => $"{item.RelativePath}:{MountRightsTokens.ToToken(item.Rights)}"));

    /// <summary>
    /// Reads the field back. Blank lines are ignored; the first line that is not a
    /// <c>path:rights</c> pair fails the whole parse, so a typo cannot be dropped silently.
    /// </summary>
    /// <param name="text">The field's content.</param>
    /// <param name="overrides">The parsed overrides; empty when the parse failed.</param>
    /// <param name="error">Why the parse failed; <see langword="null"/> on success.</param>
    public static bool TryParse(
        string? text,
        out IReadOnlyList<SubPathRightsOverride> overrides,
        out string? error)
    {
        error = null;
        var parsed = new List<SubPathRightsOverride>();

        foreach (var line in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var separator = trimmed.LastIndexOf(':');
            if (separator <= 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{trimmed}' is not a 'relative/path:rights' pair.");
                overrides = [];
                return false;
            }

            var token = trimmed[(separator + 1)..];
            if (!MountRightsTokens.TryParse(token, out var rights))
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{token}' is not one of {string.Join(", ", MountRightsTokens.Tokens)}.");
                overrides = [];
                return false;
            }

            parsed.Add(new SubPathRightsOverride(trimmed[..separator].Trim(), rights));
        }

        overrides = parsed;
        return true;
    }
}
