using System.Globalization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// Renders Core's structured results as the lines a terminal shows. Nothing is dropped:
/// an unknown doctor status or a message Studio has no icon for still prints.
/// </summary>
internal static class MessageFormatter
{
    /// <summary>Renders one validation finding as <c>[severity] CODE path — text</c>.</summary>
    public static string Format(ValidationMessage message) => ValidationMessageFormatter.Format(message);

    /// <summary>Renders a list of findings, most severe first.</summary>
    public static IReadOnlyList<string> Format(IReadOnlyList<ValidationMessage> messages) =>
        ValidationMessageFormatter.FormatAll(messages);

    /// <summary>A one-line summary of a validation pass, for the status bar.</summary>
    public static string Summarize(IReadOnlyList<ValidationMessage> messages) =>
        ValidationMessageFormatter.Summarize(messages);

    /// <summary>Renders a diagnostic run: the checks, then how the child process ended.</summary>
    public static IReadOnlyList<string> Format(DoctorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var lines = new List<string>();

        foreach (var check in report.Checks)
        {
            var status = check.Status switch
            {
                DoctorStatus.Ok => "ok  ",
                DoctorStatus.Warning => "warn",
                DoctorStatus.Failure => "FAIL",
                _ => check.RawStatus.Length > 0 ? check.RawStatus : "?",
            };

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"[{status}] {check.Check} — {check.Detail}"));
        }

        if (report.ParseError is { Length: > 0 } parseError)
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"[----] {parseError}"));

        if (lines.Count == 0 && report.RawOutput is { Length: > 0 } raw)
            lines.AddRange(raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')));

        lines.Add("");
        lines.Add(report.Run.Description);

        return lines;
    }
}
