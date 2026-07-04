using Microsoft.Extensions.Logging;

namespace Orkeon.Cli.TerminalGui.Logging;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception);
