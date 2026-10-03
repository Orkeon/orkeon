using Orkeon.Constants.Cli;
using Orkeon.Constants.FileSystem;
using System.Globalization;
using System.Security;
using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// A parsed <c>--schedule</c> value (SPEC-ORKEON-FORGE §11). Two spellings cover the
/// archetypal needs of the audience: <c>daily@HH:mm</c> ("every morning") and
/// <c>hourly</c>. Everything richer belongs in the generated artifacts, edited by hand.
/// </summary>
internal sealed record ForgeSchedule
{
    /// <summary>Runs once a day at <see cref="Hour"/>:<see cref="Minute"/>.</summary>
    public const string KindDaily = "daily";

    /// <summary>Runs at the top of every hour.</summary>
    public const string KindHourly = "hourly";

    /// <summary><see cref="KindDaily"/> or <see cref="KindHourly"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Hour of day, 0-23 (daily only).</summary>
    public int Hour { get; init; }

    /// <summary>Minute, 0-59 (daily only).</summary>
    public int Minute { get; init; }

    /// <summary>
    /// The schedule as the grammar spells it — <c>daily@HH:mm</c> or <c>hourly</c> — which is
    /// what <c>forge.json</c> records and Studio's sidecar holds (STUDIO-27).
    /// </summary>
    public string Expression => Kind == KindHourly
        ? KindHourly
        : string.Create(CultureInfo.InvariantCulture, $"{KindDaily}@{Hour:00}:{Minute:00}");

    /// <summary>Parses <c>daily@HH:mm</c> or <c>hourly</c>; anything else is an error sentence.</summary>
    public static bool TryParse(string text, out ForgeSchedule? schedule, out string? error)
    {
        schedule = null;
        error = null;

        if (string.Equals(text, KindHourly, StringComparison.OrdinalIgnoreCase))
        {
            schedule = new ForgeSchedule { Kind = KindHourly };
            return true;
        }

        if (text.StartsWith("daily@", StringComparison.OrdinalIgnoreCase)
            && TimeOnly.TryParseExact(text["daily@".Length..], "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
        {
            schedule = new ForgeSchedule { Kind = KindDaily, Hour = time.Hour, Minute = time.Minute };
            return true;
        }

        error = $"'{text}' is not a schedule — use daily@HH:mm (e.g. daily@07:30) or hourly.";
        return false;
    }
}

/// <summary>The platform whose launcher and install command the promotion highlights.</summary>
internal enum ForgePromotePlatform
{
    /// <summary>Windows: <c>run.cmd</c> and <c>schtasks</c>.</summary>
    Windows,

    /// <summary>Linux: <c>run.sh</c> and the systemd user timer.</summary>
    Linux,

    /// <summary>Everything else: <c>run.sh</c> and the cron line.</summary>
    Other,
}

/// <summary>What a promotion produced, for the <c>promoted</c> event and the caller.</summary>
internal sealed record ForgePromotionResult
{
    /// <summary>Absolute destination directory.</summary>
    public required string Destination { get; init; }

    /// <summary>The host platform's launcher, relative to the destination.</summary>
    public required string Launcher { get; init; }

    /// <summary>Relative schedule directory, when a schedule was requested.</summary>
    public string? ScheduleDirectory { get; init; }

    /// <summary>
    /// The host platform's install command, for a person installing by hand. The promotion never
    /// runs it: <c>forge schedule</c> installs the schedule, when the user agrees (STUDIO-27).
    /// </summary>
    public string? InstallCommand { get; init; }

    /// <summary>
    /// Whether an existing promotion of the same session was updated in place (W-09) —
    /// clients warn that regenerated files (run.sh, crew/) lost any hand edits.
    /// </summary>
    public bool Updated { get; init; }

    /// <summary>
    /// When the team's <c>orkeon</c> command is longer than a <c>cmd</c> command holds: its length
    /// and its longest option — <c>run.cmd</c> launches nothing and says why, <c>run.sh</c> is
    /// complete (STUDIO-51); null when <c>run.cmd</c> runs the team.
    /// </summary>
    public TeamLauncherCommandLength? WindowsLauncherTooLong { get; init; }
}

/// <summary>
/// Promotion of a Ready session (SPEC-ORKEON-FORGE §11): the crew leaves the session
/// directory as an ordinary folder — <c>crew/</c>, <c>run.cmd</c>/<c>run.sh</c>,
/// <c>FORGE.md</c>, and on request a <c>schedule/</c> of generated artifacts. Nothing here is
/// proprietary to the forge: <c>orkeon run</c> launches the folder, Studio's launcher detects it.
/// <para>
/// The launchers say what this promotion knows — the crew, the settings, the folders, the
/// brief's sample — with the option names of <c>Orkeon.Constants.Cli</c>, and the composer
/// <see cref="TeamLauncherScript"/> writes them (STUDIO-51): the frame, the header <c>forge
/// rename</c> finds the team's name in, each value as <c>cmd</c> and the runner read it back.
/// For a team it adopts, Orkeon Studio writes the two launchers again through the same composer
/// (STUDIO-50, <c>TeamLaunchers</c>), with the team's model setting and every folder bound to it,
/// which this promotion cannot know.
/// </para>
/// </summary>
internal static class ForgePromoter
{
    /// <summary>Name of the launcher script on Windows.</summary>
    public const string WindowsLauncherName = ConventionalNames.WindowsTeamLauncher;

    /// <summary>Name of the launcher script everywhere else.</summary>
    public const string PosixLauncherName = ConventionalNames.PosixTeamLauncher;

    /// <summary>Name of the generated identity card.</summary>
    public const string CardFileName = "FORGE.md";

    /// <summary>Name of the schedule artifact directory.</summary>
    public const string ScheduleDirectoryName = "schedule";

    /// <summary>Name of the copied settings file, when <c>--with-settings</c> asked for it.</summary>
    public const string SettingsFileName = ConventionalNames.SettingsFile;

    /// <summary>The platform this process runs on.</summary>
    public static ForgePromotePlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
            return ForgePromotePlatform.Windows;

        return OperatingSystem.IsLinux() ? ForgePromotePlatform.Linux : ForgePromotePlatform.Other;
    }

    /// <summary>
    /// The name every generated artifact carries (STUDIO-26, D-06): the team folder's — the unit
    /// files, the timer and the scheduled task, their descriptions, the install command, the
    /// launchers' header. Never the session's slug: that slug is the need the session was opened
    /// with, and it outlived the name the user gave the team everywhere a scheduler shows it. A
    /// systemd unit and a scheduled task only take a restricted alphabet, so the folder's name
    /// goes through the one folder rule (<see cref="FolderSlug"/>) — for every folder Studio adopts
    /// into, which that very rule named, the folder's name itself — and a name that keeps no
    /// usable character falls back the way a team folder does.
    /// </summary>
    internal static string ArtifactName(string destination) =>
        FolderSlug.From(Path.GetFileName(Path.TrimEndingDirectorySeparator(destination))) ?? FolderSlug.TeamFallback;

    /// <summary>
    /// A launcher's text with its header (<see cref="TeamLauncherScript.Header"/>) naming
    /// <paramref name="name"/> rather than <paramref name="formerName"/> (STUDIO-28): every other
    /// line — the sample inputs a user adapted included — as it was. Nothing else in a launcher
    /// names the team: its paths are anchored to the script's own folder.
    /// </summary>
    internal static string RenameLauncher(string launcher, string formerName, string name) =>
        launcher.Replace(TeamLauncherScript.Header(formerName), TeamLauncherScript.Header(name), StringComparison.Ordinal);

    /// <summary>
    /// <c>FORGE.md</c>'s text for a team renamed <paramref name="title"/> and moved from
    /// <paramref name="formerDirectory"/> to <paramref name="directory"/> (STUDIO-28): its title
    /// line, and the install command its schedule section shows — each platform's spelling, the
    /// card being written on whichever machine promoted it. Everything else, hand edits included,
    /// as it was.
    /// </summary>
    internal static string RetitleCard(
        string card, string title, string formerDirectory, string formerName, string directory, string name)
    {
        var text = card;
        if (text.StartsWith("# ", StringComparison.Ordinal))
        {
            var end = text.IndexOfAny(['\r', '\n']);
            text = $"# {title}{(end < 0 ? "" : text[end..])}";
        }

        foreach (var platform in Enum.GetValues<ForgePromotePlatform>())
        {
            text = text.Replace(
                ForgeScheduleAdapters.ManualInstallCommand(platform, formerDirectory, formerName),
                ForgeScheduleAdapters.ManualInstallCommand(platform, directory, name),
                StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>
    /// Writes the promoted folder. Throws <see cref="InvalidOperationException"/> on a
    /// precondition the user can fix (non-empty destination, missing crew) — the command
    /// maps those to exit 1 and the session stays Ready, retryable.
    /// </summary>
    public static ForgePromotionResult Promote(
        ForgeSession session,
        string destination,
        ForgeSchedule? schedule,
        string? settingsPath,
        bool copySettings,
        ForgePromotePlatform platform,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var crewSource = Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName);
        if (!Directory.Exists(crewSource))
            throw new InvalidOperationException($"The session holds no rendered crew under '{ForgeYamlRenderer.CrewDirectoryName}/'.");

        // A non-empty destination is refused — with ONE exception (W-09): the folder this
        // very session is linked to by rule R (STUDIO-25) — where it promoted to, or that
        // folder moved or renamed since; never a copy of it. Re-adoption then UPDATES it in
        // place: the generated artifacts (crew/, schedule/, launchers, FORGE.md) are
        // regenerated, everything else — sidecar, user files, outputs — is preserved. Omitting
        // the schedule on a re-adoption removes schedule/: the folder says what is true.
        var updating = false;
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            var link = ForgeTeamLink.Of(session, destination);
            if (link == TeamSessionLinkKind.Copy)
            {
                throw new InvalidOperationException(
                    $"'{destination}' is not empty: it is a copy of '{session.Document.PromotedTo}', the folder this session"
                    + " promoted to. Promote into a fresh directory, or give the copy a session of its own with `forge reopen`.");
            }

            if (!TeamSessionLink.IsLinked(link))
                throw new InvalidOperationException($"'{destination}' is not empty — promote into a fresh directory.");

            updating = true;
            DeleteIfExists(Path.Combine(destination, ForgeYamlRenderer.CrewDirectoryName));
            DeleteIfExists(Path.Combine(destination, ScheduleDirectoryName));

            // A previous promote's settings copy usually carries API keys: when this
            // re-adoption does not ask for one, a stale unreferenced copy must not
            // linger in a folder FORGE.md invites sharing.
            if (!copySettings)
                File.Delete(Path.Combine(destination, SettingsFileName));
        }

        Directory.CreateDirectory(destination);
        CopyDirectory(crewSource, Path.Combine(destination, ForgeYamlRenderer.CrewDirectoryName));

        // The settings reference of the launch scripts: copied into the folder only on
        // explicit request — a settings file usually carries API keys, and FORGE.md invites
        // sharing the folder. Without the copy the scripts point at the resolved file in
        // place (this machine), or omit --settings entirely and let `orkeon run` resolve.
        string? settingsReference = null;
        var settingsIsRelative = false;
        if (settingsPath is not null && copySettings)
        {
            // overwrite: a re-adoption may find the previous promote's copy in place.
            File.Copy(settingsPath, Path.Combine(destination, SettingsFileName), overwrite: true);
            settingsReference = SettingsFileName;
            settingsIsRelative = true;
        }
        else if (settingsPath is not null)
        {
            settingsReference = Path.GetFullPath(settingsPath);
        }

        var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName);
        var verdict = session.TryLoadArtifact<ForgeVerdict>(ForgeSession.VerdictFileName);

        // The use case the team was composed from, titled in the brief's language now that it is
        // known (STUDIO-40, D-04): the card, the record and the session say the same thing. In
        // memory, like the team's name: the caller saves the session once the promotion stands.
        if (session.Document.Reference is { } reference)
            session.Document.Reference = ForgeReference.Retitle(reference, brief?.Language);

        // The folders the crew reads and writes. The trial bench mounted /output and
        // /workspace; nothing else did, so a promoted team used to run "successfully",
        // write nothing at all and read nothing at all. The launchers now carry the mounts
        // the blueprint asks for, and the folders exist before the first launch — an absent
        // mount base path is fatal at host build.
        var writeMounts = DeliverableMounts(session);
        foreach (var mount in writeMounts)
            Directory.CreateDirectory(Path.Combine(destination, mount.Folder));

        // A folder the session held «inside the team» until now (STUDIO-46) — an input the
        // user filled for the trial — moves into the team it was meant for.
        MoveSessionFolders(session, destination, writeMounts);

        var teamName = ArtifactName(destination);
        var launchers = new TeamLauncherSpec
        {
            TeamName = teamName,
            TeamDirectory = destination,
            IsScript = ForgeSession.IsScriptFormat(session.Document.Format),
            Segments = RunArguments(session, brief, settingsReference, settingsIsRelative, writeMounts),
        };
        WritePosixLauncher(destination, launchers);
        WriteWindowsLauncher(destination, launchers);
        var windowsCommand = TeamLauncherScript.MeasureWindows(launchers);

        string? installCommand = null;
        string? scheduleDirectory = null;
        if (schedule is not null)
        {
            scheduleDirectory = ScheduleDirectoryName;
            WriteScheduleArtifacts(destination, teamName, schedule, now);
            installCommand = ForgeScheduleAdapters.ManualInstallCommand(platform, destination, teamName);
        }

        WriteCard(destination, new ForgeCard
        {
            Session = session,
            TeamName = teamName,
            Brief = brief,
            Verdict = verdict,
            Schedule = schedule,
            InstallCommand = installCommand,
            Now = now,
            WriteMounts = writeMounts,
        });

        // The machine-readable twin of the card (FORGE-09): what `forge reopen` needs to
        // rebuild a faithful session from this folder once the original one is gone — the
        // brief above all, which the crew files do not carry. No secret in it.
        ForgeTeamRecord.Write(destination, session, brief, now, schedule);

        return new ForgePromotionResult
        {
            Destination = destination,
            Launcher = platform == ForgePromotePlatform.Windows ? WindowsLauncherName : PosixLauncherName,
            ScheduleDirectory = scheduleDirectory,
            InstallCommand = installCommand,
            Updated = updating,
            WindowsLauncherTooLong = windowsCommand.Fits ? null : windowsCommand,
        };
    }

    private static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// The <c>orkeon run</c> invocation both launchers spell, one segment per option: the promoted
    /// crew, the settings when one is known, the folders, and the brief's sample inputs so the
    /// folder runs out of the box — the launchers say in a comment that these are the inputs to
    /// adapt. Paths inside the folder are anchored to the launcher's own folder; the composer
    /// (<see cref="TeamLauncherScript"/>) writes each value for the shell that reads it.
    /// </summary>
    private static List<IReadOnlyList<TeamLauncherArgument>> RunArguments(
        ForgeSession session,
        ForgeBrief? brief,
        string? settingsReference,
        bool settingsIsRelative,
        List<DeliverableMount> writeMounts)
    {
        var segments = new List<IReadOnlyList<TeamLauncherArgument>>
        {
            new[] { TeamLauncherArgument.Word("run"), TeamLauncherArgument.InTeam("", RunTarget(session), "") },
        };

        // A single-value option is written --option=value: a value starting with '-' stays its
        // value for the runner's grammar (STUDIO-51, decision 7).
        if (settingsReference is not null)
        {
            segments.Add(
            [
                TeamLauncherArgument.Assign(
                    RunOptionNames.Flag(RunOptionNames.Settings),
                    settingsIsRelative
                        ? TeamLauncherArgument.InTeam("", settingsReference, "")
                        : TeamLauncherArgument.Literal(settingsReference)),
            ]);
        }

        // Several mounts go space-separated after ONE --mount: the CLI's parser rejects a
        // repeated option. The anchor keeps the folder relocatable with the team.
        //
        // The physical segment carries the mount grammar's own quotes, because the anchor
        // expands to a path nobody controls at generation time. On Windows it always contains a
        // ':' — %~dp0 is C:\… — so the unquoted spec split into four segments and EVERY promoted
        // team died at start with a grammar error naming a path the user never typed. A team
        // folder whose path holds a '"' — Linux only — is the one the grammar's quotes cannot
        // carry; the CLI's own --mount does.
        if (writeMounts.Count > 0)
        {
            segments.Add(
            [
                TeamLauncherArgument.Word(RunOptionNames.Flag(RunOptionNames.Mount)),
                .. writeMounts.Select(mount => TeamLauncherArgument.InTeam(
                    "\"", mount.Folder, $"\":{mount.VirtualRoot}:{(mount.ReadOnly ? "ro" : "rw")}")),
            ]);
        }

        // The sample inputs only exist on the YAML path: `orkeon run` documents
        // --var/--initial-context as ignored for .ork.ts crews, and spelling ignored
        // flags would be a lie in a file people copy from.
        if (ForgeSession.IsScriptFormat(session.Document.Format))
            return segments;

        // Same single-flag rule as --mount: several values go space-separated after ONE
        // --var. Emitting the flag per variable made any brief with two sample inputs
        // promote to a team the parser refuses ("option repeated").
        var variables = (brief?.Sample?.Variables ?? [])
            .Select(pair => TeamLauncherArgument.Literal($"{pair.Key}={pair.Value}"))
            .ToList();
        if (variables.Count > 0)
            segments.Add([TeamLauncherArgument.Word(RunOptionNames.Flag(RunOptionNames.Var)), .. variables]);

        if (!string.IsNullOrWhiteSpace(brief?.Sample?.InitialContext))
            segments.Add([TeamLauncherArgument.Assign(RunOptionNames.Flag(RunOptionNames.InitialContext), brief.Sample.InitialContext)]);

        return segments;
    }

    /// <summary>
    /// One writable mount the promoted folder carries: a virtual root the blueprint writes
    /// to, backed by a folder inside the team.
    /// </summary>
    /// <param name="VirtualRoot">The root the agents address, e.g. <c>/output</c>.</param>
    /// <param name="Folder">Its folder inside the promoted directory, e.g. <c>output</c>.</param>
    /// <param name="ReadOnly">Whether the team only reads it (the <c>/workspace</c> input folder).</param>
    internal sealed record DeliverableMount(string VirtualRoot, string Folder, bool ReadOnly = false);

    /// <summary>
    /// The virtual root a reading team reads from, and the folder inside the team that backs it.
    /// <para>
    /// The trial bench mounts the CLI's working directory as <c>/workspace:ro</c>, and nothing
    /// carried that into adoption: a team whose agents use <c>file_read</c> passed its trial
    /// and then could read nothing at all — the same shape as the missing <c>/output</c>, on
    /// the other side. The same rule, evaluated where the team now lives, would be the team's
    /// own folder; it is a folder INSIDE it instead, because <c>--with-settings</c> puts an
    /// <c>appsettings.json</c> holding API keys at that root and a read mount over it would
    /// hand them to any agent with a file tool.
    /// </para>
    /// </summary>
    private const string ReadVirtualRoot = RunnerVirtualRoots.Workspace;

    /// <summary>Folder inside the promoted team backing <see cref="ReadVirtualRoot"/>.</summary>
    private const string ReadFolderName = "input";

    /// <summary>Tools whose presence means the team expects something to read.</summary>
    private static readonly string[] ReadingTools = ["file_read", "directory_read"];

    /// <summary>
    /// The virtual roots the runners mount for themselves - never a deliverable's. The satellite
    /// set, not a third hand-written copy of it: this one predated <c>All</c> and never gained
    /// <c>/sandbox</c>, so a promoted team could declare a deliverable at a root every runner
    /// refuses.
    /// </summary>
    private static readonly IReadOnlyList<string> ReservedVirtualRoots = RunnerVirtualRoots.All;

    /// <summary>
    /// The folders the promoted team mounts: the session's confirmed list (STUDIO-46), each
    /// backed by a folder inside the team — the one list the trial used, applied where it
    /// becomes true: the launcher.
    /// <para>
    /// Without this, a team that passed its trial had no folder at all once adopted: the
    /// deliverable resolver caught the access denial, logged a warning and reported the run as
    /// finished, so the folder stayed empty and nothing on screen said why.
    /// </para>
    /// </summary>
    internal static List<DeliverableMount> DeliverableMounts(ForgeSession session)
    {
        var folders = ForgeFolders.Of(session);
        var mounts = new List<DeliverableMount>();
        foreach (var folder in folders)
        {
            if (MountableRoot(folder.Path) is not { } root)
                continue;

            mounts.Add(new DeliverableMount(root, TeamFolderOf(folder, folders), folder.IsInput));
        }

        return mounts;
    }

    /// <summary>
    /// The folder inside the team behind <paramref name="folder"/>: its own name — except
    /// <c>/workspace</c>, which reads <c>input/</c>, never the team's root (see
    /// <see cref="ReadFolderName"/>), unless another folder already takes that name.
    /// </summary>
    private static string TeamFolderOf(ForgeFolder folder, IReadOnlyList<ForgeFolder> folders)
    {
        if (!string.Equals(folder.Path, ReadVirtualRoot, StringComparison.Ordinal))
            return folder.Name;

        var inputTaken = folders.Any(f => string.Equals(f.Name, ReadFolderName, StringComparison.OrdinalIgnoreCase));
        return inputTaken ? folder.Name : ReadFolderName;
    }

    /// <summary>
    /// Moves what the session held for each folder (<c>folders/&lt;name&gt;</c>) into the team's
    /// folder, then removes the session's copy. An entry the team already has is left where it
    /// is in the session: a re-adoption never overwrites the team's own files.
    /// </summary>
    private static void MoveSessionFolders(ForgeSession session, string destination, IReadOnlyList<DeliverableMount> mounts)
    {
        var folders = ForgeFolders.Of(session);
        foreach (var mount in mounts)
        {
            var folder = folders.First(f => string.Equals(f.Path, mount.VirtualRoot, StringComparison.Ordinal));
            var source = ForgeFolders.SessionFolder(session, folder);
            if (!Directory.Exists(source))
                continue;

            var target = Path.Combine(destination, mount.Folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                var moved = Path.Combine(target, Path.GetFileName(entry));
                if (File.Exists(moved) || Directory.Exists(moved))
                    continue;

                if (Directory.Exists(entry))
                    Directory.Move(entry, moved);
                else
                    File.Move(entry, moved);
            }

            if (!Directory.EnumerateFileSystemEntries(source).Any())
                Directory.Delete(source);
        }
    }

    /// <summary>
    /// What a plan addresses by itself — a reading agent reads <c>/workspace</c>, each
    /// deliverable root is written to — for a session that holds no folder list
    /// (<see cref="ForgeFolders.Of"/>) and for a plan compiled without one.
    /// </summary>
    internal static List<DeliverableMount> DerivedMounts(ForgeBlueprint? blueprint)
    {
        var roots = new List<DeliverableMount>();

        // Read first, so the chips and the command line list it the way the Composer does.
        if ((blueprint?.Agents ?? []).Any(a => (a.Tools ?? []).Intersect(ReadingTools, StringComparer.Ordinal).Any()))
            roots.Add(new DeliverableMount(ReadVirtualRoot, ReadFolderName, ReadOnly: true));

        foreach (var task in blueprint?.Tasks ?? [])
        {
            if (MountableRoot(task.Deliverable) is { } root)
                AddWriteMount(roots, root);
        }

        return roots;
    }

    /// <summary>
    /// The virtual root a deliverable can be mounted at, or <see langword="null"/> when it
    /// cannot be one.
    /// <para>
    /// A deliverable root is a single segment by construction; anything else would put the
    /// team's own files outside its folder. The blueprint is LLM-authored, so '..' and '.'
    /// are refused explicitly rather than trusted to be absent. ':' and ';' are refused for
    /// the same reason, one level down: they are the mount grammar's own separators, so a
    /// root carrying either spells a --mount the runner cannot parse and the promoted team
    /// dies at every launch. ForgeBlueprint.Validate reports it to the model, where a repair
    /// turn can rename the folder; this is the guard for a blueprint that reached here
    /// anyway. A root the runner keeps for itself is refused too: the very --mount the
    /// launcher spells would be rejected at start (ADR-008, decision 5).
    /// </para>
    /// </summary>
    private static string? MountableRoot(string? deliverable)
    {
        if (deliverable is not { Length: > 1 } value || value[0] != '/')
            return null;

        var slash = value.IndexOf('/', 1);
        var root = slash > 1 ? value[..slash] : value;
        if (root.Length <= 1)
            return null;

        var folder = root[1..];
        if (folder.Contains('/', StringComparison.Ordinal)
            || folder.Contains('\\', StringComparison.Ordinal)
            || folder.AsSpan().ContainsAny(':', ';')
            || folder is "." or "..")
        {
            return null;
        }

        return ReservedVirtualRoots.Contains(root, StringComparer.Ordinal) ? null : root;
    }

    /// <summary>
    /// Records the write mount of <paramref name="root"/>.
    /// <para>
    /// The read mount is derived first, so a deliverable landing under the SAME root meets a
    /// read-only entry. Skipping on the name alone left the team with `/workspace:ro` and a
    /// deliverable it could never write — the run reports success and produces nothing.
    /// Studio deduped the other way and showed two /workspace chips, one promising a write
    /// that was then silently dropped. One root, one mount, and a write requirement wins
    /// over a read one.
    /// </para>
    /// </summary>
    private static void AddWriteMount(List<DeliverableMount> roots, string root)
    {
        var existing = roots.FindIndex(m => string.Equals(m.VirtualRoot, root, StringComparison.Ordinal));
        if (existing < 0)
            roots.Add(new DeliverableMount(root, root[1..]));
        else if (roots[existing].ReadOnly)
            roots[existing] = roots[existing] with { ReadOnly = false };
    }

    /// <summary>What `orkeon run` targets, relative to the promoted folder.</summary>
    private static string RunTarget(ForgeSession session) =>
        ForgeSession.IsScriptFormat(session.Document.Format)
            ? $"{ForgeYamlRenderer.CrewDirectoryName}/{ForgeScriptRenderer.ScriptFileName}"
            : ForgeYamlRenderer.CrewDirectoryName;

    /// <summary>
    /// <c>run.sh</c>, executable. Run from the team's folder: the runner refuses to read a crew
    /// outside the working directory without <c>--allow-external-mounts</c>, and the security
    /// whitelist is rooted on the working directory too — a launch from anywhere else (a scheduled
    /// task starts in the system directory) was refused outright, or wrote nothing into
    /// <c>/output</c>. The composer's frame does it, and resolves a symlink onto PATH first.
    /// </summary>
    private static void WritePosixLauncher(string destination, TeamLauncherSpec launchers)
    {
        var path = Path.Combine(destination, PosixLauncherName);
        File.WriteAllText(path, TeamLauncherScript.Posix(launchers), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary><c>run.cmd</c>: UTF-8 without a BOM, which <c>cmd</c> would read as text (STUDIO-51).</summary>
    private static void WriteWindowsLauncher(string destination, TeamLauncherSpec launchers) =>
        File.WriteAllText(
            Path.Combine(destination, WindowsLauncherName),
            TeamLauncherScript.Windows(launchers),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>
    /// Writes all three schedule families under <c>schedule/</c> — the "choice" of §11 is made at
    /// install time, not at generation time: the folder is portable and the artifacts are a few
    /// hundred bytes. Every name in them is <paramref name="teamName"/> (<see cref="ArtifactName"/>)
    /// and every path this folder's own. <c>forge schedule</c> installs the host's family from
    /// them (STUDIO-27); <see cref="ForgeScheduleAdapters.ManualInstallCommand"/> is the same
    /// install, by hand.
    /// </summary>
    internal static void WriteScheduleArtifacts(string destination, string teamName, ForgeSchedule schedule, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamName);
        ArgumentNullException.ThrowIfNull(schedule);

        var directory = Path.Combine(destination, ScheduleDirectoryName);
        Directory.CreateDirectory(directory);

        var posixLauncher = Path.Combine(destination, PosixLauncherName);
        var windowsLauncher = Path.Combine(destination, WindowsLauncherName);
        File.WriteAllText(Path.Combine(directory, WindowsTaskScheduleAdapter.TaskFileName), WindowsTaskDefinition(teamName, windowsLauncher, schedule, now));
        File.WriteAllText(Path.Combine(directory, SystemdUserScheduleAdapter.ServiceName(teamName)), SystemdService(teamName, posixLauncher));
        File.WriteAllText(Path.Combine(directory, SystemdUserScheduleAdapter.TimerName(teamName)), SystemdTimer(teamName, schedule));
        File.WriteAllText(Path.Combine(directory, CronScheduleAdapter.LineFileName), CronFile(teamName, posixLauncher, schedule));
    }

    /// <summary>
    /// The Windows scheduled task (<c>schtasks /Create /XML</c>). The start boundary anchors the
    /// time of day at the next occurrence; Task Scheduler owns the recurrence after that. No
    /// principal: the task is the registering user's own, run while that user is logged on — no
    /// password asked, nothing elevated. The settings are the laptop's: the default would skip a
    /// run on battery, and a run missed while the machine was off happens when it next can, like
    /// the systemd timer's <c>Persistent=true</c>.
    /// </summary>
    internal static string WindowsTaskDefinition(string teamName, string windowsLauncher, ForgeSchedule schedule, DateTimeOffset now)
    {
        var start = schedule.Kind == ForgeSchedule.KindDaily
            ? NextOccurrence(now, schedule.Hour, schedule.Minute)
            : NextTopOfHour(now);
        var repetition = schedule.Kind == ForgeSchedule.KindHourly
            ? "      <Repetition><Interval>PT1H</Interval><Duration>P1D</Duration></Repetition>\n"
            : "";

        // The declared encoding must match the bytes on disk (UTF-8, no BOM) — schtasks accepts
        // UTF-8 XML; declaring UTF-16 over UTF-8 bytes would not parse.
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            "  <RegistrationInfo>\n" +
            $"    <Description>{SecurityElement.Escape($"Orkeon crew '{teamName}'")}</Description>\n" +
            "  </RegistrationInfo>\n" +
            "  <Triggers>\n" +
            "    <CalendarTrigger>\n" +
            string.Create(CultureInfo.InvariantCulture, $"      <StartBoundary>{start:yyyy-MM-dd'T'HH:mm:ss}</StartBoundary>\n") +
            "      <Enabled>true</Enabled>\n" +
            repetition +
            "      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>\n" +
            "    </CalendarTrigger>\n" +
            "  </Triggers>\n" +
            "  <Settings>\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
            "    <StartWhenAvailable>true</StartWhenAvailable>\n" +
            "  </Settings>\n" +
            "  <Actions Context=\"Author\">\n" +
            $"    {TaskAction(windowsLauncher)}\n" +
            "  </Actions>\n" +
            "</Task>\n";
    }

    /// <summary>
    /// The task's action running <paramref name="windowsLauncher"/> (STUDIO-51, decision 6):
    /// <c>cmd.exe</c> itself, under the quoting rule that does not depend on the path, the launcher
    /// its one argument — written again, it needs no reinstall. One line, the one
    /// <see cref="ScheduleArtifactsDescribe"/> looks for.
    /// </summary>
    private static string TaskAction(string windowsLauncher) =>
        $"<Exec><Command>{XmlText(WindowsTaskScheduleAdapter.TaskCommand)}</Command>"
        + $"<Arguments>{XmlText(WindowsTaskScheduleAdapter.TaskArguments(windowsLauncher))}</Arguments></Exec>";

    /// <summary>Text inside an XML element: the three characters markup reads, escaped — a quote stays one.</summary>
    private static string XmlText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>The systemd user service the timer starts.</summary>
    internal static string SystemdService(string teamName, string posixLauncher) =>
        $"[Unit]\nDescription=Orkeon crew '{teamName}'\n\n[Service]\nType=oneshot\nExecStart={SystemdUserScheduleAdapter.ExecStartValue(posixLauncher)}\n";

    /// <summary>The systemd user timer.</summary>
    internal static string SystemdTimer(string teamName, ForgeSchedule schedule)
    {
        var onCalendar = schedule.Kind == ForgeSchedule.KindDaily
            ? string.Create(CultureInfo.InvariantCulture, $"*-*-* {schedule.Hour:00}:{schedule.Minute:00}:00")
            : "hourly";
        return $"[Unit]\nDescription=Schedule for Orkeon crew '{teamName}'\n\n[Timer]\nOnCalendar={onCalendar}\nPersistent=true\n\n[Install]\nWantedBy=timers.target\n";
    }

    /// <summary>
    /// <c>cron.txt</c>: the cron line, marked with the team's tag — how <c>forge unschedule</c> finds
    /// it again among the user's own lines, and how a person does.
    /// </summary>
    internal static string CronFile(string teamName, string posixLauncher, ForgeSchedule schedule)
    {
        var cron = schedule.Kind == ForgeSchedule.KindDaily
            ? string.Create(CultureInfo.InvariantCulture, $"{schedule.Minute} {schedule.Hour} * * *")
            : "0 * * * *";
        return "# Generated by Orkeon Forge — `orkeon forge schedule` installs this line; by hand, append it to the crontab of the user who runs the crew.\n"
            + $"{cron} {CronScheduleAdapter.Command(posixLauncher)} {CronScheduleAdapter.Marker(CronScheduleAdapter.Tag(teamName))}\n";
    }

    /// <summary>
    /// Makes <c>schedule/</c> describe <paramref name="teamDirectory"/> as it is now before an
    /// install (STUDIO-27): the artifacts are left as they are — hand edits included — when they
    /// name this folder's launchers under <paramref name="teamName"/>, and regenerated from
    /// <paramref name="schedule"/> when they do not: a copy of a scheduled team, a team moved or
    /// renamed since its promotion, or artifacts deleted.
    /// </summary>
    internal static void EnsureScheduleArtifacts(string teamDirectory, string teamName, ForgeSchedule schedule, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        if (ScheduleArtifactsDescribe(teamDirectory, teamName))
            return;

        DeleteIfExists(Path.Combine(teamDirectory, ScheduleDirectoryName));
        WriteScheduleArtifacts(teamDirectory, teamName, schedule, now);
    }

    /// <summary>
    /// Whether every family's artifact names this folder's launcher, under <paramref name="teamName"/>,
    /// in the form this version writes: artifacts an earlier version wrote — a task running
    /// <c>run.cmd</c> by itself, an unescaped <c>ExecStart</c> or cron line (STUDIO-51) — are
    /// written again before an install.
    /// </summary>
    private static bool ScheduleArtifactsDescribe(string teamDirectory, string teamName)
    {
        var directory = Path.Combine(teamDirectory, ScheduleDirectoryName);
        var posixLauncher = Path.Combine(teamDirectory, PosixLauncherName);
        var windowsLauncher = Path.Combine(teamDirectory, WindowsLauncherName);

        return Mentions(Path.Combine(directory, WindowsTaskScheduleAdapter.TaskFileName), TaskAction(windowsLauncher))
            && Mentions(Path.Combine(directory, SystemdUserScheduleAdapter.ServiceName(teamName)), $"ExecStart={SystemdUserScheduleAdapter.ExecStartValue(posixLauncher)}\n")
            && File.Exists(Path.Combine(directory, SystemdUserScheduleAdapter.TimerName(teamName)))
            && Mentions(Path.Combine(directory, CronScheduleAdapter.LineFileName), $" {CronScheduleAdapter.Command(posixLauncher)} ");
    }

    /// <summary>
    /// The warning of a promotion whose <c>run.cmd</c> launches nothing (STUDIO-51, decision 8):
    /// the length, the bound and the longest option — names and numbers, never a value.
    /// </summary>
    internal static string LauncherTooLongWarning(TeamLauncherCommandLength length)
    {
        ArgumentNullException.ThrowIfNull(length);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"run.cmd launches nothing on Windows: its orkeon command is {length.Length} characters long, over the {TeamLauncherScript.WindowsCommandLimit} a cmd command holds — the longest option is {length.LongestOption}. run.sh is complete; shorten that value for the team to run on Windows.");
    }

    private static bool Mentions(string file, string text) =>
        File.Exists(file) && File.ReadAllText(file).Contains(text, StringComparison.Ordinal);

    /// <summary>Everything <c>FORGE.md</c> says, gathered from one promotion.</summary>
    private sealed record ForgeCard
    {
        /// <summary>The promoted session.</summary>
        public required ForgeSession Session { get; init; }

        /// <summary>The team's name as the artifacts carry it (<see cref="ArtifactName"/>): the title when the session has none.</summary>
        public required string TeamName { get; init; }

        /// <summary>What the crew promised, when the session holds a brief.</summary>
        public ForgeBrief? Brief { get; init; }

        /// <summary>How the trial was judged, when one was recorded.</summary>
        public ForgeVerdict? Verdict { get; init; }

        /// <summary>The requested schedule, when <c>--schedule</c> asked for one.</summary>
        public ForgeSchedule? Schedule { get; init; }

        /// <summary>The command the card displays for a person installing by hand.</summary>
        public string? InstallCommand { get; init; }

        /// <summary>When the folder was promoted.</summary>
        public required DateTimeOffset Now { get; init; }

        /// <summary>The mounts the launchers carry.</summary>
        public required IReadOnlyList<DeliverableMount> WriteMounts { get; init; }
    }

    /// <summary>
    /// The card's language: it talks to the crew's owner, in the brief's tongue, not to the
    /// framework.
    /// </summary>
    private sealed record CardLanguage(bool IsFrench)
    {
        /// <summary>Picks the sentence this card is written in.</summary>
        public string Pick(string french, string english) => IsFrench ? french : english;
    }

    /// <summary>
    /// <c>FORGE.md</c> — the identity card a colleague reads when picking up the folder:
    /// where the crew comes from, what it promises, how it was judged. Written in the
    /// brief's language: the card talks to the crew's owner, not to the framework.
    /// </summary>
    private static void WriteCard(string destination, ForgeCard content)
    {
        var language = new CardLanguage(
            string.Equals(content.Brief?.Language, "fr", StringComparison.OrdinalIgnoreCase));

        var card = new StringBuilder();
        card.AppendLine(CultureInfo.InvariantCulture,
            $"# {content.Session.Document.Title ?? content.TeamName}");
        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture,
            $"> {language.Pick("Généré par l'Atelier Orkeon le", "Generated by the Orkeon Forge on")} {content.Now:yyyy-MM-dd} — Orkeon {ForgeEngineVersion.Current}.");

        // Where the team's structure came from (STUDIO-40, D-04), next to where the team came from.
        if (content.Session.Document.Reference is { Id.Length: > 0 } reference)
        {
            card.AppendLine(">");
            card.AppendLine(CultureInfo.InvariantCulture,
                $"> {language.Pick("Inspiré de :", "Inspired by:")} {reference.Title ?? reference.Id} ({reference.Id}).");
        }

        AppendBriefSections(card, content.Brief, language);
        AppendVerdictSection(card, content.Verdict, language);
        AppendLaunchSection(card, content.Session, content.WriteMounts, language);
        AppendFoldersSection(card, content.WriteMounts, language);
        AppendScheduleSection(card, content.Schedule, content.InstallCommand, language);

        File.WriteAllText(Path.Combine(destination, CardFileName), card.ToString());
    }

    /// <summary>What the crew was asked for: goal, expected output, constraints, criteria.</summary>
    private static void AppendBriefSections(StringBuilder card, ForgeBrief? brief, CardLanguage language)
    {
        if (brief is null)
            return;

        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Objectif", "Goal")}");
        card.AppendLine();
        card.AppendLine(brief.Goal ?? "");
        if (!string.IsNullOrWhiteSpace(brief.Context))
            card.AppendLine(CultureInfo.InvariantCulture, $"\n{brief.Context}");

        if (brief.ExpectedOutput is { } output)
        {
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Sortie attendue", "Expected output")}");
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture,
                $"{output.Description ?? ""}{(output.Format is null ? "" : $" ({output.Format})")}");
        }

        if (brief.Constraints is { Count: > 0 } constraints)
        {
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Contraintes", "Constraints")}");
            card.AppendLine();
            foreach (var constraint in constraints)
                card.AppendLine(CultureInfo.InvariantCulture, $"- {constraint}");
        }

        if (brief.Acceptance is { Count: > 0 } acceptance)
        {
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Critères d'acceptation", "Acceptance criteria")}");
            card.AppendLine();
            foreach (var criterion in acceptance)
                card.AppendLine(CultureInfo.InvariantCulture,
                    $"- **{criterion.Id}** ({criterion.Kind}) — {criterion.Statement}");
        }
    }

    /// <summary>How the trial was judged — or that nothing judged it.</summary>
    private static void AppendVerdictSection(StringBuilder card, ForgeVerdict? verdict, CardLanguage language)
    {
        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Verdict", "Verdict")}");
        card.AppendLine();

        if (verdict is null)
        {
            card.AppendLine(language.Pick("Aucun verdict enregistré pour cette session.",
                "No verdict was recorded for this session."));
            return;
        }

        var conformity = verdict.Passing
            ? language.Pick("conforme", "conforming")
            : language.Pick("accepté sur pièce (non conforme)", "accepted as-is (not conforming)");
        card.AppendLine(CultureInfo.InvariantCulture,
            $"Score {verdict.Score.ToString("0.00", CultureInfo.InvariantCulture)} — {conformity} ({language.Pick("juge", "judge")}: {verdict.Judge}).");
        foreach (var finding in verdict.Findings)
            card.AppendLine(CultureInfo.InvariantCulture,
                $"- [{finding.Severity}] {finding.Statement}{(finding.Acceptance is null ? "" : $" ({finding.Acceptance})")}");
    }

    /// <summary>
    /// How to launch the team. The launchers, and only the launchers: `orkeon run
    /// &lt;dir&gt;/crew` does start the crew, but WITHOUT the --mount arguments run.sh
    /// supplies — so a team that writes deliverables writes nothing that way, and reports
    /// success. The CLI reference and both getting-started pages carry that caveat; this
    /// card is what the colleague receiving the folder reads, and it was the one surface
    /// still recommending the bare command.
    /// </summary>
    private static void AppendLaunchSection(
        StringBuilder card, ForgeSession session, IReadOnlyList<DeliverableMount> writeMounts, CardLanguage language)
    {
        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Lancer l'équipe", "Run the crew")}");
        card.AppendLine();

        var target = RunTarget(session);
        var bareCommand = writeMounts.Count > 0
            ? language.Pick(
                $" Passez par eux : `orkeon run {target}` démarre bien l'équipe, mais sans les `--mount` que les lanceurs fournissent, donc les livrables ne sont écrits nulle part et l'exécution se déclare réussie.",
                $" Use them: `orkeon run {target}` does start the crew, but without the `--mount` arguments the launchers supply, so the deliverables are written nowhere and the run reports success.")
            : language.Pick(
                $" Le dossier est ordinaire : `orkeon run {target}` le lance aussi.",
                $" The folder is ordinary: `orkeon run {target}` launches it too.");

        card.AppendLine(
            language.Pick(
                $"`./{PosixLauncherName}` (Linux/macOS) ou `{WindowsLauncherName}` (Windows) — les entrées d'exemple y sont à adapter.",
                $"`./{PosixLauncherName}` (Linux/macOS) or `{WindowsLauncherName}` (Windows) — adapt the sample inputs inside.")
            + bareCommand
            + language.Pick(" Orkeon Studio détecte le dossier.", " Orkeon Studio detects the folder."));
    }

    /// <summary>Where the mounts land, so the folder explains itself.</summary>
    private static void AppendFoldersSection(
        StringBuilder card, IReadOnlyList<DeliverableMount> writeMounts, CardLanguage language)
    {
        if (writeMounts.Count == 0)
            return;

        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Les dossiers de cette équipe", "This team's folders")}");
        card.AppendLine();
        card.AppendLine(language.Pick(
            "Les agents n'adressent que des points de montage. Les lanceurs relient ceux-ci à des dossiers de l'équipe — déplacez le dossier, les liens suivent :",
            "Agents only ever address mount points. The launchers bind these to folders inside the team — move the folder and the bindings follow:"));
        card.AppendLine();
        foreach (var mount in writeMounts)
            card.AppendLine(CultureInfo.InvariantCulture,
                $"- `{mount.VirtualRoot}` {(mount.ReadOnly ? language.Pick("lecture", "read") : language.Pick("écriture", "write"))} → `{mount.Folder}/`");

        var readFolders = writeMounts.Where(m => m.ReadOnly).Select(m => $"`{m.Folder}/`").ToList();
        if (readFolders.Count == 0)
            return;

        var listed = string.Join(", ", readFolders);
        card.AppendLine();
        card.AppendLine(language.Pick(
            $"Déposez dans {listed} ce que l'équipe doit lire. Elle ne lit que ses dossiers d'entrée : la racine de l'équipe n'est pas montée, pour que l'`{SettingsFileName}` qui peut s'y trouver reste hors de portée des agents.",
            $"Drop what the team should read into {listed}. It reads its input folders only: the team's root is not mounted, so the `{SettingsFileName}` that may sit there stays out of the agents' reach."));
    }

    /// <summary>
    /// The schedule: the artifacts, who installs them — Orkeon Studio with the user's consent, or
    /// <c>forge schedule</c> (STUDIO-27, DA-3) — and the command that installs them by hand.
    /// </summary>
    private static void AppendScheduleSection(
        StringBuilder card, ForgeSchedule? schedule, string? installCommand, CardLanguage language)
    {
        if (schedule is null)
            return;

        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {language.Pick("Planification", "Schedule")}");
        card.AppendLine();
        card.AppendLine(language.Pick(
            $"Les artefacts sous `{ScheduleDirectoryName}/` couvrent les trois plateformes (tâche planifiée Windows, timer systemd, ligne cron). Orkeon n'a pas d'ordonnanceur à lui : c'est le système qui lance l'équipe. Orkeon Studio installe la planification avec votre accord ; en ligne de commande, depuis ce dossier, `orkeon forge schedule .` l'installe et `orkeon forge unschedule .` la retire. À la main, sur cette machine :",
            $"The artifacts under `{ScheduleDirectoryName}/` cover the three platforms (Windows scheduled task, systemd timer, cron line). Orkeon has no scheduler of its own: the operating system runs the team. Orkeon Studio installs the schedule with your consent; from a terminal, in this folder, `orkeon forge schedule .` installs it and `orkeon forge unschedule .` removes it. By hand, on this machine:"));
        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"```\n{installCommand}\n```");
    }

    private static DateTime NextOccurrence(DateTimeOffset now, int hour, int minute)
    {
        var candidate = now.UtcDateTime.Date.AddHours(hour).AddMinutes(minute);
        return candidate <= now.UtcDateTime ? candidate.AddDays(1) : candidate;
    }

    private static DateTime NextTopOfHour(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1);
    }

    /// <summary>Recursive copy; the target is created, existing files are not expected there.</summary>
    internal static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var subdirectory in Directory.GetDirectories(source))
            CopyDirectory(subdirectory, Path.Combine(target, Path.GetFileName(subdirectory)));
    }
}
