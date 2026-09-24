using System.Globalization;
using System.Text.Json.Serialization;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The <c>schedule</c> block of <c>forge.json</c> (STUDIO-27, D-03): the schedule the folder
/// declares — its promotion's <c>--schedule</c> — and, once <c>forge schedule</c> installed it,
/// what was installed. Absent from a folder that declares none and has nothing installed.
/// </summary>
internal sealed record ForgeTeamSchedule
{
    /// <summary>The declared schedule, as the grammar spells it (<c>daily@HH:mm</c> or <c>hourly</c>); null when the folder declares none.</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; init; }

    /// <summary>What <c>forge schedule</c> registered with the operating system; null while nothing is.</summary>
    [JsonPropertyName("installed")]
    public ForgeScheduleInstallation? Installed { get; init; }

    /// <summary>The block for <paramref name="expression"/> and <paramref name="installed"/>; null when both are.</summary>
    public static ForgeTeamSchedule? Of(string? expression, ForgeScheduleInstallation? installed) =>
        expression is null && installed is null ? null : new ForgeTeamSchedule { Expression = expression, Installed = installed };
}

/// <summary>
/// What <c>forge schedule</c> registered, recorded so that a check and a removal act on these
/// names and never on names they would compute (D-03): a folder renamed since still removes the
/// registration it made under its former name. Every field is optional on read — a record
/// hand-edited into an incomplete one stays readable, and names nothing to act on.
/// </summary>
internal sealed record ForgeScheduleInstallation
{
    /// <summary>The schedule installed.</summary>
    [JsonPropertyName("expression")]
    public string? Expression { get; init; }

    /// <summary>The OS family it was installed on: <c>windows</c>, <c>linux</c> or <c>other</c>.</summary>
    [JsonPropertyName("family")]
    public string? Family { get; init; }

    /// <summary>The names it holds there: the task, the two units, or the cron tag.</summary>
    [JsonPropertyName("names")]
    public IReadOnlyList<string>? Names { get; init; }

    /// <summary>The team folder it was installed for — the one whose launcher it runs.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>When it was installed (UTC, ISO-8601).</summary>
    [JsonPropertyName("installedAt")]
    public string? InstalledAt { get; init; }
}

/// <summary>Where a folder's schedule stands (<c>schedule.state</c>).</summary>
internal enum ForgeScheduleState
{
    /// <summary>Registered, running this folder's launcher under this folder's name, on the declared schedule.</summary>
    Installed,

    /// <summary>Nothing of this folder's is registered here.</summary>
    Absent,

    /// <summary>Registered, but no longer what the folder is: to reinstall.</summary>
    Stale,
}

/// <summary>A folder's schedule after a verb: the <c>schedule.state</c> event's payload.</summary>
internal sealed record ForgeScheduleReport
{
    /// <summary>The team folder.</summary>
    public required string TeamDirectory { get; init; }

    /// <summary>Where its schedule stands.</summary>
    public required ForgeScheduleState State { get; init; }

    /// <summary>Why it is <see cref="ForgeScheduleState.Stale"/> or <see cref="ForgeScheduleState.Absent"/>; one of <see cref="ForgeScheduleReasons"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>The schedule the folder declares; null when it declares none.</summary>
    public string? Expression { get; init; }

    /// <summary>This machine's OS family.</summary>
    public required string Family { get; init; }

    /// <summary>The names of the registration concerned — installed, stale, or just removed; empty when none is.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>A removal: whether a registration was actually removed from the OS.</summary>
    public bool? Removed { get; init; }
}

/// <summary>The wire spellings of <see cref="ForgeScheduleReport.Reason"/>.</summary>
internal static class ForgeScheduleReasons
{
    /// <summary>Absent: this folder never installed its schedule, or removed it.</summary>
    public const string NotInstalled = "not-installed";

    /// <summary>Absent: the recorded registration was removed outside Orkeon.</summary>
    public const string Gone = "gone";

    /// <summary>Absent: the recorded registration is the original's, this folder being a copy of it.</summary>
    public const string Copy = "copy";

    /// <summary>Absent: the recorded registration was made on another OS family.</summary>
    public const string OtherSystem = "other-system";

    /// <summary>Stale: it runs another folder's launcher — this one was moved since.</summary>
    public const string Moved = "moved";

    /// <summary>Stale: it carries another name than the folder gives it now — renamed since.</summary>
    public const string Renamed = "renamed";

    /// <summary>Stale: it fires on another schedule than the one the folder declares.</summary>
    public const string Changed = "changed";

    /// <summary>Stale: it fires while the folder declares no schedule any more.</summary>
    public const string Undeclared = "undeclared";

    /// <summary>Stale: the OS holds it disabled.</summary>
    public const string Disabled = "disabled";
}

/// <summary>A verb the OS or the folder refused: the code, the sentence, and the command a person can run instead.</summary>
internal sealed record ForgeScheduleFailure(string Code, string Message, string? Command = null);

/// <summary>What a schedule verb produced: the folder's state, or the refusal — and what it could not do while succeeding.</summary>
internal sealed record ForgeScheduleOutcome
{
    /// <summary>The state the verb leaves the folder in; null when it failed.</summary>
    public ForgeScheduleReport? Report { get; init; }

    /// <summary>The refusal; null when the verb succeeded.</summary>
    public ForgeScheduleFailure? Failure { get; init; }

    /// <summary>Non-fatal messages, each a <c>warning</c> on the stream.</summary>
    public IReadOnlyList<(string Code, string Message)> Warnings { get; init; } = [];
}

/// <summary>The operating system the schedule verbs act on, and the clock that stamps an install.</summary>
/// <param name="Adapter">The scheduler of this machine's OS family.</param>
/// <param name="Clock">The current instant.</param>
internal sealed record ForgeScheduleHost(IForgeScheduleAdapter Adapter, Func<DateTimeOffset> Clock)
{
    /// <summary>This machine: its OS family, its real scheduler.</summary>
    public static ForgeScheduleHost ForCurrentMachine() => new(
        ForgeScheduleAdapters.For(
            ForgePromoter.DetectPlatform(), SystemForgeOsCommands.Instance, SystemdUserScheduleAdapter.DefaultUnitDirectory()),
        () => DateTimeOffset.UtcNow);
}

/// <summary>
/// The managed schedule (STUDIO-27, DA-3): the operating system runs the team — Orkeon has no
/// scheduler of its own — and the CLI, which owns the artifacts, installs, checks and removes
/// the registration on the user's behalf. Studio asks first; the verbs themselves are idempotent.
/// <list type="bullet">
/// <item><description><b>Install</b> registers what <c>schedule/</c> describes, regenerated first when it describes another folder (a copy, a moved or renamed team); a registration of this folder's under a former name goes first.</description></item>
/// <item><description><b>Check</b> compares what the OS holds under the recorded names with what the folder is now.</description></item>
/// <item><description><b>Remove</b> unregisters, deletes <c>schedule/</c> and the <c>forge.json</c> block; nothing to remove is a success.</description></item>
/// </list>
/// Which registration a folder may act on is recorded, never computed: the <c>installed</c> block
/// of its own <c>forge.json</c>. A copy of a scheduled team inherits its original's block; the
/// block names the folder it was made for, and while that folder still claims it, the copy leaves
/// it alone.
/// </summary>
internal sealed class ForgeScheduler(ForgeScheduleHost host)
{
    /// <summary>Who owns the registration a folder's record names.</summary>
    private enum Ownership
    {
        /// <summary>The record names none.</summary>
        None,

        /// <summary>This folder's — made for it, or for where it stood before it was moved or renamed.</summary>
        Own,

        /// <summary>Another folder's, which still claims it: this one is a copy of it.</summary>
        Foreign,

        /// <summary>Made on another OS family: nothing here can act on it.</summary>
        OtherSystem,
    }

    private IForgeScheduleAdapter Adapter => host.Adapter;

    private string Family => SpellFamily(Adapter.Family);

    /// <summary>The wire spelling of an OS family: <c>windows</c>, <c>linux</c> or <c>other</c>.</summary>
    public static string SpellFamily(ForgePromotePlatform platform) => platform switch
    {
        ForgePromotePlatform.Windows => "windows",
        ForgePromotePlatform.Linux => "linux",
        _ => "other",
    };

    /// <summary><c>forge schedule &lt;team&gt;</c>: installs the declared schedule, or reinstalls it.</summary>
    public ForgeScheduleOutcome Install(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var record = ForgeTeamRecord.TryRead(teamDirectory);
        if (record?.Schedule?.Expression is not { } expression)
        {
            return Failed(ForgeErrorCodes.ScheduleNone,
                $"'{teamDirectory}' declares no schedule — adopt it with `forge promote --schedule daily@HH:mm|hourly` first.");
        }

        if (!ForgeSchedule.TryParse(expression, out var schedule, out var grammarError))
            return Failed(ForgeErrorCodes.ScheduleNone, $"{ForgeTeamRecord.FileName} declares {grammarError}");

        var launcher = Adapter.LauncherOf(teamDirectory);
        if (!File.Exists(launcher))
        {
            return Failed(ForgeErrorCodes.TeamUnreadable,
                $"'{teamDirectory}' holds no {Path.GetFileName(launcher)} for the schedule to run — adopt it again to regenerate its launchers.");
        }

        // What schedule/ describes has to be this folder, where and as it is now: a copy, a moved or a
        // renamed team carries artifacts naming another launcher and another name.
        var artifactName = ForgePromoter.ArtifactName(teamDirectory);
        ForgePromoter.EnsureScheduleArtifacts(teamDirectory, artifactName, schedule!, host.Clock());

        var names = Adapter.NamesFor(artifactName);
        var target = new ForgeScheduleTarget(
            teamDirectory, Path.Combine(teamDirectory, ForgePromoter.ScheduleDirectoryName), launcher, names);
        var manual = ForgeScheduleAdapters.ManualInstallCommand(Adapter.Family, teamDirectory, artifactName);
        var installed = record.Schedule.Installed;
        var ownership = OwnershipOf(installed, teamDirectory);

        var probe = Adapter.Probe(names);
        if (probe.Refusal is { } unanswered)
            return Failed(ForgeErrorCodes.ScheduleRefused, Refused(unanswered), manual);

        // Another folder's registration under the very name this one takes: never replaced.
        if (probe.Exists
            && !SameLauncher(probe.Launcher, launcher)
            && !(ownership == Ownership.Own && SameNames(installed!.Names, names))
            && ClaimantOf(probe.Launcher, names) is { } claimant)
        {
            return Failed(ForgeErrorCodes.ScheduleNameTaken,
                $"'{names[0]}' is already scheduled for '{claimant}'. Rename this folder, or unschedule that team first.");
        }

        // This folder's registration under a former name — it was renamed — goes first.
        if (ownership == Ownership.Own && !SameNames(installed!.Names, names))
        {
            var former = Adapter.Remove(installed.Names!);
            if (former.Refusal is { } formerRefusal)
                return Failed(ForgeErrorCodes.ScheduleRefused, Refused(formerRefusal), Adapter.ManualRemoveCommand(installed.Names!));
        }

        var outcome = Adapter.Install(target);
        if (outcome.Refusal is { } refusal)
            return Failed(ForgeErrorCodes.ScheduleRefused, Refused(refusal), manual);

        ForgeTeamRecord.SaveSchedule(teamDirectory, ForgeTeamSchedule.Of(expression, new ForgeScheduleInstallation
        {
            Expression = expression,
            Family = Family,
            Names = names,
            Path = teamDirectory,
            InstalledAt = host.Clock().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        }));

        return new ForgeScheduleOutcome
        {
            Report = Report(teamDirectory, ForgeScheduleState.Installed, expression) with { Names = names },
        };
    }

    /// <summary><c>forge schedule &lt;team&gt; --check</c>: where the folder's schedule stands, nothing changed.</summary>
    public ForgeScheduleOutcome Check(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var record = ForgeTeamRecord.TryRead(teamDirectory);
        var expression = record?.Schedule?.Expression;
        var installed = record?.Schedule?.Installed;

        switch (OwnershipOf(installed, teamDirectory))
        {
            case Ownership.None:
                return Checked(Report(teamDirectory, ForgeScheduleState.Absent, expression) with { Reason = ForgeScheduleReasons.NotInstalled });
            case Ownership.Foreign:
                return Checked(Report(teamDirectory, ForgeScheduleState.Absent, expression) with { Reason = ForgeScheduleReasons.Copy });
            case Ownership.OtherSystem:
                return Checked(Report(teamDirectory, ForgeScheduleState.Absent, expression) with { Reason = ForgeScheduleReasons.OtherSystem });
        }

        var names = installed!.Names!;
        var probe = Adapter.Probe(names);
        if (probe.Refusal is { } refusal)
        {
            return Failed(ForgeErrorCodes.ScheduleRefused, Refused(refusal),
                ForgeScheduleAdapters.ManualInstallCommand(Adapter.Family, teamDirectory, ForgePromoter.ArtifactName(teamDirectory)));
        }

        if (!probe.Exists)
            return Checked(Report(teamDirectory, ForgeScheduleState.Absent, expression) with { Reason = ForgeScheduleReasons.Gone });

        var stale = StaleReason(teamDirectory, installed, probe, expression);
        return Checked(Report(teamDirectory, stale is null ? ForgeScheduleState.Installed : ForgeScheduleState.Stale, expression) with
        {
            Reason = stale,
            Names = names,
        });
    }

    /// <summary>
    /// <c>forge unschedule &lt;team&gt;</c>: unregisters what this folder installed, deletes
    /// <c>schedule/</c> and the <c>forge.json</c> block. Nothing to remove is a success; a
    /// refusal leaves everything in place, so a retry still knows the names.
    /// </summary>
    public ForgeScheduleOutcome Remove(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var record = ForgeTeamRecord.TryRead(teamDirectory);
        var installed = record?.Schedule?.Installed;
        var warnings = new List<(string Code, string Message)>();
        var removed = false;
        IReadOnlyList<string> names = [];

        switch (OwnershipOf(installed, teamDirectory))
        {
            case Ownership.Own:
                names = installed!.Names!;
                var outcome = Adapter.Remove(names);
                if (outcome.Refusal is { } refusal)
                    return Failed(ForgeErrorCodes.ScheduleRefused, Refused(refusal), Adapter.ManualRemoveCommand(names));
                removed = outcome.Changed;
                break;

            case Ownership.Foreign:
                warnings.Add((ForgeErrorCodes.ScheduleNotOwned,
                    $"'{string.Join("', '", installed!.Names!)}' is the schedule of '{installed.Path}', which this folder is a copy of: it is left in place."));
                break;

            case Ownership.OtherSystem:
                warnings.Add((ForgeErrorCodes.ScheduleOtherSystem,
                    $"This folder's schedule was installed on {installed!.Family}, not here: remove '{string.Join("', '", installed.Names!)}' there."));
                break;
        }

        try
        {
            var artifacts = Path.Combine(teamDirectory, ForgePromoter.ScheduleDirectoryName);
            if (Directory.Exists(artifacts))
                Directory.Delete(artifacts, recursive: true);
            if (record?.Schedule is not null)
                ForgeTeamRecord.SaveSchedule(teamDirectory, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The registration — what actually runs the team — is gone; files the disk kept only
            // describe it. Said, and the verb still succeeds.
            warnings.Add((ForgeErrorCodes.ScheduleFilesKept,
                $"The schedule is removed, but its files could not all be deleted ({ex.Message})."));
        }

        return new ForgeScheduleOutcome
        {
            Report = Report(teamDirectory, ForgeScheduleState.Absent, expression: null) with
            {
                Reason = ForgeScheduleReasons.NotInstalled,
                Names = names,
                Removed = removed,
            },
            Warnings = warnings,
        };
    }

    /// <summary>
    /// The warning a promotion that drops the schedule owes when this folder's registration is
    /// still installed (the promotion itself touches no OS): the registration keeps running the
    /// team until <c>forge unschedule</c> removes it. Null when there is nothing to say.
    /// </summary>
    public static string? StillInstalled(string teamDirectory, ForgePromotePlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var installed = ForgeTeamRecord.TryRead(teamDirectory)?.Schedule?.Installed;
        return OwnershipOf(installed, teamDirectory, SpellFamily(platform)) == Ownership.Own
            ? $"The team is no longer scheduled, but '{string.Join("', '", installed!.Names!)}' still runs it: remove it with `orkeon forge unschedule \"{teamDirectory}\"`."
            : null;
    }

    private Ownership OwnershipOf(ForgeScheduleInstallation? installed, string teamDirectory) =>
        OwnershipOf(installed, teamDirectory, Family);

    /// <summary>
    /// Who owns the registration <paramref name="installed"/> names. Made for this folder, it is
    /// this folder's; made for another folder that still exists and still claims it in its own
    /// record, it is that folder's — this one is a copy; made for a folder that is gone or no
    /// longer claims it, it is this one's, moved or renamed since.
    /// </summary>
    private static Ownership OwnershipOf(ForgeScheduleInstallation? installed, string teamDirectory, string family)
    {
        if (installed?.Names is not { Count: > 0 })
            return Ownership.None;

        if (!string.Equals(installed.Family, family, StringComparison.Ordinal))
            return Ownership.OtherSystem;

        if (SamePath(installed.Path, teamDirectory))
            return Ownership.Own;

        return installed.Path is { Length: > 0 } origin
            && ClaimsItsOwn(origin, installed.Names, family)
                ? Ownership.Foreign
                : Ownership.Own;
    }

    /// <summary>Whether <paramref name="folder"/>'s own record names <paramref name="names"/>, installed for itself.</summary>
    private static bool ClaimsItsOwn(string folder, IReadOnlyList<string> names, string family)
    {
        if (!Directory.Exists(folder))
            return false;

        var theirs = ForgeTeamRecord.TryRead(folder)?.Schedule?.Installed;
        return theirs is not null
            && string.Equals(theirs.Family, family, StringComparison.Ordinal)
            && SamePath(theirs.Path, folder)
            && SameNames(theirs.Names, names);
    }

    /// <summary>The folder whose launcher <paramref name="launcher"/> is, when that folder claims <paramref name="names"/>.</summary>
    private string? ClaimantOf(string? launcher, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(launcher))
            return null;

        string? folder;
        try
        {
            folder = Path.GetDirectoryName(Path.GetFullPath(launcher));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return folder is { Length: > 0 } && ClaimsItsOwn(folder, names, Family) ? folder : null;
    }

    /// <summary>Why a registration that exists is no longer what the folder is; null when it still is.</summary>
    private string? StaleReason(string teamDirectory, ForgeScheduleInstallation installed, ForgeScheduleProbe probe, string? expression)
    {
        if (!SamePath(installed.Path, teamDirectory)
            || (probe.Launcher is not null && !SameLauncher(probe.Launcher, Adapter.LauncherOf(teamDirectory))))
        {
            return ForgeScheduleReasons.Moved;
        }

        if (!SameNames(installed.Names, Adapter.NamesFor(ForgePromoter.ArtifactName(teamDirectory))))
            return ForgeScheduleReasons.Renamed;
        if (expression is null)
            return ForgeScheduleReasons.Undeclared;
        if (!string.Equals(installed.Expression, expression, StringComparison.OrdinalIgnoreCase))
            return ForgeScheduleReasons.Changed;
        return probe.Enabled ? null : ForgeScheduleReasons.Disabled;
    }

    private ForgeScheduleReport Report(string teamDirectory, ForgeScheduleState state, string? expression) => new()
    {
        TeamDirectory = teamDirectory,
        State = state,
        Expression = expression,
        Family = Family,
    };

    private static ForgeScheduleOutcome Checked(ForgeScheduleReport report) => new() { Report = report };

    private static ForgeScheduleOutcome Failed(string code, string message, string? command = null) =>
        new() { Failure = new ForgeScheduleFailure(code, message, command) };

    private string Refused(string answer) =>
        $"The {SpellFamily(Adapter.Family)} scheduler refused: {answer.Trim()}";

    /// <summary>Whether two recorded name lists are the same names, in the same order.</summary>
    private static bool SameNames(IReadOnlyList<string>? left, IReadOnlyList<string> right) =>
        left is not null && left.SequenceEqual(right, StringComparer.Ordinal);

    /// <summary>Whether two paths name the same folder: full paths, separators trimmed, compared the way the platform does.</summary>
    internal static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                PhysicalPathContainment.Comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the launcher the OS reports is <paramref name="expected"/>. Compared as paths, and
    /// failing that on their ASCII letters alone: <c>schtasks</c> prints in the console's code page,
    /// so an accented user folder can come back decoded differently — never with other ASCII
    /// letters in other places.
    /// </summary>
    internal static bool SameLauncher(string? reported, string expected)
    {
        if (reported is null)
            return false;
        if (SamePath(reported, expected))
            return true;

        return string.Equals(AsciiSkeleton(reported), AsciiSkeleton(expected), PhysicalPathContainment.Comparison);
    }

    private static string AsciiSkeleton(string path) =>
        new(path.Where(c => c is > '\0' and < (char)0x80).ToArray());
}
