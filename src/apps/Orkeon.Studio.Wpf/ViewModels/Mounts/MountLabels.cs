using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>
/// How a mount is named on an agent-facing or novice screen: the virtual path the agents
/// address, plus its rights — <c>/output (lecture, écriture)</c> — never the folder on this
/// machine (ADR-008).
/// <para>
/// One implementation, because there were four: the team cards, the Composer's chips, the
/// agent editor's scope line and the team-mounts modal each re-derived it, and the two that
/// were written first fell back to dumping the raw <c>physical:virtual:rights</c> string when
/// the parser refused it — which is exactly the disk path that must not appear.
/// </para>
/// </summary>
internal static class MountLabels
{
    /// <summary>Shown in place of a mount string the parser cannot read.</summary>
    public static string Unreadable(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        return strings[StudioStringKeys.TeamsMountUnreadable];
    }

    /// <summary>
    /// The label for one mount string, and whether it is writable (the pencil / folder icon).
    /// </summary>
    public static (string Label, bool IsReadWrite) Describe(string mountString, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        if (!MountDefinition.TryParse(mountString, out var mount, out _) || mount is null)
            return (Unreadable(strings), false);

        var readWrite = mount.Rights != MountRights.ReadOnly;
        return (
            string.Format(
                CultureInfo.CurrentCulture,
                strings[readWrite ? StudioStringKeys.TeamsMountRw : StudioStringKeys.TeamsMountRo],
                mount.VirtualPath),
            readWrite);
    }

    /// <summary>
    /// The same for a whole list, joined by <paramref name="separator"/>. An empty list reads
    /// as an em dash — the screens that call this always have room for one line.
    /// </summary>
    public static string DescribeAll(
        IReadOnlyList<string> mountStrings, IStudioStrings strings, string separator = " · ")
    {
        ArgumentNullException.ThrowIfNull(mountStrings);
        ArgumentNullException.ThrowIfNull(strings);

        return mountStrings.Count == 0
            ? "—"
            : string.Join(separator, mountStrings.Select(m => Describe(m, strings).Label));
    }
}
