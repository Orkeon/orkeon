using System.Globalization;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Profiles;

/// <summary>What the model setting a team names is to a run outside Studio (STUDIO-52).</summary>
public enum TeamSettingKind
{
    /// <summary>The team names no setting: every run takes the default, from Studio as outside it.</summary>
    None,

    /// <summary>The setting is the host profile <see cref="TeamSettingStanding.Id"/>: the team's launchers name it.</summary>
    Offered,

    /// <summary>
    /// The setting is offered to no crew (<see cref="TeamSettingStanding.Check"/> says why): Studio
    /// launches the team on it, a run outside Studio — a scheduled one — takes the default.
    /// </summary>
    NotOffered,

    /// <summary>
    /// No setting of this machine bears the name — removed, renamed by hand, or named by a team that
    /// came from another machine: the default runs in its place, from Studio as outside it.
    /// </summary>
    Missing,
}

/// <summary>
/// The standing of the model setting a team's companion file names (STUDIO-52, decision 2): the one
/// reading the team card, the schedule offer, the Run screen and the launchers share. A setting is
/// found by its name, ordinal — the lookup every launch makes (<see cref="ModelProfileSet.Find"/>) —,
/// and offered under the id <see cref="HostLlmProfiles.Classify"/> gives it.
/// </summary>
public sealed record TeamSettingStanding
{
    /// <summary>A team that names no setting.</summary>
    public static TeamSettingStanding None { get; } = new() { Kind = TeamSettingKind.None };

    /// <summary>What the setting is to a run outside Studio.</summary>
    public required TeamSettingKind Kind { get; init; }

    /// <summary>The setting's name, as the companion file writes it; null for <see cref="TeamSettingKind.None"/>.</summary>
    public string? Name { get; init; }

    /// <summary>The host profile id the launchers name — <c>--llm-profile</c> —, for <see cref="TeamSettingKind.Offered"/> only.</summary>
    public string? Id { get; init; }

    /// <summary>The setting's standing as a host profile; null for <see cref="TeamSettingKind.None"/> and <see cref="TeamSettingKind.Missing"/>.</summary>
    public HostProfileCheck? Check { get; init; }

    /// <summary>
    /// The setting as a team card and the Run screen name it, in the interface's language: its name —
    /// and, absent from this machine, that the default runs in its place, the same words on both
    /// screens. Null for a team that names no setting.
    /// </summary>
    /// <param name="strings">The localization port.</param>
    public string? Label(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return Kind == TeamSettingKind.Missing
            ? string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.TeamsSettingMissing], Name)
            : Name;
    }

    /// <summary>The standing of <paramref name="setting"/> among <paramref name="settings"/>.</summary>
    /// <param name="setting">The setting the team's companion file names; null or empty when it names none.</param>
    /// <param name="settings">The model settings of this machine.</param>
    public static TeamSettingStanding Of(string? setting, ModelProfileSet settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (setting is not { Length: > 0 })
            return None;

        // The classification of every setting, by name: the first of two settings under one name is
        // the one every lookup finds, and the first to answer to an id owns it (STUDIO-48).
        if (!HostLlmProfiles.Classify(settings).TryGetValue(setting, out var check))
            return new TeamSettingStanding { Kind = TeamSettingKind.Missing, Name = setting };

        return check.IsOffered
            ? new TeamSettingStanding { Kind = TeamSettingKind.Offered, Name = setting, Id = check.Id, Check = check }
            : new TeamSettingStanding { Kind = TeamSettingKind.NotOffered, Name = setting, Check = check };
    }
}
