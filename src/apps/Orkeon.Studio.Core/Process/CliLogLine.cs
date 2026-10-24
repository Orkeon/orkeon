using System.Text.RegularExpressions;

namespace Orkeon.Studio.Core.Process;

/// <summary>The severity a CLI console log line carries, in the order of <c>Microsoft.Extensions.Logging</c>.</summary>
public enum CliLogLevel
{
    /// <summary><c>trce</c>.</summary>
    Trace,

    /// <summary><c>dbug</c>.</summary>
    Debug,

    /// <summary><c>info</c>.</summary>
    Information,

    /// <summary><c>warn</c>.</summary>
    Warning,

    /// <summary><c>fail</c>.</summary>
    Error,

    /// <summary><c>crit</c>.</summary>
    Critical,
}

/// <summary>
/// One line of the CLI's console logger, read for the Run screen's journal. Under <c>--events</c>
/// every log goes to stderr (stdout carries the protocol), so the stream says nothing about severity:
/// the level token of the simple console format (<c>info:</c>, <c>warn:</c>, <c>fail:</c>…) does. The
/// token is the logger's fixed format, never localized. The logger's own timestamp is dropped: the
/// journal already shows when Studio read the line.
/// </summary>
/// <param name="Level">The level the line carries; null when it is not a log line.</param>
/// <param name="Text">The line without the logger's timestamp; the line unchanged when it is not a log line.</param>
public sealed partial record CliLogLine(CliLogLevel? Level, string Text)
{
    /// <summary>Reads one line: an optional <c>HH:mm:ss.fff </c> timestamp, then a level token and <c>: </c>.</summary>
    public static CliLogLine Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = LogLine().Match(text);
        if (!match.Success)
            return new CliLogLine(null, text);

        var level = match.Groups["level"].Value switch
        {
            "trce" => CliLogLevel.Trace,
            "dbug" => CliLogLevel.Debug,
            "info" => CliLogLevel.Information,
            "warn" => CliLogLevel.Warning,
            "fail" => CliLogLevel.Error,
            _ => CliLogLevel.Critical,
        };
        return new CliLogLine(level, text[match.Groups["level"].Index..]);
    }

    [GeneratedRegex(@"^(?:\d{2}:\d{2}:\d{2}(?:\.\d{1,7})? )?(?<level>trce|dbug|info|warn|fail|crit): ", RegexOptions.CultureInvariant)]
    private static partial Regex LogLine();
}
