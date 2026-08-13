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
    public static string Format(ValidationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var severity = message.Severity switch
        {
            ValidationSeverity.Error => "ERROR",
            ValidationSeverity.Warning => "WARN ",
            _ => "INFO ",
        };

        var path = message.Path is { Length: > 0 } value
            ? string.Create(CultureInfo.InvariantCulture, $" {value}")
            : "";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{severity}] {message.Code}{path} — {message.Text}");
    }

    /// <summary>Renders a list of findings, most severe first.</summary>
    public static IReadOnlyList<string> Format(IReadOnlyList<ValidationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages
            .OrderByDescending(message => message.Severity)
            .Select(Format)
            .ToList();
    }

    /// <summary>A one-line summary of a validation pass, for the status bar.</summary>
    public static string Summarize(IReadOnlyList<ValidationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
            return "Validation: no findings.";

        var errors = messages.Count(message => message.Severity == ValidationSeverity.Error);
        var warnings = messages.Count(message => message.Severity == ValidationSeverity.Warning);
        var infos = messages.Count - errors - warnings;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Validation: {errors} error(s), {warnings} warning(s), {infos} note(s).");
    }

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
