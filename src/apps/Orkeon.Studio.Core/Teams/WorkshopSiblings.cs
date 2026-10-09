using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// What one gesture on a workshop team did to its trees (STUDIO-64), each tree named by its
/// kind — <c>teams</c>, <c>workbooks</c>, <c>tests</c>, <c>settings</c>, a whole
/// <c>mounts.&lt;name&gt;</c> — in the order <see cref="WorkshopLayout.TreesOf"/> lists them.
/// </summary>
/// <param name="Moved">The trees that moved — or, for a copy, were copied.</param>
/// <param name="Taken">The trees whose destination was already taken: then nothing moved.</param>
/// <param name="Kept">The trees still under the former slug after a gesture that moved none of them or put them back — the ones put back and the one that failed, or every tree when a destination was taken.</param>
/// <param name="Destination">Where the gesture put things: the <c>archive/&lt;slug&gt;</c> folder, the copied settings folder; null for a rename, and when nothing moved.</param>
public sealed record WorkshopMoveResult(
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> Taken,
    IReadOnlyList<string> Kept,
    string? Destination)
{
    /// <summary>Nothing to do, nothing done: a plain catalogue, or a team with no tree beside its own.</summary>
    public static readonly WorkshopMoveResult Nothing = new([], [], [], null);

    /// <summary>Whether every tree went where the gesture meant it to: no destination taken, nothing put back.</summary>
    public bool Succeeded => Taken.Count == 0 && Kept.Count == 0;
}

/// <summary>
/// In a workshop folder, the trees that go with a team follow the gestures Studio makes on it
/// (STUDIO-64): Rename moves <c>workbooks/</c>, <c>tests/</c>, <c>settings/</c> and each
/// <c>mounts.&lt;name&gt;/</c> after the engine moved the team folder; Delete moves the whole of
/// them, the team folder included, under <c>archive/&lt;slug&gt;/&lt;kind&gt;/</c>; Duplicate
/// copies <c>settings/</c> alone — the one tree that changes a run. Every move is journaled and
/// put back when a later one fails, the way <c>forge rename</c> does; a plain catalogue —
/// <see cref="WorkshopLayout.IsWorkshop"/> false — is never touched.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: the catalogue lives on the host's disk — the workshop folder is " +
    "user-owned storage addressed before any VFS mount exists.")]
public static class WorkshopSiblings
{
    // EXCEPTION-BOOTSTRAP: the workshop folder is the physical directory that holds the teams
    // root, moved and copied on the host's disk before any VFS mount exists.

    /// <summary>
    /// The trees of <paramref name="slug"/> that already exist beside the teams root — the team
    /// folder left aside, which the engine's own rule checks —: a rename towards that slug is
    /// refused while any is there, before the engine is asked. Empty outside a workshop.
    /// </summary>
    public static IReadOnlyList<string> TakenTrees(string teamsRoot, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (!WorkshopLayout.IsWorkshop(teamsRoot))
            return [];

        return [.. Siblings(teamsRoot, slug).Select(tree => tree.Kind)];
    }

    /// <summary>
    /// Moves the sibling trees of <paramref name="fromSlug"/> under <paramref name="toSlug"/>,
    /// after the engine renamed the team folder: the ones that exist, each refused when its
    /// destination is taken — then nothing moves —, and all put back when one cannot move.
    /// Nothing outside a workshop.
    /// </summary>
    public static WorkshopMoveResult FollowRename(string teamsRoot, string fromSlug, string toSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSlug);

        if (!WorkshopLayout.IsWorkshop(teamsRoot))
            return WorkshopMoveResult.Nothing;

        var root = WorkshopLayout.RootOf(teamsRoot);
        var trees = Siblings(teamsRoot, fromSlug);
        if (trees.Count == 0)
            return WorkshopMoveResult.Nothing;

        var moves = trees.Select(tree => (tree.Kind, From: tree.Path, To: Path.Combine(root, tree.Kind, toSlug))).ToList();
        var taken = moves.Where(move => Exists(move.To)).Select(move => move.Kind).ToList();
        if (taken.Count > 0)
            return new WorkshopMoveResult([], taken, [.. moves.Select(move => move.Kind)], null);

        return MoveAll(moves, destinationFolder: null);
    }

    /// <summary>
    /// Moves every tree of <paramref name="slug"/>, its team folder first, under
    /// <c>archive/&lt;slug&gt;/&lt;kind&gt;/</c> — <c>archive/&lt;slug&gt;-2/</c> when the name is
    /// taken, by <see cref="TeamCatalog.FreeSibling"/>. The team folder moves first: when it
    /// cannot, nothing else has moved; a later refusal puts everything back. Nothing outside a
    /// workshop — the caller deletes as it always did.
    /// </summary>
    public static WorkshopMoveResult Archive(string teamsRoot, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (!WorkshopLayout.IsWorkshop(teamsRoot))
            return WorkshopMoveResult.Nothing;

        var trees = WorkshopLayout.TreesOf(teamsRoot, slug);
        if (trees.Count == 0)
            return WorkshopMoveResult.Nothing;

        var archive = Path.Combine(WorkshopLayout.RootOf(teamsRoot), WorkshopLayout.ArchiveFolder, slug);
        if (Exists(archive))
            archive = TeamCatalog.FreeSibling(archive);

        try
        {
            Directory.CreateDirectory(archive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WorkshopMoveResult([], [], [.. trees.Select(tree => tree.Kind)], null);
        }

        var moves = trees.Select(tree => (tree.Kind, From: tree.Path, To: Path.Combine(archive, tree.Kind))).ToList();
        return MoveAll(moves, archive);
    }

    /// <summary>
    /// Copies <c>settings/&lt;fromSlug&gt;</c> to <c>settings/&lt;toSlug&gt;</c>, and nothing else:
    /// the copy's launchers read the settings of their own folder, and a copy without them would
    /// run on the machine's model in silence. A settings folder already under the copy's name is
    /// the user's and is left alone, said as taken; a copy the disk refused is removed whole and
    /// said as kept. Nothing outside a workshop, nothing for a team without settings.
    /// </summary>
    public static WorkshopMoveResult CopySettings(string teamsRoot, string fromSlug, string toSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSlug);

        if (!WorkshopLayout.IsWorkshop(teamsRoot))
            return WorkshopMoveResult.Nothing;

        var settings = Path.Combine(WorkshopLayout.RootOf(teamsRoot), WorkshopLayout.SettingsFolder);
        var source = Path.Combine(settings, fromSlug);
        if (!Directory.Exists(source))
            return WorkshopMoveResult.Nothing;

        var destination = Path.Combine(settings, toSlug);
        if (Exists(destination))
            return new WorkshopMoveResult([], [WorkshopLayout.SettingsFolder], [], null);

        try
        {
            TeamCatalog.CopyTree(source, destination);
            return new WorkshopMoveResult([WorkshopLayout.SettingsFolder], [], [], destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(destination);
            return new WorkshopMoveResult([], [], [WorkshopLayout.SettingsFolder], null);
        }
    }

    /// <summary>The trees of a team beside its own folder: the team folder is the engine's business.</summary>
    private static List<WorkshopTree> Siblings(string teamsRoot, string slug) =>
        [.. WorkshopLayout.TreesOf(teamsRoot, slug).Where(tree => tree.Kind != WorkshopLayout.TeamsFolder)];

    /// <summary>
    /// Moves each tree in order, journaling what moved; the first refusal puts the moved ones
    /// back, last first, and the result names every tree as kept. A tree the disk keeps at its
    /// destination on the way back is still said as moved — nothing is deleted to hide it.
    /// </summary>
    private static WorkshopMoveResult MoveAll(List<(string Kind, string From, string To)> moves, string? destinationFolder)
    {
        var journal = new List<(string Kind, string From, string To)>();
        foreach (var move in moves)
        {
            try
            {
                Directory.Move(move.From, move.To);
                journal.Add(move);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var stuck = MoveBack(journal);
                if (destinationFolder is not null && stuck.Count == 0)
                    TryDelete(destinationFolder);

                var kept = moves.Select(m => m.Kind).Where(kind => !stuck.Contains(kind, StringComparer.Ordinal)).ToList();
                return new WorkshopMoveResult(stuck, [], kept, stuck.Count == 0 ? null : destinationFolder);
            }
        }

        return new WorkshopMoveResult([.. journal.Select(move => move.Kind)], [], [], destinationFolder);
    }

    /// <summary>Puts the journaled trees back, last first; the kinds the disk kept at their destination.</summary>
    private static List<string> MoveBack(List<(string Kind, string From, string To)> journal)
    {
        var stuck = new List<string>();
        for (var i = journal.Count - 1; i >= 0; i--)
        {
            try
            {
                Directory.Move(journal[i].To, journal[i].From);
            }
            catch (Exception back) when (back is IOException or UnauthorizedAccessException)
            {
                stuck.Add(journal[i].Kind);
            }
        }

        return stuck;
    }

    /// <summary>Whether a file or a folder sits at <paramref name="path"/>; an unreadable disk answers no.</summary>
    private static bool Exists(string path)
    {
        try
        {
            return Path.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Removes an empty archive folder, or a copy that failed halfway; what the disk keeps stays.</summary>
    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Better a leftover folder than an exception out of a gesture that already failed.
        }
    }
}
