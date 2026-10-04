using System.Globalization;
using Orkeon.Domain.Common;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Teams;

/// <summary>What a team's scheduled run cannot take as Studio shows it (STUDIO-52), in the order a card says it.</summary>
public enum ScheduledRunIssue
{
    /// <summary>The settings file as saved defines no host profile under the id the launchers name: the run is refused.</summary>
    SettingRefused,

    /// <summary>The settings file as saved declares no folder under an id the launchers name: the run is refused.</summary>
    FolderRefused,

    /// <summary>The team's setting is offered to no crew: the scheduled run takes the default.</summary>
    OnDefault,

    /// <summary>The settings file as saved holds another version of the team's setting: the scheduled run takes it.</summary>
    Outdated,
}

/// <summary>One thing a team's scheduled run cannot follow (STUDIO-52).</summary>
/// <param name="Issue">What.</param>
/// <param name="Subject">The setting's name, or the folder's virtual path — never a folder of the disk.</param>
/// <param name="Check">For <see cref="ScheduledRunIssue.OnDefault"/>: why no crew can name the setting.</param>
public sealed record ScheduledRunNotice(ScheduledRunIssue Issue, string Subject, HostProfileCheck? Check = null);

/// <summary>
/// What a team's scheduled run reads, against what Studio shows (STUDIO-52, decisions 2 and 4). The
/// operating system runs the team's launchers, which name the host profile of its setting
/// (<c>--llm-profile</c>) and the declarations of its folders (<c>--mount-id</c>), and the run looks
/// both up in the settings file as it was saved — not in the document the settings screen holds:
/// a profile or a folder id the file does not have refuses the run, an entry the screen changed
/// since runs as it was saved, and a setting offered to no crew is not named at all — the run takes
/// the default. A pure reading: the caller hands the file as saved, read through the screen's store.
/// </summary>
public static class ScheduledRunCheck
{
    /// <summary>
    /// Everything <paramref name="team"/>'s scheduled run cannot follow, in the order a card says it:
    /// a refusal first — the setting, then each folder —, then the default, then an older version.
    /// Empty when the run takes what Studio shows.
    /// </summary>
    /// <param name="team">The team, its folders read against the settings shown — what its launchers name.</param>
    /// <param name="settings">The model settings as Studio shows them.</param>
    /// <param name="saved">The settings file as saved — what the scheduled run reads —; null when there is none.</param>
    public static IReadOnlyList<ScheduledRunNotice> Of(TeamSummary team, ModelProfileSet settings, AppSettingsDocument? saved)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(settings);

        var setting = SettingNotice(TeamSettingStanding.Of(team.Profile, settings), settings, saved);
        var folders = FolderIds(team)
            .Where(folder => saved?.Mounts.Find(folder.Id) is null)
            .Select(folder => new ScheduledRunNotice(ScheduledRunIssue.FolderRefused, folder.VirtualPath))
            .ToList();

        // A refusal first, the setting's before the folders'; then what the run takes instead.
        return setting is { Issue: ScheduledRunIssue.SettingRefused }
            ? [setting, .. folders]
            : [.. folders, .. setting is null ? [] : new[] { setting }];
    }

    /// <summary>What the scheduled run makes of the team's setting; null when it takes it as Studio shows it, or names none.</summary>
    private static ScheduledRunNotice? SettingNotice(TeamSettingStanding standing, ModelProfileSet settings, AppSettingsDocument? saved)
    {
        switch (standing.Kind)
        {
            case TeamSettingKind.NotOffered:
                return new ScheduledRunNotice(ScheduledRunIssue.OnDefault, standing.Name!, standing.Check);

            case TeamSettingKind.Offered:
                // The entry as the mirror writes it (STUDIO-48), the reference to the key's variable
                // included: the run takes the saved one (STUDIO-49). An entry read from the file never
                // carries the placeholder of a key — its key is judged by the one rule (STUDIO-54).
                var expected = settings.Find(standing.Name)!.ToHostEntry(standing.Id!);
                if (saved?.Llm.Profiles.Get(expected.Id) is not { } written)
                    return new ScheduledRunNotice(ScheduledRunIssue.SettingRefused, standing.Name!);

                return written with { Id = expected.Id } == expected with { ApiKeyPlaceholder = null }
                       && saved.Llm.Profiles.KeyAgrees(expected)
                    ? null
                    : new ScheduledRunNotice(ScheduledRunIssue.Outdated, standing.Name!);

            default:
                // Absent from this machine, the setting is said where its name is shown, and the run
                // takes the default as Studio's own launch does; a team naming none has nothing to say.
                return null;
        }
    }

    /// <summary>
    /// The declarations the team's launchers name by id (<c>--mount-id</c>, <see cref="Launch.LaunchMountPlan"/>):
    /// each one once, in the companion file's order, with the folder the agents see.
    /// </summary>
    private static List<(MountId Id, string VirtualPath)> FolderIds(TeamSummary team)
    {
        var ids = new List<(MountId Id, string VirtualPath)>();
        foreach (var mount in team.ResolvedMounts)
        {
            var id = mount.Source switch
            {
                TeamMountSource.Settings => mount.SettingsEntry?.Id,
                TeamMountSource.UnknownId => mount.Id,
                _ => null,
            };
            if (id is null || ids.Any(known => known.Id.Equals(id)))
                continue;

            var virtualPath = mount.SettingsEntry?.VirtualPath
                ?? (MountDefinition.TryParse(mount.Raw, out var parsed, out _) ? parsed.VirtualPath : id.ToString());
            ids.Add((id, virtualPath));
        }

        return ids;
    }

    /// <summary>The first of <see cref="Of"/> — the one line a card shows —; null when the run takes what Studio shows.</summary>
    public static ScheduledRunNotice? First(TeamSummary team, ModelProfileSet settings, AppSettingsDocument? saved) =>
        Of(team, settings, saved) is { Count: > 0 } notices ? notices[0] : null;

    /// <summary>The line <paramref name="notice"/> is in the interface's language.</summary>
    /// <param name="notice">What the scheduled run cannot follow.</param>
    /// <param name="strings">The localization port.</param>
    public static string Describe(ScheduledRunNotice notice, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(strings);

        return notice.Issue switch
        {
            ScheduledRunIssue.SettingRefused => Format(strings[StudioStringKeys.TeamsScheduledRunRefused], notice.Subject),
            ScheduledRunIssue.FolderRefused => Format(strings[StudioStringKeys.TeamsScheduledRunFolderRefused], notice.Subject),
            ScheduledRunIssue.Outdated => Format(strings[StudioStringKeys.TeamsScheduledRunOutdated], notice.Subject),
            _ => Format(
                strings[StudioStringKeys.TeamsScheduledRunOnDefault],
                notice.Subject,
                notice.Check?.Status == HostProfileStatus.NoProvider
                    ? Format(strings[StudioStringKeys.TeamsScheduledRunNoModel], notice.Subject)
                    : HostProfileText.Describe(notice.Check, strings)),
        };
    }

    private static string Format(string pattern, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, pattern, values);
}
