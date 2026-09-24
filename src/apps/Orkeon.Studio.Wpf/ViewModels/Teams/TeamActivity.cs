using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// What Studio is doing with a team folder right now (STUDIO-28, D-02 — shared with STUDIO-31,
/// D-09). A gesture that moves or hides the folder — rename, archive, restore — is refused while
/// the answer is anything but <see cref="None"/>. A run started outside Studio (the CLI, the
/// operating system's scheduler) is invisible here: the engine's own undo protects those.
/// </summary>
public enum TeamActivity
{
    /// <summary>Nothing: the folder can move.</summary>
    None,

    /// <summary>The run in flight on the Launch screen targets the team.</summary>
    Running,

    /// <summary>The run in flight on the Test screen targets the team.</summary>
    Testing,

    /// <summary>The wizard has the team open — « Modify » reopened it, and its re-adoption writes back into this folder.</summary>
    OpenInWizard,
}

/// <summary>Reads a <see cref="TeamActivity"/> off what the two launchers and the wizard hold.</summary>
public static class TeamActivities
{
    /// <summary>
    /// What Studio is doing with <paramref name="teamPath"/>. A run target is the team's when it
    /// is the folder itself or anything under it — the launcher takes a crew file as readily as
    /// the folder; the wizard holds the team when it reopened that very folder. A run answers
    /// first: it is the one reading the folder this instant.
    /// </summary>
    /// <param name="teamPath">The team folder a gesture is about to move or hide.</param>
    /// <param name="runningTarget">The target of the Launch screen's run in flight; null while none runs.</param>
    /// <param name="testingTarget">The target of the Test screen's run in flight; null while none runs.</param>
    /// <param name="openInWizard">The team folder the wizard reopened; null while it has none.</param>
    public static TeamActivity Of(string teamPath, string? runningTarget, string? testingTarget, string? openInWizard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamPath);

        var team = TeamCatalog.NormalizePath(teamPath);
        if (Holds(runningTarget, team))
            return TeamActivity.Running;
        if (Holds(testingTarget, team))
            return TeamActivity.Testing;
        return Holds(openInWizard, team) ? TeamActivity.OpenInWizard : TeamActivity.None;
    }

    // The one containment predicate: ~/teams/veille-2 is not inside ~/teams/veille.
    private static bool Holds(string? path, string team) =>
        path is { Length: > 0 } && PhysicalPathContainment.IsUnder(TeamCatalog.NormalizePath(path), team);
}
