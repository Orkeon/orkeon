using Microsoft.Extensions.Logging;

namespace Orkeon.Cli.TerminalGui.Hosting;

public sealed record TerminalGuiOptions
{
    /// <summary>Initial vertical split ratio (logs pane height / total). Default 0.5.</summary>
    public double InitialSplitRatio { get; init; } = 0.5;

    /// <summary>Minimum log level surfaced in the logs pane (UI filter, runtime-tweakable). Default Information.</summary>
    public LogLevel DefaultMinimumLogLevel { get; init; } = LogLevel.Information;

    /// <summary>Max number of log entries kept in the in-memory ring buffer. Default 5000.</summary>
    public int LogsBufferCapacity { get; init; } = 5000;

    /// <summary>Title shown in the logs pane frame.</summary>
    public string LogsPaneTitle { get; init; } = "Logs";

    /// <summary>Title shown in the REPL pane frame.</summary>
    public string ReplPaneTitle { get; init; } = "REPL";

    /// <summary>Title shown in the tasks (running command) bandeau pane frame.</summary>
    public string TasksPaneTitle { get; init; } = "Running command";

    /// <summary>
    /// When true (default), the REPL history wraps long lines automatically and the horizontal
    /// scrollbar is hidden. The wrap is display-only: copy/paste returns the original text with no
    /// injected carriage return. When false, the horizontal scrollbar reappears (lines are not wrapped).
    /// </summary>
    public bool ReplWordWrap { get; init; } = true;
}
