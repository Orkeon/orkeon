namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Pure formatting helpers for the running-command bandeau. Lives outside
/// <see cref="TasksPaneView"/> so unit tests can exercise the formatting without
/// loading any Terminal.Gui type (and tripping the TUI-12 module-init bug).
/// </summary>
internal static class TasksPaneFormatter
{
    /// <summary>Formats elapsed time as <c>m:ss</c> under one hour, <c>h:mm:ss</c> beyond.</summary>
    public static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
}
