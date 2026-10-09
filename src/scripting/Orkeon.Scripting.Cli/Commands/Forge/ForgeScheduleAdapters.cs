using System.Xml;
using System.Xml.Linq;
using Orkeon.Constants.Configuration;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>What the operating system holds under a registration's names.</summary>
internal sealed record ForgeScheduleProbe
{
    /// <summary>Nothing is registered under those names.</summary>
    public static ForgeScheduleProbe Absent { get; } = new();

    /// <summary>The OS could not be asked — its scheduler is missing, or refused the question.</summary>
    public static ForgeScheduleProbe Unanswered(string refusal) => new() { Refusal = refusal };

    /// <summary>Whether a registration exists under the names.</summary>
    public bool Exists { get; init; }

    /// <summary>Whether the OS will fire it: false for a task or a timer disabled by hand, a cron line commented out.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The launcher the registration runs, as the OS reports it; null when it could not be read.</summary>
    public string? Launcher { get; init; }

    /// <summary>
    /// Whether the registration runs <see cref="Launcher"/> the way an earlier version registered it
    /// — a Windows task naming <c>run.cmd</c> as its command (STUDIO-51) —: to reinstall.
    /// </summary>
    public bool Outdated { get; init; }

    /// <summary>Why the OS could not be asked; null when it answered.</summary>
    public string? Refusal { get; init; }
}

/// <summary>What an install or a removal did to the operating system.</summary>
/// <param name="Changed">Whether a registration was written or removed.</param>
/// <param name="Refusal">What the OS answered when it refused; null when it did not.</param>
internal sealed record ForgeScheduleOsOutcome(bool Changed, string? Refusal)
{
    /// <summary>A registration was written, or removed.</summary>
    public static ForgeScheduleOsOutcome Done { get; } = new(true, null);

    /// <summary>Nothing to do: there was nothing to remove.</summary>
    public static ForgeScheduleOsOutcome Unchanged { get; } = new(false, null);

    /// <summary>The OS refused, and said this.</summary>
    public static ForgeScheduleOsOutcome Refused(string refusal) => new(false, refusal);

    /// <summary>Whether the OS refused.</summary>
    public bool IsRefused => Refusal is not null;
}

/// <summary>What an install registers.</summary>
/// <param name="TeamDirectory">The team folder.</param>
/// <param name="ScheduleDirectory">Its <c>schedule/</c>, generated for where and what the folder is now.</param>
/// <param name="Launcher">The launcher the registration runs.</param>
/// <param name="Names">The names the registration takes on this OS (<see cref="IForgeScheduleAdapter.NamesFor"/>).</param>
internal sealed record ForgeScheduleTarget(string TeamDirectory, string ScheduleDirectory, string Launcher, IReadOnlyList<string> Names);

/// <summary>
/// One family of operating system's scheduler behind the schedule verbs (STUDIO-27, D-02): the
/// Windows Task Scheduler, the systemd user manager, or the user's crontab. Every command goes
/// through <see cref="IForgeOsCommands"/> as a binary and a list of arguments, never a shell;
/// every name it acts on is one it is handed — computed by <see cref="NamesFor"/> at install,
/// read back from <c>forge.json</c> for a check or a removal, never guessed (D-03).
/// </summary>
internal interface IForgeScheduleAdapter
{
    /// <summary>The family this adapter speaks for.</summary>
    ForgePromotePlatform Family { get; }

    /// <summary>The names a team called <paramref name="artifactName"/> registers under (<see cref="ForgePromoter.ArtifactName"/>).</summary>
    IReadOnlyList<string> NamesFor(string artifactName);

    /// <summary>The launcher of <paramref name="teamDirectory"/> this family's registration runs.</summary>
    string LauncherOf(string teamDirectory);

    /// <summary>Registers <paramref name="target"/>, replacing a registration of the same names.</summary>
    ForgeScheduleOsOutcome Install(ForgeScheduleTarget target);

    /// <summary>Asks the OS what it holds under <paramref name="names"/>.</summary>
    ForgeScheduleProbe Probe(IReadOnlyList<string> names);

    /// <summary>Removes the registration under <paramref name="names"/>; nothing registered is <see cref="ForgeScheduleOsOutcome.Unchanged"/>.</summary>
    ForgeScheduleOsOutcome Remove(IReadOnlyList<string> names);

    /// <summary>The command a person runs to remove it by hand, when the OS refused Orkeon.</summary>
    string ManualRemoveCommand(IReadOnlyList<string> names);
}

/// <summary>The adapters, and the install command each family displays.</summary>
internal static class ForgeScheduleAdapters
{
    /// <summary>The adapter of <paramref name="platform"/>, over <paramref name="commands"/>.</summary>
    /// <param name="platform">The family.</param>
    /// <param name="commands">The OS seam.</param>
    /// <param name="unitDirectory">Where the systemd user units go (Linux only).</param>
    public static IForgeScheduleAdapter For(ForgePromotePlatform platform, IForgeOsCommands commands, string unitDirectory) =>
        platform switch
        {
            ForgePromotePlatform.Windows => new WindowsTaskScheduleAdapter(commands),
            ForgePromotePlatform.Linux => new SystemdUserScheduleAdapter(commands, unitDirectory),
            _ => new CronScheduleAdapter(commands),
        };

    /// <summary>
    /// The command a person runs to install <paramref name="teamDirectory"/>'s schedule by hand on
    /// <paramref name="platform"/> — the fallback every refusal carries, and the one the
    /// <c>promoted</c> event displays: for this machine, never written into a file — <c>FORGE.md</c>
    /// names nothing of the machine (STUDIO-52). Displayed, never executed: what Orkeon itself runs
    /// is the adapter's list of arguments.
    /// </summary>
    public static string ManualInstallCommand(ForgePromotePlatform platform, string teamDirectory, string artifactName)
    {
        var schedule = Path.Combine(teamDirectory, ForgePromoter.ScheduleDirectoryName);
        return platform switch
        {
            ForgePromotePlatform.Windows =>
                $"schtasks /Create /TN \"{WindowsTaskScheduleAdapter.TaskName(artifactName)}\" /XML \"{Path.Combine(schedule, WindowsTaskScheduleAdapter.TaskFileName)}\" /F",
            ForgePromotePlatform.Linux =>
                $"cp \"{Path.Combine(schedule, SystemdUserScheduleAdapter.ServiceName(artifactName))}\" \"{Path.Combine(schedule, SystemdUserScheduleAdapter.TimerName(artifactName))}\" ~/.config/systemd/user/ "
                + $"&& systemctl --user daemon-reload && systemctl --user enable --now {SystemdUserScheduleAdapter.TimerName(artifactName)}",
            _ =>
                $"( crontab -l 2>/dev/null; cat \"{Path.Combine(schedule, CronScheduleAdapter.LineFileName)}\" ) | crontab -",
        };
    }
}

/// <summary>
/// Windows: a task of the current user's Task Scheduler library, created from
/// <c>schedule/windows-task.xml</c> — <c>schtasks /Create /TN "Orkeon &lt;team&gt;" /XML … /F</c>,
/// read back with <c>/Query … /XML</c>, removed with <c>/Delete … /F</c>. The XML names no
/// principal, so the task runs as the user who registered it, only while that user is logged
/// on: no password is asked, nothing is elevated. Its action runs <c>cmd.exe</c> on the launcher
/// (<see cref="TaskCommand"/>, <see cref="TaskArguments"/>).
/// </summary>
internal sealed class WindowsTaskScheduleAdapter(IForgeOsCommands commands) : IForgeScheduleAdapter
{
    /// <summary>The Task Scheduler's command-line client.</summary>
    public const string Program = "schtasks";

    /// <summary>The generated task definition under <c>schedule/</c>.</summary>
    public const string TaskFileName = "windows-task.xml";

    /// <summary>The task a team called <paramref name="artifactName"/> registers.</summary>
    public static string TaskName(string artifactName) => $"Orkeon {artifactName}";

    /// <summary>
    /// The program a task runs: <c>cmd.exe</c> itself, under the variable the Task Scheduler
    /// expands (STUDIO-51, decision 6). A task naming <c>run.cmd</c> as its command had the
    /// Scheduler compose <c>cmd /c</c>'s line, whose quotes <c>cmd</c> keeps or strips by what the
    /// path holds — a <c>&amp;</c>, a <c>(</c> or a <c>^</c> could keep the team from starting.
    /// </summary>
    public const string TaskCommand = @"%SystemRoot%\System32\cmd.exe";

    /// <summary>
    /// The arguments of a task running <paramref name="launcher"/>: <c>/d</c> no AutoRun command of
    /// the registry, <c>/v:off</c> a <c>!</c> of the path kept, <c>/s</c> the first and the last
    /// quote of the command removed and they alone — the path keeps the two others, whatever it
    /// holds. The launcher's path stays the only thing the task names: written again, it needs no
    /// reinstall.
    /// </summary>
    public static string TaskArguments(string launcher) => $"/d /v:off /s /c \"\"{launcher}\"\"";

    /// <inheritdoc />
    public ForgePromotePlatform Family => ForgePromotePlatform.Windows;

    /// <inheritdoc />
    public IReadOnlyList<string> NamesFor(string artifactName) => [TaskName(artifactName)];

    /// <inheritdoc />
    public string LauncherOf(string teamDirectory) => Path.Combine(teamDirectory, ForgePromoter.WindowsLauncherName);

    /// <inheritdoc />
    public ForgeScheduleOsOutcome Install(ForgeScheduleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // /F replaces a task of the same name: installing again is reinstalling.
        var created = commands.Run(
            Program, ["/Create", "/TN", target.Names[0], "/XML", Path.Combine(target.ScheduleDirectory, TaskFileName), "/F"]);
        return created.Succeeded ? ForgeScheduleOsOutcome.Done : ForgeScheduleOsOutcome.Refused(created.Diagnostic);
    }

    /// <inheritdoc />
    public ForgeScheduleProbe Probe(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var query = commands.Run(Program, ["/Query", "/TN", names[0], "/XML"]);
        if (!query.Started)
            return ForgeScheduleProbe.Unanswered(query.Diagnostic);

        // schtasks says « not found » in the user's language and exits 1 whatever the reason, so
        // any refusal to show the task reads as its absence. A removal that finds nothing then
        // has nothing to remove — and an install that follows says what the OS really refuses.
        return query.ExitCode == 0 ? ReadTask(query.StandardOutput) : ForgeScheduleProbe.Absent;
    }

    /// <inheritdoc />
    public ForgeScheduleOsOutcome Remove(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var probe = Probe(names);
        if (probe.Refusal is { } refusal)
            return ForgeScheduleOsOutcome.Refused(refusal);
        if (!probe.Exists)
            return ForgeScheduleOsOutcome.Unchanged;

        var deleted = commands.Run(Program, ["/Delete", "/TN", names[0], "/F"]);
        return deleted.Succeeded ? ForgeScheduleOsOutcome.Done : ForgeScheduleOsOutcome.Refused(deleted.Diagnostic);
    }

    /// <inheritdoc />
    public string ManualRemoveCommand(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return $"schtasks /Delete /TN \"{names[0]}\" /F";
    }

    /// <summary>
    /// The batch file <c>cmd /s /c</c> runs from <paramref name="arguments"/>: what follows
    /// <c>/c</c>, its first and last quote removed — <c>/s</c> — then its own quotes; null when the
    /// arguments run nothing.
    /// </summary>
    private static string? LauncherIn(string? arguments)
    {
        if (arguments is null)
            return null;

        var slashC = arguments.IndexOf("/c ", StringComparison.OrdinalIgnoreCase);
        if (slashC < 0)
            return null;

        var line = arguments[(slashC + 3)..].Trim();
        if (line.Length >= 2 && line[0] == '"' && line[^1] == '"')
            line = line[1..^1];

        return line.Trim().Trim('"');
    }

    /// <summary>
    /// The task's definition as <c>/Query /XML</c> printed it: the launcher it runs, and whether it is
    /// enabled. The launcher is read in the arguments of <c>cmd.exe</c> — the command compared once
    /// its variables are expanded, without regard to case —; a task whose command is the launcher
    /// itself, as an earlier version registered it, gives that launcher and reads
    /// <see cref="ForgeScheduleProbe.Outdated"/>. The declaration says UTF-16 whatever the bytes were;
    /// parsed from a string, it is not consulted.
    /// </summary>
    internal static ForgeScheduleProbe ReadTask(string xml)
    {
        try
        {
            var task = XDocument.Parse(xml);
            var exec = task.Descendants().FirstOrDefault(e => e.Name.LocalName == "Exec");
            var command = exec?.Elements().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value.Trim();
            var arguments = exec?.Elements().FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value.Trim();
            var enabled = task.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Settings")?
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Enabled")?.Value.Trim();

            var runsCmd = command is not null && string.Equals(
                Environment.ExpandEnvironmentVariables(command),
                Environment.ExpandEnvironmentVariables(TaskCommand),
                StringComparison.OrdinalIgnoreCase);
            var launcher = runsCmd ? LauncherIn(arguments) : command?.Trim('"');
            return new ForgeScheduleProbe
            {
                Exists = true,
                Launcher = launcher is { Length: > 0 } ? launcher : null,
                Outdated = !runsCmd
                    || launcher is not { Length: > 0 }
                    || !string.Equals(arguments, TaskArguments(launcher), StringComparison.OrdinalIgnoreCase),
                Enabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase),
            };
        }
        catch (XmlException)
        {
            // The task exists — the query succeeded — and its definition is not one this reads.
            return new ForgeScheduleProbe { Exists = true };
        }
    }
}

/// <summary>
/// Linux: a timer of the systemd user manager. The two units of <c>schedule/</c> are copied into
/// the user's unit directory, then <c>systemctl --user daemon-reload</c> and
/// <c>enable --now</c> the timer; the removal is <c>disable --now</c>, the units deleted, and
/// <c>daemon-reload</c> again. Everything is the user's own: no <c>sudo</c>, no system unit.
/// </summary>
internal sealed class SystemdUserScheduleAdapter(IForgeOsCommands commands, string unitDirectory) : IForgeScheduleAdapter
{
    /// <summary>The systemd manager's command-line client.</summary>
    public const string Program = "systemctl";

    /// <summary>The timer a team called <paramref name="artifactName"/> enables.</summary>
    public static string TimerName(string artifactName) => $"orkeon-{artifactName}.timer";

    /// <summary>The service that timer starts.</summary>
    public static string ServiceName(string artifactName) => $"orkeon-{artifactName}.service";

    /// <summary>
    /// The user's unit directory, where systemd looks for user units: <c>$XDG_CONFIG_HOME/systemd/user</c>,
    /// <c>~/.config/systemd/user</c> when that variable is unset.
    /// </summary>
    public static string DefaultUnitDirectory()
    {
        var configHome = Environment.GetEnvironmentVariable(EnvironmentVariableNames.XdgConfigHome);
        var root = configHome is { Length: > 0 } && Path.IsPathRooted(configHome)
            ? configHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "systemd", "user");
    }

    /// <summary>Where this adapter writes the units.</summary>
    public string UnitDirectory => unitDirectory;

    /// <inheritdoc />
    public ForgePromotePlatform Family => ForgePromotePlatform.Linux;

    /// <inheritdoc />
    public IReadOnlyList<string> NamesFor(string artifactName) => [TimerName(artifactName), ServiceName(artifactName)];

    /// <inheritdoc />
    public string LauncherOf(string teamDirectory) => Path.Combine(teamDirectory, ForgePromoter.PosixLauncherName);

    /// <inheritdoc />
    public ForgeScheduleOsOutcome Install(ForgeScheduleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // The units a refused install must not leave behind: only the ones it created — a
        // reinstall overwrites units that were already the user's registration.
        var created = new List<string>();
        try
        {
            Directory.CreateDirectory(UnitDirectory);
            foreach (var unit in target.Names)
            {
                var installed = Path.Combine(UnitDirectory, unit);
                if (!File.Exists(installed))
                    created.Add(installed);
                File.Copy(Path.Combine(target.ScheduleDirectory, unit), installed, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Undo(created, ex.Message);
        }

        var reload = commands.Run(Program, ["--user", "daemon-reload"]);
        if (!reload.Succeeded)
            return Undo(created, reload.Diagnostic);

        var enabled = commands.Run(Program, ["--user", "enable", "--now", target.Names[0]]);
        return enabled.Succeeded ? ForgeScheduleOsOutcome.Done : Undo(created, enabled.Diagnostic);
    }

    /// <inheritdoc />
    public ForgeScheduleProbe Probe(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var timer = Path.Combine(UnitDirectory, names[0]);
        var service = Path.Combine(UnitDirectory, names[^1]);
        if (!File.Exists(timer) && !File.Exists(service))
            return ForgeScheduleProbe.Absent;

        // A service left without its timer is registered and fires never: to reinstall, and
        // removable — systemd has no unit state to give for a timer that is not there.
        if (!File.Exists(timer))
            return new ForgeScheduleProbe { Exists = true, Enabled = false, Launcher = ExecStartOf(service) };

        var state = commands.Run(Program, ["--user", "is-enabled", names[0]]);
        if (!state.Started || (!state.Succeeded && state.StandardOutput.Trim().Length == 0))
            return ForgeScheduleProbe.Unanswered(state.Diagnostic);

        return new ForgeScheduleProbe
        {
            Exists = true,
            Enabled = state.Succeeded,
            Launcher = ExecStartOf(service),
        };
    }

    /// <inheritdoc />
    public ForgeScheduleOsOutcome Remove(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var units = names.Select(name => Path.Combine(UnitDirectory, name)).ToList();
        if (!units.Any(File.Exists))
            return ForgeScheduleOsOutcome.Unchanged;

        // Only a timer that is there can be disabled: a service left alone is merely deleted.
        if (File.Exists(units[0]))
        {
            var disabled = commands.Run(Program, ["--user", "disable", "--now", names[0]]);
            if (!disabled.Succeeded)
                return ForgeScheduleOsOutcome.Refused(disabled.Diagnostic);
        }

        try
        {
            foreach (var unit in units)
                File.Delete(unit);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ForgeScheduleOsOutcome.Refused(ex.Message);
        }

        // Best effort: the timer is already stopped and disabled, and its files are gone — a
        // manager that cannot reload merely keeps an inert unit in memory until it next does.
        commands.Run(Program, ["--user", "daemon-reload"]);
        return ForgeScheduleOsOutcome.Done;
    }

    /// <inheritdoc />
    public string ManualRemoveCommand(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return $"systemctl --user disable --now {names[0]} && rm "
            + string.Join(' ', names.Select(name => $"\"{Path.Combine(UnitDirectory, name)}\""))
            + " && systemctl --user daemon-reload";
    }

    /// <summary>The unit files a refused install created are deleted again; the refusal is reported whatever that costs.</summary>
    private ForgeScheduleOsOutcome Undo(List<string> created, string refusal)
    {
        foreach (var unit in created)
        {
            try
            {
                File.Delete(unit);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: the refusal being reported is the OS's, not this cleanup's.
            }
        }

        // A reload refused a moment ago may pass now; either way the refusal above stands.
        if (created.Count > 0)
            commands.Run(Program, ["--user", "daemon-reload"]);

        return ForgeScheduleOsOutcome.Refused(refusal);
    }

    /// <summary>The <c>ExecStart</c> of the installed service: the launcher it runs, unquoted.</summary>
    private static string? ExecStartOf(string service)
    {
        try
        {
            return File.Exists(service) ? ExecStartLauncher(File.ReadAllText(service)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>ExecStart</c>'s value running <paramref name="launcher"/> (STUDIO-51, decision 6): one
    /// quoted word in which systemd reads a backslash and a quote as C escapes, a <c>%</c> as a
    /// specifier and a <c>$</c> as a variable — all four escaped, so a path holding them runs.
    /// </summary>
    public static string ExecStartValue(string launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        return "\"" + launcher
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal)
            .Replace("$", "$$", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// The launcher the <c>ExecStart</c> of <paramref name="unit"/> runs, its quoting undone
    /// (<see cref="ExecStartValue"/>); null when it names none. An earlier version wrote the path
    /// between quotes, unescaped: it reads the same for every path that line could run.
    /// </summary>
    internal static string? ExecStartLauncher(string unit)
    {
        const string key = "ExecStart=";
        var value = unit.ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(key, StringComparison.Ordinal))
            .Select(line => line[key.Length..].Trim())
            .FirstOrDefault(value => value.Length > 0);
        if (value is null || value[0] != '"')
            return value;

        var launcher = new System.Text.StringBuilder();
        for (var i = 1; i < value.Length && value[i] != '"'; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is '\\' or '"')
                i++;
            else if (value[i] is '%' or '$' && i + 1 < value.Length && value[i + 1] == value[i])
                i++;

            launcher.Append(value[i]);
        }

        return launcher.Length > 0 ? launcher.ToString() : null;
    }
}

/// <summary>
/// Everything else, macOS included: one line of the user's crontab, marked by a trailing comment
/// <c># orkeon:&lt;team&gt;</c> — read with <c>crontab -l</c>, written back whole with
/// <c>crontab -</c> on stdin, never through a shell. The marker is how the line is found again:
/// every other line of the crontab is the user's, and is written back exactly as it was read.
/// launchd is out of scope.
/// </summary>
internal sealed class CronScheduleAdapter(IForgeOsCommands commands) : IForgeScheduleAdapter
{
    /// <summary>The crontab's command-line client.</summary>
    public const string Program = "crontab";

    /// <summary>The generated cron line under <c>schedule/</c>.</summary>
    public const string LineFileName = "cron.txt";

    /// <summary>The tag of a team called <paramref name="artifactName"/>: the name of its cron line.</summary>
    public static string Tag(string artifactName) => $"orkeon:{artifactName}";

    /// <summary>The comment that marks the tagged line, at its end.</summary>
    public static string Marker(string tag) => $"# {tag}";

    /// <summary>
    /// The command of a cron line running <paramref name="launcher"/> (STUDIO-51, decision 6):
    /// between single quotes — the <c>/bin/sh</c> cron hands it to expands nothing there, a quote
    /// closed, escaped and reopened —, and every <c>%</c> escaped: it is cron's line separator.
    /// </summary>
    public static string Command(string launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        return ("'" + launcher.Replace("'", "'\\''", StringComparison.Ordinal) + "'").Replace("%", "\\%", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public ForgePromotePlatform Family => ForgePromotePlatform.Other;

    /// <inheritdoc />
    public IReadOnlyList<string> NamesFor(string artifactName) => [Tag(artifactName)];

    /// <inheritdoc />
    public string LauncherOf(string teamDirectory) => Path.Combine(teamDirectory, ForgePromoter.PosixLauncherName);

    /// <inheritdoc />
    public ForgeScheduleOsOutcome Install(ForgeScheduleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var tag = target.Names[0];
        if (JobLine(Path.Combine(target.ScheduleDirectory, LineFileName), tag) is not { } job)
            return ForgeScheduleOsOutcome.Refused($"{ForgePromoter.ScheduleDirectoryName}/{LineFileName} holds no cron line.");

        var crontab = Read();
        if (crontab.Refusal is { } refusal)
            return ForgeScheduleOsOutcome.Refused(refusal);

        // Its own previous line goes, whatever it said: installing again is reinstalling.
        return Write([.. crontab.Lines.Where(line => !IsTagged(line, tag)), job]);
    }

    /// <inheritdoc />
    public ForgeScheduleProbe Probe(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var crontab = Read();
        if (crontab.Refusal is { } refusal)
            return ForgeScheduleProbe.Unanswered(refusal);

        var line = crontab.Lines.FirstOrDefault(entry => IsTagged(entry, names[0]));
        if (line is null)
            return ForgeScheduleProbe.Absent;

        return new ForgeScheduleProbe
        {
            Exists = true,
            // A line commented out by hand keeps its marker, and fires no more.
            Enabled = !line.TrimStart().StartsWith('#'),
            Launcher = QuotedPath(line),
        };
    }

    /// <inheritdoc />
    public ForgeScheduleOsOutcome Remove(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var crontab = Read();
        if (crontab.Refusal is { } refusal)
            return ForgeScheduleOsOutcome.Refused(refusal);

        var kept = crontab.Lines.Where(line => !IsTagged(line, names[0])).ToList();
        return kept.Count == crontab.Lines.Count ? ForgeScheduleOsOutcome.Unchanged : Write(kept);
    }

    /// <inheritdoc />
    public string ManualRemoveCommand(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return $"crontab -l | grep -v '{Marker(names[0])}$' | crontab -";
    }

    /// <summary>Whether <paramref name="line"/> ends with <paramref name="tag"/>'s marker — the whole tag, not a longer one.</summary>
    public static bool IsTagged(string line, string tag)
    {
        ArgumentNullException.ThrowIfNull(line);

        var trimmed = line.TrimEnd();
        var marker = Marker(tag);
        return trimmed.EndsWith(marker, StringComparison.Ordinal)
            && (trimmed.Length == marker.Length || char.IsWhiteSpace(trimmed[^(marker.Length + 1)]));
    }

    /// <summary>
    /// The job line of <c>cron.txt</c> — its first line that is neither blank nor a comment — carrying
    /// <paramref name="tag"/>'s marker, added when a hand edit dropped it. Null when there is none.
    /// </summary>
    private static string? JobLine(string file, string tag)
    {
        if (!File.Exists(file))
            return null;

        var job = File.ReadLines(file)
            .Select(line => line.TrimEnd())
            .FirstOrDefault(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'));
        if (job is null)
            return null;

        return IsTagged(job, tag) ? job : $"{job} {Marker(tag)}";
    }

    /// <summary>
    /// The launcher a cron line runs: its first quoted word, its quoting undone
    /// (<see cref="Command"/>) — or, on a line an earlier version wrote, its first double-quoted
    /// token. A <c>\%</c> is a <c>%</c> either way.
    /// </summary>
    internal static string? QuotedPath(string line)
    {
        var open = line.IndexOfAny(['\'', '"']);
        if (open < 0)
            return null;

        if (line[open] == '"')
        {
            var close = line.IndexOf('"', open + 1);
            return close > open + 1 ? line[(open + 1)..close].Replace("\\%", "%", StringComparison.Ordinal) : null;
        }

        // Single-quoted stretches joined by \' — a quote closed, escaped and reopened.
        var launcher = new System.Text.StringBuilder();
        while (true)
        {
            var close = line.IndexOf('\'', open + 1);
            if (close < 0)
                return null;

            launcher.Append(line, open + 1, close - open - 1);
            if (string.CompareOrdinal(line, close + 1, "\\''", 0, 3) != 0)
                break;

            launcher.Append('\'');
            open = close + 3;
        }

        return launcher.Length > 0 ? launcher.ToString().Replace("\\%", "%", StringComparison.Ordinal) : null;
    }

    /// <summary>
    /// The user's crontab, line by line. « No crontab » is an empty one; any other failure is a
    /// refusal — and nothing is ever written over a crontab this could not read, or the user's
    /// own lines would be replaced by Orkeon's.
    /// </summary>
    private (IReadOnlyList<string> Lines, string? Refusal) Read()
    {
        var listed = commands.Run(Program, ["-l"]);
        if (!listed.Started)
            return ([], listed.Diagnostic);

        if (listed.ExitCode != 0)
        {
            return listed.StandardError.Contains("no crontab", StringComparison.OrdinalIgnoreCase)
                ? ([], null)
                : ([], listed.Diagnostic);
        }

        var lines = listed.StandardOutput.ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return (lines, null);
    }

    /// <summary>Writes the whole crontab back through <c>crontab -</c>.</summary>
    private ForgeScheduleOsOutcome Write(List<string> lines)
    {
        var content = lines.Count == 0 ? "" : string.Join('\n', lines) + "\n";
        var written = commands.Run(Program, ["-"], content);
        return written.Succeeded ? ForgeScheduleOsOutcome.Done : ForgeScheduleOsOutcome.Refused(written.Diagnostic);
    }
}
