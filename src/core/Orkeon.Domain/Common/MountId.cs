using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Orkeon.Domain.Common;

/// <summary>
/// Strongly-typed identifier of one entry of the file-system mount list (VFS-90). It is the
/// <c>&lt;ulid&gt;|</c> prefix of a mount string, what a crew's <c>mounts:</c> block and Studio's
/// team sidecar name a settings entry by — so two entries may share a virtual root and still be
/// told apart, and the association crew ↔ settings no longer rests on the root's name alone.
/// </summary>
public sealed partial class MountId : EntityId<MountId>
{
    /// <summary>The length of the textual form: 26 Crockford base32 characters.</summary>
    public const int Length = 26;

    /// <summary>
    /// Reads an id from its textual form, strictly. <c>Ulid.Parse</c> is lenient — a letter
    /// outside the Crockford alphabet is decoded into a different id, a first character above
    /// <c>7</c> overflows, the all-zero id parses — so a typo would silently name another entry;
    /// the shape is checked first and only a well-formed, non-empty id comes back.
    /// </summary>
    /// <param name="text">The candidate, case-insensitive; the canonical form is upper case.</param>
    /// <param name="id">The id when the text is one.</param>
    public static bool TryParse(string? text, [NotNullWhen(true)] out MountId? id)
    {
        id = null;
        if (text is null)
            return false;

        var trimmed = text.Trim();
        if (!Shape().IsMatch(trimmed))
            return false;

        var value = Ulid.Parse(trimmed, CultureInfo.InvariantCulture);
        if (value == default)
            return false;

        id = From(value);
        return true;
    }

    [GeneratedRegex("^[0-7][0-9A-HJKMNP-TV-Z]{25}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
