using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// Which teams name which settings entry (VFS-90): the Settings › Authorized folders screen
/// says "used by …" on a row and asks before removing an entry a team depends on.
/// </summary>
public static class TeamMountReferences
{
    /// <summary>
    /// The teams referencing each mount id, from the raw sidecar entries — the id is what a
    /// sidecar records, whether or not this machine still declares it.
    /// </summary>
    public static IReadOnlyDictionary<MountId, IReadOnlyList<TeamSummary>> ByMountId(IEnumerable<TeamSummary> teams)
    {
        ArgumentNullException.ThrowIfNull(teams);

        var references = new Dictionary<MountId, List<TeamSummary>>();
        foreach (var team in teams)
        {
            foreach (var raw in team.Metadata?.Mounts ?? [])
            {
                if (FileSystemMount.TryGetId(raw) is not { } id)
                    continue;

                if (!references.TryGetValue(id, out var list))
                    references[id] = list = [];
                if (!list.Contains(team))
                    list.Add(team);
            }
        }

        return references.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TeamSummary>)pair.Value);
    }

    /// <summary>The teams referencing <paramref name="id"/>, in catalog order; empty when none does.</summary>
    public static IReadOnlyList<TeamSummary> TeamsUsing(MountId id, IEnumerable<TeamSummary> teams)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ByMountId(teams).TryGetValue(id, out var list) ? list : [];
    }
}
