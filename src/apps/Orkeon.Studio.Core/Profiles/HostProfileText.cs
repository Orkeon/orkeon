using System.Globalization;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>
/// Words the standing of a model setting as a host profile (STUDIO-48) in the interface's
/// language: the name a crew writes, or why no crew can write one. One wording for the profile
/// cards and the editor.
/// </summary>
public static class HostProfileText
{
    /// <summary>
    /// The line under a setting's name; null when there is nothing to say — no standing, or a
    /// setting without a model, whose card already says that its runs answer as an echo.
    /// </summary>
    /// <param name="check">The standing, or null.</param>
    /// <param name="strings">The localization port.</param>
    public static string? Describe(HostProfileCheck? check, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return check?.Status switch
        {
            HostProfileStatus.Offered => Format(strings[StudioStringKeys.ProfileHostId], check.Id),
            HostProfileStatus.NoId => strings[StudioStringKeys.ProfileHostIdNone],
            HostProfileStatus.DefaultName => strings[StudioStringKeys.ProfileHostIdReserved],
            HostProfileStatus.TakenBySetting => Format(strings[StudioStringKeys.ProfileHostIdTakenBySetting], check.Id, check.TakenBy),
            HostProfileStatus.TakenByFile => Format(strings[StudioStringKeys.ProfileHostIdTakenByFile], check.Id),
            _ => null,
        };
    }

    /// <summary>Whether the line is a problem to show in the warning tone rather than the name to write.</summary>
    public static bool IsIssue(HostProfileCheck? check) =>
        check is { IsOffered: false, Status: not HostProfileStatus.NoProvider };

    private static string Format(string pattern, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, pattern, values);
}
