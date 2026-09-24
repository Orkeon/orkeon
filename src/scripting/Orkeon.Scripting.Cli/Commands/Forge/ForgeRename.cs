using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>The linked session's folder, moved after its team's: what <c>session.renamed</c> says.</summary>
/// <param name="From">The session's slug before.</param>
/// <param name="To">Its slug now: the team folder's name, suffixed when another session held it.</param>
/// <param name="Directory">Its folder now.</param>
/// <param name="Suffixed">Whether the team folder's name was another session's, so a <c>-2</c>… was added.</param>
internal sealed record ForgeRenamedSession(string From, string To, string Directory, bool Suffixed);

/// <summary>What a rename that stands did.</summary>
internal sealed record ForgeRenameResult
{
    /// <summary>The team folder before.</summary>
    public required string From { get; init; }

    /// <summary>The team folder now — <see cref="From"/> itself when the new name keeps the folder.</summary>
    public required string Directory { get; init; }

    /// <summary>The name every title now carries.</summary>
    public required string Name { get; init; }

    /// <summary>The linked session's folder move; null when no session is linked or its folder already had the name.</summary>
    public ForgeRenamedSession? Session { get; init; }

    /// <summary>The schedule reinstalled under the new name; null when none was installed.</summary>
    public ForgeScheduleReport? Schedule { get; init; }
}

/// <summary>Why a rename did not happen: the code, and the sentence — which says whether everything was put back.</summary>
internal sealed record ForgeRenameFailure(string Code, string Message, bool Recoverable = true);

/// <summary>What <c>forge rename</c> produced: the rename, or the refusal — and what it could not do while succeeding.</summary>
internal sealed record ForgeRenameOutcome
{
    /// <summary>The rename; null when it was refused.</summary>
    public ForgeRenameResult? Result { get; init; }

    /// <summary>The refusal; null when the rename stands.</summary>
    public ForgeRenameFailure? Failure { get; init; }

    /// <summary>Non-fatal messages of a rename that stands, each a <c>warning</c> on the stream.</summary>
    public IReadOnlyList<(string Code, string Message)> Warnings { get; init; } = [];
}

/// <summary>
/// <c>forge rename &lt;team-folder&gt; --name &lt;name&gt;</c> (STUDIO-28, D-01): renames a team, all of it
/// or nothing. In order:
/// <list type="number">
/// <item><description>the new folder name, by the one folder rule (<see cref="FolderSlug"/>) — refused when something is already there (D-03);</description></item>
/// <item><description>the team folder, moved to it;</description></item>
/// <item><description>the session rule R links to the team, its folder following the team's (STUDIO-26) — a copy's original is never touched;</description></item>
/// <item><description>the titles: <c>session.json</c> (and <c>promotedTo</c>), <c>forge.json</c>, Studio's <c>studio-team.json</c>, <c>FORGE.md</c>, the launchers' header;</description></item>
/// <item><description><c>schedule/</c>, regenerated for the folder as it is now;</description></item>
/// <item><description>the registration the operating system holds, reinstalled under the new name when one ran the team.</description></item>
/// </list>
/// Everything a step changes is journaled first — the folders moved, the files' bytes — and a step
/// that fails puts back everything done before it, the registration last: it reads the former
/// <c>schedule/</c>, which has to be back where it was. Fully offline, no LLM.
/// </summary>
internal static class ForgeTeamRenamer
{
    /// <summary>How Studio writes its sidecar, kept on a rewrite.</summary>
    private static readonly JsonSerializerOptions SidecarOptions = new() { WriteIndented = true };

    /// <summary>
    /// Renames the team at <paramref name="teamDirectory"/> <paramref name="name"/>. The sessions
    /// rule R reads are <paramref name="workspace"/>'s; the operating system is
    /// <paramref name="host"/>'s, whose clock stamps what is saved.
    /// </summary>
    public static ForgeRenameOutcome Rename(string workspace, string teamDirectory, string name, ForgeScheduleHost host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(host);

        var team = Path.TrimEndingDirectorySeparator(teamDirectory);
        if (!Directory.Exists(team) || Path.GetDirectoryName(team) is not { Length: > 0 } parent)
            return Refused(ForgeErrorCodes.TeamUnreadable, $"rename names no team folder: '{team}'.", recoverable: false);

        // Run from the wrong directory, the verb must never move a folder that is not a team's.
        if (!HoldsATeam(team))
        {
            return Refused(ForgeErrorCodes.TeamUnreadable,
                $"'{team}' holds no team: no {ForgeTeamRecord.FileName}, no {ConventionalNames.TeamSidecarFile}, no crew — nothing to rename.",
                recoverable: false);
        }

        // (1) The folder rule's name, with a team folder's fallback — the very name Studio would
        // have given the team at its adoption.
        var folderName = FolderSlug.From(name) ?? FolderSlug.TeamFallback;
        var destination = Path.Combine(parent, folderName);
        var moves = !ForgeScheduler.SamePath(team, destination);
        if (moves && Occupant(destination) is { } occupant)
        {
            return Refused(ForgeErrorCodes.RenameTaken,
                $"The name's folder '{destination}' is taken: {occupant} is already there. Choose another name.");
        }

        // Read before anything moves: which session rule R links to the team, what its record
        // says, and whether the operating system runs it.
        var link = ForgeTeamLink.Resolve(workspace, team);
        var record = ForgeTeamRecord.TryRead(team);
        var warnings = new List<(string Code, string Message)>();
        IReadOnlyList<string>? former = null;
        if (moves)
        {
            former = RegistrationToFollow(team, record, host, warnings, out var refusal);
            if (refusal is not null)
                return new ForgeRenameOutcome { Failure = refusal };
        }

        var now = host.Clock();
        var journal = new RenameJournal();
        try
        {
            // (2) The team folder.
            var directory = team;
            if (moves)
            {
                Directory.Move(team, destination);
                journal.Moved(team, destination);
                directory = destination;
            }

            // (3) The linked session follows, then (4) every title.
            ForgeSession? session = null;
            ForgeRenamedSession? sessionMove = null;
            if (link is { IsLinked: true, Session: { } linked })
                (session, sessionMove) = FollowTeam(linked, folderName, journal, now);
            if (session is not null)
            {
                session.Document.Title = name;
                session.Document.PromotedTo = directory;
                session.Save(now);
            }

            var formerName = ForgePromoter.ArtifactName(team);
            var artifactName = ForgePromoter.ArtifactName(directory);
            RetitleRecord(directory, name, session?.Document.Slug ?? folderName, journal);
            RenameInSidecar(directory, name, journal, warnings);
            RewriteFile(journal, Path.Combine(directory, ForgePromoter.CardFileName),
                card => ForgePromoter.RetitleCard(card, name, team, formerName, directory, artifactName));
            foreach (var launcher in new[] { ForgePromoter.PosixLauncherName, ForgePromoter.WindowsLauncherName })
            {
                RewriteFile(journal, Path.Combine(directory, launcher),
                    text => ForgePromoter.RenameLauncher(text, formerName, artifactName));
            }

            // (5) schedule/ describes the folder as it is now...
            RegenerateSchedule(directory, artifactName, record, journal, now);

            // (6) ...and the registration runs it, under its new name. Last: its undo needs every
            // other undo done first, the former schedule/ back where it was.
            ForgeScheduleReport? reinstalled = null;
            if (former is not null)
            {
                journal.Last($"the registration '{former[0]}'", () => Reregister(host.Adapter, former, team, artifactName));
                var outcome = new ForgeScheduler(host).Install(directory);
                if (outcome.Failure is { } failure)
                {
                    return RolledBack(journal, failure.Code,
                        $"The team was not renamed: its schedule could not be reinstalled under the new name — {failure.Message}");
                }

                reinstalled = outcome.Report;
                warnings.AddRange(outcome.Warnings);
            }

            return new ForgeRenameOutcome
            {
                Result = new ForgeRenameResult
                {
                    From = team,
                    Directory = directory,
                    Name = name,
                    Session = sessionMove,
                    Schedule = reinstalled,
                },
                Warnings = warnings,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RolledBack(journal, ForgeErrorCodes.RenameFailed, $"The team was not renamed: {ex.Message}");
        }
    }

    /// <summary>What sits where the team would move: null when nothing does.</summary>
    private static string? Occupant(string path)
    {
        if (File.Exists(path))
            return "a file";
        if (!Directory.Exists(path))
            return null;

        return HoldsATeam(path) ? "another team" : "a folder that holds no team";
    }

    /// <summary>The extensions of a crew definition file at a team's root.</summary>
    private static readonly string[] CrewFileExtensions = [".yaml", ".yml", ".ork.ts"];

    /// <summary>
    /// Whether <paramref name="folder"/> holds a team: a promotion's record, Studio's sidecar, a
    /// <c>crew/</c>, or a crew definition at its root — what an import keeps of a crew folder or of a
    /// single crew file.
    /// </summary>
    private static bool HoldsATeam(string folder) =>
        File.Exists(Path.Combine(folder, ForgeTeamRecord.FileName))
        || File.Exists(Path.Combine(folder, ConventionalNames.TeamSidecarFile))
        || Directory.Exists(Path.Combine(folder, ForgeYamlRenderer.CrewDirectoryName))
        || Directory.EnumerateFiles(folder).Any(file =>
            CrewFileExtensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The names of this folder's registration that the rename must carry over — one the operating
    /// system still holds, running this team. Null when there is none: never installed, removed
    /// outside Orkeon since, a copy's inherited block, or installed on another system (said as a
    /// warning). A registration that runs the team although it declares no schedule any more cannot
    /// follow it, and neither can one the OS cannot be asked about: both refuse the rename, nothing
    /// touched. One disabled by hand follows it, and is enabled again — as <c>forge schedule</c> does.
    /// </summary>
    private static IReadOnlyList<string>? RegistrationToFollow(
        string team,
        ForgeTeamRecord? record,
        ForgeScheduleHost host,
        List<(string Code, string Message)> warnings,
        out ForgeRenameFailure? refusal)
    {
        refusal = null;
        if (record?.Schedule?.Installed is not { Names.Count: > 0 } installed)
            return null;

        var check = new ForgeScheduler(host).Check(team);
        if (check.Failure is { } unanswered)
        {
            refusal = new ForgeRenameFailure(unanswered.Code,
                $"The team was not renamed: whether its schedule is installed could not be checked — {unanswered.Message}");
            return null;
        }

        var report = check.Report!;
        if (report.State == ForgeScheduleState.Absent)
        {
            if (report.Reason == ForgeScheduleReasons.OtherSystem)
            {
                warnings.Add((ForgeErrorCodes.ScheduleOtherSystem,
                    $"This team's schedule was installed on {installed.Family}: there, '{string.Join("', '", installed.Names)}' still runs its former folder — reinstall it there with `orkeon forge schedule`."));
            }

            return null;
        }

        if (report.Reason == ForgeScheduleReasons.Undeclared)
        {
            refusal = new ForgeRenameFailure(ForgeErrorCodes.ScheduleStillInstalled,
                $"The team was not renamed: '{string.Join("', '", installed.Names)}' still runs it although it declares no schedule any more,"
                + $" and would be left running a folder that no longer exists. Remove it with `orkeon forge unschedule \"{team}\"`, then rename.");
            return null;
        }

        return installed.Names;
    }

    /// <summary>
    /// The session's folder takes the team folder's name (STUDIO-26), journaled: the move, and the
    /// bytes <c>session.json</c> had before — the move itself rewrites it under the new slug.
    /// </summary>
    private static (ForgeSession Session, ForgeRenamedSession? Move) FollowTeam(
        ForgeSession session, string folderName, RenameJournal journal, DateTimeOffset now)
    {
        var file = Path.Combine(session.Directory, ForgeSession.SessionFileName);
        var original = File.ReadAllBytes(file);
        if (session.FolderNameAfter(folderName) is not { } slug)
        {
            journal.Keep(file, original);
            return (session, null);
        }

        var moved = session.MoveTo(slug, now);
        journal.Moved(session.Directory, moved.Directory);
        journal.Keep(Path.Combine(moved.Directory, ForgeSession.SessionFileName), original);
        return (moved, new ForgeRenamedSession(
            session.Document.Slug, slug, moved.Directory, Suffixed: !string.Equals(slug, folderName, StringComparison.Ordinal)));
    }

    /// <summary>The record's title and slug; a folder without a readable record keeps having none.</summary>
    private static void RetitleRecord(string directory, string name, string slug, RenameJournal journal)
    {
        if (ForgeTeamRecord.TryRead(directory) is null)
            return;

        journal.Snapshot(Path.Combine(directory, ForgeTeamRecord.FileName));
        ForgeTeamRecord.Retitle(directory, name, slug);
    }

    /// <summary>
    /// The name in Studio's sidecar, every other field — Studio's, known to this build or not — as
    /// it was. A sidecar that is not a JSON object is left alone and said: the rename stands.
    /// </summary>
    private static void RenameInSidecar(string directory, string name, RenameJournal journal, List<(string Code, string Message)> warnings)
    {
        var file = Path.Combine(directory, ConventionalNames.TeamSidecarFile);
        if (!File.Exists(file))
            return;

        JsonObject? sidecar;
        try
        {
            sidecar = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
        }
        catch (JsonException)
        {
            sidecar = null;
        }

        if (sidecar is null)
        {
            warnings.Add((ForgeErrorCodes.RenameSidecarUnreadable,
                $"{ConventionalNames.TeamSidecarFile} could not be read: the name it records was left as it was."));
            return;
        }

        journal.Snapshot(file);
        sidecar["name"] = name;
        File.WriteAllText(file, sidecar.ToJsonString(SidecarOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary><paramref name="file"/> rewritten by <paramref name="rewrite"/>, journaled; absent, or unchanged, it is not touched.</summary>
    private static void RewriteFile(RenameJournal journal, string file, Func<string, string> rewrite)
    {
        if (!File.Exists(file))
            return;

        var text = File.ReadAllText(file);
        var rewritten = rewrite(text);
        if (string.Equals(text, rewritten, StringComparison.Ordinal))
            return;

        journal.Snapshot(file);
        File.WriteAllText(file, rewritten);
    }

    /// <summary><c>schedule/</c> regenerated when the team declares a schedule and its artifacts describe another folder or name.</summary>
    private static void RegenerateSchedule(
        string directory, string artifactName, ForgeTeamRecord? record, RenameJournal journal, DateTimeOffset now)
    {
        if (record?.Schedule?.Expression is not { } expression || !ForgeSchedule.TryParse(expression, out var schedule, out _))
            return;

        journal.SnapshotDirectory(Path.Combine(directory, ForgePromoter.ScheduleDirectoryName));
        ForgePromoter.EnsureScheduleArtifacts(directory, artifactName, schedule!, now);
    }

    /// <summary>
    /// The undo of a reinstall, once <paramref name="team"/> and its <c>schedule/</c> are back: the
    /// new name's registration removed, and the former one registered again from that
    /// <c>schedule/</c> — the artifacts it was made from — unless the OS still holds it running the
    /// team's launcher. Throws what the OS answered, for the journal to name.
    /// </summary>
    private static void Reregister(IForgeScheduleAdapter adapter, IReadOnlyList<string> formerNames, string team, string artifactName)
    {
        var names = adapter.NamesFor(artifactName);
        if (!names.SequenceEqual(formerNames, StringComparer.Ordinal))
            Ensure(adapter.Remove(names));

        var launcher = adapter.LauncherOf(team);
        var probe = adapter.Probe(formerNames);
        if (probe.Refusal is { } refusal)
            throw new IOException(refusal);
        if (probe.Exists && ForgeScheduler.SameLauncher(probe.Launcher, launcher))
            return;

        Ensure(adapter.Install(new ForgeScheduleTarget(
            team, Path.Combine(team, ForgePromoter.ScheduleDirectoryName), launcher, formerNames)));
    }

    private static void Ensure(ForgeScheduleOsOutcome outcome)
    {
        if (outcome.Refusal is { } refusal)
            throw new IOException(refusal);
    }

    private static ForgeRenameOutcome Refused(string code, string message, bool recoverable = true) =>
        new() { Failure = new ForgeRenameFailure(code, message, recoverable) };

    /// <summary>Everything undone, and the refusal says so — or names what could not be put back.</summary>
    private static ForgeRenameOutcome RolledBack(RenameJournal journal, string code, string message)
    {
        var kept = journal.Rollback();
        return Refused(code, kept.Count == 0
            ? $"{message} Everything is as it was."
            : $"{message} These could not be put back: {string.Join("; ", kept)}.");
    }

    /// <summary>
    /// What a rename did, and how to undo it: each folder moved, each file's bytes before its first
    /// rewrite, each directory's files before it was regenerated. The undo runs backwards — a file
    /// is put back while its folder is still where the rename took it, then the folder goes back —
    /// and what must wait for every folder to be back runs after all of it.
    /// </summary>
    private sealed class RenameJournal
    {
        private readonly List<(string What, Action Undo)> _undo = [];
        private readonly List<(string What, Action Undo)> _last = [];
        private readonly HashSet<string> _kept = new(StringComparer.Ordinal);

        /// <summary>A folder moved from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public void Moved(string from, string to) =>
            _undo.Add(($"'{to}' back to '{from}'", () => Directory.Move(to, from)));

        /// <summary><paramref name="file"/>'s bytes, as they were before the rename touched it — read before this call.</summary>
        public void Keep(string file, byte[] content)
        {
            if (_kept.Add(file))
                _undo.Add(($"'{file}'", () => File.WriteAllBytes(file, content)));
        }

        /// <summary><paramref name="file"/>'s bytes, as they are now — before its first rewrite.</summary>
        public void Snapshot(string file)
        {
            if (!_kept.Contains(file))
                Keep(file, File.ReadAllBytes(file));
        }

        /// <summary><paramref name="directory"/>'s files as they are now — or that there is no such directory.</summary>
        public void SnapshotDirectory(string directory)
        {
            var files = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(file => (Path.GetRelativePath(directory, file), File.ReadAllBytes(file)))
                    .ToList()
                : null;

            _undo.Add(($"'{directory}'", () =>
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                if (files is null)
                    return;

                Directory.CreateDirectory(directory);
                foreach (var (relative, content) in files)
                {
                    var path = Path.Combine(directory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, content);
                }
            }));
        }

        /// <summary>An undo that runs once every other one ran: the folders are back.</summary>
        public void Last(string what, Action undo) => _last.Add((what, undo));

        /// <summary>Undoes everything, backwards; returns what could not be put back, in words.</summary>
        public List<string> Rollback()
        {
            var failed = new List<string>();
            foreach (var (what, undo) in Enumerable.Reverse(_undo).Concat(Enumerable.Reverse(_last)))
            {
                try
                {
                    undo();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{what} ({ex.Message})");
                }
            }

            return failed;
        }
    }
}
