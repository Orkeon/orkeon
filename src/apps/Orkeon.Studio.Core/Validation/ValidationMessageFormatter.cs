using System.Globalization;

namespace Orkeon.Studio.Core.Validation;

/// <summary>
/// Renders a <see cref="ValidationMessage"/> as the one line a list shows. The three Studio
/// front-ends share it so a finding cannot read as an error in one and as advice in another:
/// the severity is always spelled out, never left to a colour or an icon the terminal may
/// not render.
/// </summary>
public static class ValidationMessageFormatter
{
    /// <summary>
    /// The severity label, padded to a fixed width so a column of findings stays aligned.
    /// </summary>
    public static string SeverityLabel(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error => "ERROR",
        ValidationSeverity.Warning => "WARN ",
        _ => "INFO ",
    };

    /// <summary>Renders one finding as <c>[SEVERITY] CODE path — text</c>.</summary>
    public static string Format(ValidationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var path = message.Path is { Length: > 0 } value
            ? string.Create(CultureInfo.InvariantCulture, $" {value}")
            : "";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{SeverityLabel(message.Severity)}] {message.Code}{path} — {message.Text}");
    }

    /// <summary>Renders a list of findings, most severe first.</summary>
    public static IReadOnlyList<string> FormatAll(IReadOnlyList<ValidationMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages
            .OrderByDescending(message => message.Severity)
            .Select(Format)
            .ToList();
    }

    /// <summary>A one-line summary of a validation pass, for a status bar.</summary>
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
}
