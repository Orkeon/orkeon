using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Logging;

public sealed class TerminalGuiLoggerProvider : ILoggerProvider
{
    private readonly LogsPaneView _logs;
    private readonly ConcurrentDictionary<string, TerminalGuiLogger> _loggers = new();
    private LogLevel _minimumLevel;

    public TerminalGuiLoggerProvider(LogsPaneView logs, TerminalGuiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _minimumLevel = options.DefaultMinimumLogLevel;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, c => new TerminalGuiLogger(c, this));

    public void SetMinimumLevel(LogLevel level) => _minimumLevel = level;

    public LogLevel CurrentMinimumLevel => _minimumLevel;

    internal bool IsEnabled(LogLevel level) => level >= _minimumLevel;

    internal void Append(LogEntry entry)
    {
        if (!IsEnabled(entry.Level)) return;
        _logs.Append(Format(entry));
    }

    internal static string Format(LogEntry e)
    {
        var lvl = e.Level switch
        {
            LogLevel.Trace       => "TRC",
            LogLevel.Debug       => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning     => "WRN",
            LogLevel.Error       => "ERR",
            LogLevel.Critical    => "CRT",
            _                    => "???"
        };
        var cat = ShortenCategory(e.Category);
        var ex = e.Exception is null ? "" : $"\n  → {e.Exception.GetType().Name}: {e.Exception.Message}";
        return $"{e.Timestamp:HH:mm:ss} [{lvl}] {cat}: {e.Message}{ex}";
    }

    internal static string ShortenCategory(string fullName)
    {
        var idx = fullName.LastIndexOf('.');
        return idx >= 0 ? fullName[(idx + 1)..] : fullName;
    }

    public void Dispose() => _loggers.Clear();
}
