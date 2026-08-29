using Orkeon.Constants.FileSystem;
using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// A parsed <c>--schedule</c> value (SPEC-ORKEON-FORGE §11). Two spellings cover the
/// archetypal needs of the audience: <c>daily@HH:mm</c> ("chaque matin") and
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

    /// <summary>The host platform's install command — displayed, never executed (§3.4).</summary>
    public string? InstallCommand { get; init; }

    /// <summary>
    /// Whether an existing promotion of the same session was updated in place (W-09) —
    /// clients warn that regenerated files (run.sh, crew/) lost any hand edits.
    /// </summary>
    public bool Updated { get; init; }
}

/// <summary>
/// Promotion of a Ready session (SPEC-ORKEON-FORGE §11): the crew leaves the session
/// directory as an ordinary folder — <c>crew/</c>, <c>run.cmd</c>/<c>run.sh</c>,
/// <c>FORGE.md</c>, and on request a <c>schedule/</c> of generated artifacts. The launch
/// scripts are composed here, in the CLI, against the CLI's own <c>orkeon run</c> grammar
/// (never Studio's mirror), so they cannot drift from what the runner actually parses.
/// Nothing here is proprietary to the forge: <c>orkeon run</c> launches the folder,
/// Studio's launcher detects it.
/// </summary>
internal static class ForgePromoter
{
    /// <summary>Name of the launcher script on Windows.</summary>
    public const string WindowsLauncherName = "run.cmd";

    /// <summary>Name of the launcher script everywhere else.</summary>
    public const string PosixLauncherName = "run.sh";

    /// <summary>Name of the generated identity card.</summary>
    public const string CardFileName = "FORGE.md";

    /// <summary>Name of the schedule artifact directory.</summary>
    public const string ScheduleDirectoryName = "schedule";

    /// <summary>Name of the copied settings file, when <c>--with-settings</c> asked for it.</summary>
    public const string SettingsFileName = "appsettings.json";

    /// <summary>The platform this process runs on.</summary>
    public static ForgePromotePlatform DetectPlatform() =>
        OperatingSystem.IsWindows() ? ForgePromotePlatform.Windows
        : OperatingSystem.IsLinux() ? ForgePromotePlatform.Linux
        : ForgePromotePlatform.Other;

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
        // very session already promoted to. Re-adoption then UPDATES it in place: the
        // generated artifacts (crew/, schedule/, launchers, FORGE.md) are regenerated,
        // everything else — sidecar, user files, outputs — is preserved. Omitting the
        // schedule on a re-adoption removes schedule/: the folder says what is true.
        var updating = false;
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            if (!IsSameDirectory(session.Document.PromotedTo, destination))
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
        var verdict = session.TryLoadArtifact<ForgeVerdict>("verdict.json");

        // The folders the crew reads and writes. The trial bench mounted /output and
        // /workspace; nothing else did, so a promoted team used to run "successfully",
        // write nothing at all and read nothing at all. The launchers now carry the mounts
        // the blueprint asks for, and the folders exist before the first launch — an absent
        // mount base path is fatal at host build.
        var writeMounts = DeliverableMounts(session);
        foreach (var mount in writeMounts)
            Directory.CreateDirectory(Path.Combine(destination, mount.Folder));

        WritePosixLauncher(destination, session, brief, settingsReference, settingsIsRelative, writeMounts);
        WriteWindowsLauncher(destination, session, brief, settingsReference, settingsIsRelative, writeMounts);

        string? installCommand = null;
        string? scheduleDirectory = null;
        if (schedule is not null)
        {
            scheduleDirectory = ScheduleDirectoryName;
            installCommand = WriteScheduleArtifacts(destination, session, schedule, platform, now);
        }

        WriteCard(destination, session, brief, verdict, schedule, installCommand, now, writeMounts);

        return new ForgePromotionResult
        {
            Destination = destination,
            Launcher = platform == ForgePromotePlatform.Windows ? WindowsLauncherName : PosixLauncherName,
            ScheduleDirectory = scheduleDirectory,
            InstallCommand = installCommand,
            Updated = updating,
        };
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is the very folder this session already
    /// promoted to — full-path, trailing-separator-blind, case-blind on Windows.
    /// </summary>
    private static bool IsSameDirectory(string? promotedTo, string candidate)
    {
        if (string.IsNullOrWhiteSpace(promotedTo))
            return false;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(promotedTo)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)),
            comparison);
    }

    private static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// The `orkeon run` invocation both launchers spell, one pre-quoted token per entry:
    /// the promoted crew directory, the settings when one is known, and the brief's sample
    /// inputs so the folder runs out of the box — the scripts say in a comment that these
    /// are the inputs to adapt. Paths inside the folder are anchored to the script's own
    /// directory (<c>$DIR</c> / <c>%~dp0</c>), double-quoted so the shell expands the
    /// anchor; literal values get the shell's literal quoting.
    /// </summary>
    private static List<string> RunArguments(
        ForgeSession session,
        ForgeBrief? brief,
        string? settingsReference,
        bool settingsIsRelative,
        bool posix,
        IReadOnlyList<DeliverableMount> writeMounts)
    {
        string Anchored(string relative) => posix ? $"\"$DIR/{relative}\"" : $"\"%~dp0{relative}\"";
        // One shell-quoted token carrying the whole spec, with the mount grammar's own
        // quotes (\" survives both shells as a literal '"') around the physical segment.
        string GrammarQuotedMount(string relative, string virtualRoot, bool readOnly)
        {
            var rights = readOnly ? "ro" : "rw";
            return posix
                ? $"\"\\\"$DIR/{relative}\\\":{virtualRoot}:{rights}\""
                : $"\"\\\"%~dp0{relative}\\\":{virtualRoot}:{rights}\"";
        }
        string Literal(string value) => posix ? ShQuote(value) : CmdQuote(value);

        var script = ForgeSession.IsScriptFormat(session.Document.Format);
        var segments = new List<string>
        {
            $"run {Anchored(RunTarget(session))}",
        };

        if (settingsReference is not null)
            segments.Add($"--settings {(settingsIsRelative ? Anchored(settingsReference) : Literal(settingsReference))}");

        // Several mounts go space-separated after ONE --mount: the CLI's parser rejects a
        // repeated option. The anchor keeps the folder relocatable with the team.
        //
        // The physical segment carries the mount grammar's own quotes (\" survives both
        // shells as a literal '"'), because the anchor expands to a path nobody controls at
        // generation time. On Windows it always contains a ':' — %~dp0 is C:\… — so the
        // unquoted spec split into four segments and EVERY promoted team died at start with
        // a grammar error naming a path the user never typed. A destination path containing
        // a literal '"' is the one case no launcher can escape from the outside; the CLI's
        // own --mount handles it.
        if (writeMounts.Count > 0)
        {
            var specs = writeMounts.Select(m => GrammarQuotedMount(m.Folder, m.VirtualRoot, m.ReadOnly));
            segments.Add($"--mount {string.Join(' ', specs)}");
        }

        // The sample inputs only exist on the YAML path: `orkeon run` documents
        // --var/--initial-context as ignored for .ork.ts crews, and spelling ignored
        // flags would be a lie in a file people copy from.
        if (!script)
        {
            // Same single-flag rule as --mount: several values go space-separated after ONE
            // --var. Emitting the flag per variable made any brief with two sample inputs
            // promote to a team the parser refuses ("option repeated").
            var variables = (brief?.Sample?.Variables ?? [])
                .Select(pair => Literal($"{pair.Key}={pair.Value}"))
                .ToList();
            if (variables.Count > 0)
                segments.Add($"--var {string.Join(' ', variables)}");

            if (!string.IsNullOrWhiteSpace(brief?.Sample?.InitialContext))
                segments.Add($"--initial-context {Literal(brief.Sample.InitialContext)}");
        }

        return segments;
    }

    /// <summary>
    /// One writable mount the promoted folder carries: a virtual root the blueprint writes
    /// to, backed by a folder inside the team.
    /// </summary>
    /// <param name="VirtualRoot">The root the agents address, e.g. <c>/output</c>.</param>
    /// <param name="Folder">Its folder inside the promoted directory, e.g. <c>output</c>.</param>
    /// <param name="ReadOnly">Whether the team only reads it (the <c>/workspace</c> input folder).</param>
    private sealed record DeliverableMount(string VirtualRoot, string Folder, bool ReadOnly = false);

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
    private const string ReadVirtualRoot = "/workspace";

    /// <summary>Folder inside the promoted team backing <see cref="ReadVirtualRoot"/>.</summary>
    private const string ReadFolderName = "input";

    /// <summary>Tools whose presence means the team expects something to read.</summary>
    private static readonly string[] ReadingTools = ["file_read", "directory_read"];

    /// <summary>The virtual roots the runners mount for themselves — never a deliverable's.</summary>
    private static readonly string[] ReservedVirtualRoots =
        [RunnerVirtualRoots.Crew, RunnerVirtualRoots.Script, RunnerVirtualRoots.LlmLogs];

    /// <summary>
    /// The virtual roots the blueprint's deliverables are written to — the same derivation
    /// the Composer shows as chips, applied where it becomes true: the launcher.
    /// <para>
    /// Without this, a team that passed its trial (where the bench mounts <c>/output</c>)
    /// had no <c>/output</c> at all once adopted: the deliverable resolver caught the access
    /// denial, logged a warning and reported the run as finished, so the folder stayed empty
    /// and nothing on screen said why.
    /// </para>
    /// </summary>
    private static List<DeliverableMount> DeliverableMounts(ForgeSession session)
    {
        var blueprint = session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName);
        var roots = new List<DeliverableMount>();

        // Read first, so the chips and the command line list it the way the Composer does.
        if ((blueprint?.Agents ?? []).Any(a => (a.Tools ?? []).Intersect(ReadingTools, StringComparer.Ordinal).Any()))
            roots.Add(new DeliverableMount(ReadVirtualRoot, ReadFolderName, ReadOnly: true));

        foreach (var task in blueprint?.Tasks ?? [])
        {
            if (task.Deliverable is not { Length: > 1 } deliverable || deliverable[0] != '/')
                continue;

            var slash = deliverable.IndexOf('/', 1);
            var root = slash > 1 ? deliverable[..slash] : deliverable;
            if (root.Length <= 1)
                continue;

            var folder = root[1..];
            // A deliverable root is a single segment by construction; anything else would
            // put the team's own files outside its folder. The blueprint is LLM-authored, so
            // '..' and '.' are refused explicitly rather than trusted to be absent.
            // ':' and ';' are refused for the same reason, one level down: they are the mount
            // grammar's own separators, so a root carrying either spells a --mount the runner
            // cannot parse and the promoted team dies at every launch. ForgeBlueprint.Validate
            // reports it to the model, where a repair turn can rename the folder; this is the
            // guard for a blueprint that reached here anyway.
            if (folder.Contains('/', StringComparison.Ordinal)
                || folder.Contains('\\', StringComparison.Ordinal)
                || folder.AsSpan().ContainsAny(':', ';')
                || folder is "." or "..")
            {
                continue;
            }

            // A root the runner keeps for itself would make the promoted team unlaunchable:
            // the very --mount the launcher spells is refused at start (ADR-008, decision 5).
            if (ReservedVirtualRoots.Contains(root, StringComparer.Ordinal))
                continue;

            // The read mount is derived first, so a deliverable landing under the SAME root
            // meets a read-only entry. Skipping on the name alone left the team with
            // `/workspace:ro` and a deliverable it could never write — the run reports success
            // and produces nothing. Studio deduped the other way and showed two /workspace
            // chips, one promising a write that was then silently dropped. One root, one
            // mount, and a write requirement wins over a read one.
            var existing = roots.FindIndex(m => string.Equals(m.VirtualRoot, root, StringComparison.Ordinal));
            if (existing < 0)
                roots.Add(new DeliverableMount(root, folder));
            else if (roots[existing].ReadOnly)
                roots[existing] = roots[existing] with { ReadOnly = false };
        }

        return roots;
    }

    /// <summary>What `orkeon run` targets, relative to the promoted folder.</summary>
    private static string RunTarget(ForgeSession session) =>
        ForgeSession.IsScriptFormat(session.Document.Format)
            ? $"{ForgeYamlRenderer.CrewDirectoryName}/{ForgeScriptRenderer.ScriptFileName}"
            : ForgeYamlRenderer.CrewDirectoryName;

    private static void WritePosixLauncher(
        string destination, ForgeSession session, ForgeBrief? brief,
        string? settingsReference, bool settingsIsRelative,
        IReadOnlyList<DeliverableMount> writeMounts)
    {
        var builder = new StringBuilder();
        builder.Append("#!/usr/bin/env sh\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"# Generated by Orkeon Forge (session '{session.Document.Slug}').\n");
        builder.Append(ForgeSession.IsScriptFormat(session.Document.Format)
            ? "# The crew lives in crew/crew.ork.ts — edit it there, this script only launches it.\n"
            : "# The --var/--initial-context lines below are the test sample: adapt them to the real run.\n");
        // Resolve symlinks before taking the directory. Symlinking "run this team" onto PATH is
        // normal for a folder the card calls ordinary, and `dirname "$0"` on the LINK gives
        // ~/bin — so the launcher mounted ~/bin/output and died naming folders the user never
        // created. Plain `readlink` (no -f) is portable where GNU's -f is not. The `|| exit`
        // matters too: without it a failed cd left the script running in the caller's cwd.
        builder.Append("SELF=\"$0\"\n");
        builder.Append("while [ -L \"$SELF\" ]; do\n");
        builder.Append("  LINK=\"$(readlink \"$SELF\")\"\n");
        builder.Append("  case \"$LINK\" in\n");
        builder.Append("    /*) SELF=\"$LINK\" ;;\n");
        builder.Append("    *) SELF=\"$(dirname \"$SELF\")/$LINK\" ;;\n");
        builder.Append("  esac\n");
        builder.Append("done\n");
        builder.Append("DIR=\"$(cd \"$(dirname \"$SELF\")\" && pwd)\" || exit 1\n");
        // Run FROM the team's folder. The runner refuses to read a crew outside the working
        // directory without --allow-external-mounts, and the security whitelist is rooted on
        // the working directory too — so a launch from anywhere else (a scheduled task starts
        // in the system directory) was refused outright, or wrote nothing into /output.
        builder.Append("cd \"$DIR\" || exit 1\n");
        builder.Append("exec orkeon");

        foreach (var segment in RunArguments(session, brief, settingsReference, settingsIsRelative, posix: true, writeMounts))
            builder.Append(" \\\n  ").Append(segment);

        builder.Append('\n');

        var path = Path.Combine(destination, PosixLauncherName);
        File.WriteAllText(path, builder.ToString());
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void WriteWindowsLauncher(
        string destination, ForgeSession session, ForgeBrief? brief,
        string? settingsReference, bool settingsIsRelative,
        IReadOnlyList<DeliverableMount> writeMounts)
    {
        var builder = new StringBuilder();
        builder.Append("@echo off\r\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"rem Generated by Orkeon Forge (session '{session.Document.Slug}').\r\n");
        builder.Append(ForgeSession.IsScriptFormat(session.Document.Format)
            ? "rem The crew lives in crew\\crew.ork.ts — edit it there, this script only launches it.\r\n"
            : "rem The --var/--initial-context values below are the test sample: adapt them to the real run.\r\n");
        // Run FROM the team's folder — see WritePosixLauncher for why.
        builder.Append("cd /d \"%~dp0\" || exit /b 1\r\n");
        builder.Append("orkeon");

        foreach (var segment in RunArguments(session, brief, settingsReference, settingsIsRelative, posix: false, writeMounts))
            builder.Append(' ').Append(segment);

        builder.Append("\r\n");
        File.WriteAllText(Path.Combine(destination, WindowsLauncherName), builder.ToString());
    }

    /// <summary>
    /// Writes all three schedule families under <c>schedule/</c> — the "choice" of §11 is
    /// made at install time, not at generation time: the folder is portable and the
    /// artifacts are a few hundred bytes. Returns the install command for
    /// <paramref name="platform"/>, which the caller displays and never executes.
    /// </summary>
    private static string WriteScheduleArtifacts(
        string destination, ForgeSession session, ForgeSchedule schedule,
        ForgePromotePlatform platform, DateTimeOffset now)
    {
        var slug = session.Document.Slug;
        var directory = Path.Combine(destination, ScheduleDirectoryName);
        Directory.CreateDirectory(directory);

        var posixLauncher = Path.Combine(destination, PosixLauncherName);
        var windowsLauncher = Path.Combine(destination, WindowsLauncherName);

        // Windows scheduled task (schtasks /Create /XML). The start boundary anchors the
        // time of day at the next occurrence; Task Scheduler owns the recurrence after that.
        var start = schedule.Kind == ForgeSchedule.KindDaily
            ? NextOccurrence(now, schedule.Hour, schedule.Minute)
            : NextTopOfHour(now);
        var repetition = schedule.Kind == ForgeSchedule.KindHourly
            ? "      <Repetition><Interval>PT1H</Interval><Duration>P1D</Duration></Repetition>\n"
            : "";
        File.WriteAllText(Path.Combine(directory, "windows-task.xml"),
            // The declared encoding must match the bytes on disk (UTF-8, no BOM) —
            // schtasks accepts UTF-8 XML; declaring UTF-16 over UTF-8 bytes would not parse.
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            "  <Triggers>\n" +
            "    <CalendarTrigger>\n" +
            string.Create(CultureInfo.InvariantCulture, $"      <StartBoundary>{start:yyyy-MM-dd'T'HH:mm:ss}</StartBoundary>\n") +
            "      <Enabled>true</Enabled>\n" +
            repetition +
            "      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>\n" +
            "    </CalendarTrigger>\n" +
            "  </Triggers>\n" +
            "  <Actions Context=\"Author\">\n" +
            $"    <Exec><Command>{SecurityElement.Escape(windowsLauncher)}</Command></Exec>\n" +
            "  </Actions>\n" +
            "</Task>\n");

        // systemd user unit pair.
        var onCalendar = schedule.Kind == ForgeSchedule.KindDaily
            ? string.Create(CultureInfo.InvariantCulture, $"*-*-* {schedule.Hour:00}:{schedule.Minute:00}:00")
            : "hourly";
        File.WriteAllText(Path.Combine(directory, $"orkeon-{slug}.service"),
            $"[Unit]\nDescription=Orkeon crew '{slug}'\n\n[Service]\nType=oneshot\nExecStart=\"{posixLauncher}\"\n");
        File.WriteAllText(Path.Combine(directory, $"orkeon-{slug}.timer"),
            $"[Unit]\nDescription=Schedule for Orkeon crew '{slug}'\n\n[Timer]\nOnCalendar={onCalendar}\nPersistent=true\n\n[Install]\nWantedBy=timers.target\n");

        // cron line.
        var cron = schedule.Kind == ForgeSchedule.KindDaily
            ? string.Create(CultureInfo.InvariantCulture, $"{schedule.Minute} {schedule.Hour} * * *")
            : "0 * * * *";
        File.WriteAllText(Path.Combine(directory, "cron.txt"),
            $"# Generated by Orkeon Forge — append to the crontab of the user who runs the crew.\n{cron} \"{posixLauncher}\"\n");

        return platform switch
        {
            ForgePromotePlatform.Windows =>
                $"schtasks /Create /TN \"Orkeon {slug}\" /XML \"{Path.Combine(directory, "windows-task.xml")}\"",
            ForgePromotePlatform.Linux =>
                $"cp \"{Path.Combine(directory, $"orkeon-{slug}.service")}\" \"{Path.Combine(directory, $"orkeon-{slug}.timer")}\" ~/.config/systemd/user/ " +
                $"&& systemctl --user daemon-reload && systemctl --user enable --now orkeon-{slug}.timer",
            _ =>
                $"( crontab -l 2>/dev/null; cat \"{Path.Combine(directory, "cron.txt")}\" ) | crontab -",
        };
    }

    /// <summary>
    /// <c>FORGE.md</c> — the identity card a colleague reads when picking up the folder:
    /// where the crew comes from, what it promises, how it was judged. Written in the
    /// brief's language: the card talks to the crew's owner, not to the framework.
    /// </summary>
    private static void WriteCard(
        string destination, ForgeSession session, ForgeBrief? brief, ForgeVerdict? verdict,
        ForgeSchedule? schedule, string? installCommand, DateTimeOffset now,
        IReadOnlyList<DeliverableMount> writeMounts)
    {
        var fr = string.Equals(brief?.Language, "fr", StringComparison.OrdinalIgnoreCase);
        string L(string french, string english) => fr ? french : english;

        var card = new StringBuilder();
        card.AppendLine(CultureInfo.InvariantCulture,
            $"# {session.Document.Title ?? session.Document.Slug}");
        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture,
            $"> {L("Généré par l'Atelier Orkeon le", "Generated by the Orkeon Forge on")} {now:yyyy-MM-dd} — Orkeon {OrkeonVersion()}.");

        if (brief is not null)
        {
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Objectif", "Goal")}");
            card.AppendLine();
            card.AppendLine(brief.Goal ?? "");
            if (!string.IsNullOrWhiteSpace(brief.Context))
                card.AppendLine(CultureInfo.InvariantCulture, $"\n{brief.Context}");

            if (brief.ExpectedOutput is { } output)
            {
                card.AppendLine();
                card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Sortie attendue", "Expected output")}");
                card.AppendLine();
                card.AppendLine(CultureInfo.InvariantCulture,
                    $"{output.Description ?? ""}{(output.Format is null ? "" : $" ({output.Format})")}");
            }

            if (brief.Constraints is { Count: > 0 } constraints)
            {
                card.AppendLine();
                card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Contraintes", "Constraints")}");
                card.AppendLine();
                foreach (var constraint in constraints)
                    card.AppendLine(CultureInfo.InvariantCulture, $"- {constraint}");
            }

            if (brief.Acceptance is { Count: > 0 } acceptance)
            {
                card.AppendLine();
                card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Critères d'acceptation", "Acceptance criteria")}");
                card.AppendLine();
                foreach (var criterion in acceptance)
                    card.AppendLine(CultureInfo.InvariantCulture,
                        $"- **{criterion.Id}** ({criterion.Kind}) — {criterion.Statement}");
            }
        }

        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Verdict", "Verdict")}");
        card.AppendLine();
        if (verdict is null)
        {
            card.AppendLine(L("Aucun verdict enregistré pour cette session.",
                "No verdict was recorded for this session."));
        }
        else
        {
            var conformity = verdict.Passing
                ? L("conforme", "conforming")
                : L("accepté sur pièce (non conforme)", "accepted as-is (not conforming)");
            card.AppendLine(CultureInfo.InvariantCulture,
                $"Score {verdict.Score.ToString("0.00", CultureInfo.InvariantCulture)} — {conformity} ({L("juge", "judge")}: {verdict.Judge}).");
            foreach (var finding in verdict.Findings)
                card.AppendLine(CultureInfo.InvariantCulture,
                    $"- [{finding.Severity}] {finding.Statement}{(finding.Acceptance is null ? "" : $" ({finding.Acceptance})")}");
        }

        card.AppendLine();
        card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Lancer l'équipe", "Run the crew")}");
        card.AppendLine();
        // The launchers, and only the launchers. `orkeon run <dir>/crew` does start the crew,
        // but WITHOUT the --mount arguments run.sh supplies — so a team that writes
        // deliverables writes nothing that way, and reports success. The CLI reference and both
        // getting-started pages carry that caveat; this card is what the colleague receiving
        // the folder reads, and it was the one surface still recommending the bare command.
        var deliverableCaveat = writeMounts.Count > 0;
        card.AppendLine(fr
            ? $"`./{PosixLauncherName}` (Linux/macOS) ou `{WindowsLauncherName}` (Windows) — les entrées d'exemple y sont à adapter."
              + (deliverableCaveat
                  ? $" Passez par eux : `orkeon run {RunTarget(session)}` démarre bien l'équipe, mais sans les `--mount` que les lanceurs fournissent, donc les livrables ne sont écrits nulle part et l'exécution se déclare réussie."
                  : $" Le dossier est ordinaire : `orkeon run {RunTarget(session)}` le lance aussi.")
              + " Orkeon Studio détecte le dossier."
            : $"`./{PosixLauncherName}` (Linux/macOS) or `{WindowsLauncherName}` (Windows) — adapt the sample inputs inside."
              + (deliverableCaveat
                  ? $" Use them: `orkeon run {RunTarget(session)}` does start the crew, but without the `--mount` arguments the launchers supply, so the deliverables are written nowhere and the run reports success."
                  : $" The folder is ordinary: `orkeon run {RunTarget(session)}` launches it too.")
              + " Orkeon Studio detects the folder.");

        if (writeMounts.Count > 0)
        {
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Les dossiers de cette équipe", "This team's folders")}");
            card.AppendLine();
            card.AppendLine(fr
                ? "Les agents n'adressent que des points de montage. Les lanceurs relient ceux-ci à des dossiers de l'équipe — déplacez le dossier, les liens suivent :"
                : "Agents only ever address mount points. The launchers bind these to folders inside the team — move the folder and the bindings follow:");
            card.AppendLine();
            foreach (var mount in writeMounts)
                card.AppendLine(CultureInfo.InvariantCulture,
                    $"- `{mount.VirtualRoot}` {(mount.ReadOnly ? L("lecture", "read") : L("écriture", "write"))} → `{mount.Folder}/`");

            if (writeMounts.Any(m => m.ReadOnly))
            {
                card.AppendLine();
                card.AppendLine(fr
                    ? $"Déposez dans `{ReadFolderName}/` ce que l'équipe doit lire. C'est le seul dossier qu'elle lit : la racine de l'équipe n'est pas montée, pour que l'`{SettingsFileName}` qui peut s'y trouver reste hors de portée des agents."
                    : $"Drop what the team should read into `{ReadFolderName}/`. It is the only folder it reads: the team's root is not mounted, so the `{SettingsFileName}` that may sit there stays out of the agents' reach.");
            }
        }

        if (schedule is not null)
        {
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"## {L("Planification", "Schedule")}");
            card.AppendLine();
            card.AppendLine(fr
                ? $"Les artefacts sous `{ScheduleDirectoryName}/` couvrent les trois plateformes (tâche planifiée Windows, timer systemd, ligne cron). Orkeon n'a pas d'ordonnanceur : installez l'artefact vous-même — par exemple :"
                : $"The artifacts under `{ScheduleDirectoryName}/` cover the three platforms (Windows scheduled task, systemd timer, cron line). Orkeon has no scheduler: install the artifact yourself — for example:");
            card.AppendLine();
            card.AppendLine(CultureInfo.InvariantCulture, $"```\n{installCommand}\n```");
        }

        File.WriteAllText(Path.Combine(destination, CardFileName), card.ToString());
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

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var subdirectory in Directory.GetDirectories(source))
            CopyDirectory(subdirectory, Path.Combine(target, Path.GetFileName(subdirectory)));
    }

    /// <summary>POSIX single-quote quoting — correct for any content.</summary>
    private static string ShQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    /// <summary>cmd double-quote quoting, embedded quotes escaped the way .NET parses them back.</summary>
    private static string CmdQuote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string OrkeonVersion()
    {
        var version = typeof(ForgePromoter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata > 0 ? version[..metadata] : version;
    }
}
