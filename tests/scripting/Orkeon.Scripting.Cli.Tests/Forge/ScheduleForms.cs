using System.Security;
using System.Xml.Linq;
using Orkeon.Tests.Shared.Launchers;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The forms a registration runs a team's launcher in (STUDIO-51, decision 6), as the schedule
/// suites expect them — written out here, never borrowed from the adapters they check: the
/// Windows task read the way the operating system runs it, <c>ExecStart</c>'s and the cron line's
/// quoting, and what an earlier version wrote.
/// </summary>
internal static class ScheduleForms
{
    /// <summary>
    /// What a task's action runs: <c>cmd.exe</c> itself, and the launcher <c>cmd /d /v:off /s /c</c>
    /// finds in its arguments (<see cref="CmdBatchModel.SlashCProgram"/>); null for any other action.
    /// </summary>
    public static string? TaskRuns(string xml)
    {
        var exec = XDocument.Parse(xml).Descendants().Single(e => e.Name.LocalName == "Exec");
        return exec.Elements().SingleOrDefault(e => e.Name.LocalName == "Command")?.Value == @"%SystemRoot%\System32\cmd.exe"
            ? CmdBatchModel.SlashCProgram(exec.Elements().Single(e => e.Name.LocalName == "Arguments").Value)
            : null;
    }

    /// <summary><c>ExecStart</c>'s quoting: C escapes for a backslash and a quote, <c>%%</c> for a specifier, <c>$$</c> for a variable.</summary>
    public static string SystemdQuoted(string path) =>
        "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal).Replace("$", "$$", StringComparison.Ordinal) + "\"";

    /// <summary>A cron command's quoting: single quotes for the shell, every <c>%</c> escaped for cron.</summary>
    public static string CronQuoted(string path) =>
        "'" + path.Replace("'", "'\\''", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal) + "'";

    /// <summary>The task definition an earlier version wrote: <c>run.cmd</c> as the command, no argument.</summary>
    public static string FormerTask(string windowsLauncher) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
        + "  <Triggers><CalendarTrigger><StartBoundary>2026-09-25T07:30:00</StartBoundary><Enabled>true</Enabled>"
        + "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger></Triggers>\n"
        + $"  <Actions Context=\"Author\">\n    <Exec><Command>{SecurityElement.Escape(windowsLauncher)}</Command></Exec>\n  </Actions>\n"
        + "</Task>\n";

    /// <summary>The job line of a <c>cron.txt</c>: its first line neither blank nor a comment.</summary>
    public static string CronJob(string cronFile) =>
        cronFile.Split('\n').First(line => line.Length > 0 && !line.StartsWith('#'));
}
