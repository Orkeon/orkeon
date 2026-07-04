using Microsoft.Extensions.Logging;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsLoggerTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public HashSet<LogLevel> Disabled { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => !Disabled.Contains(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public void debug_logs_when_enabled()
    {
        var logger = new CapturingLogger();
        var jsLog = new JsLogger(logger);

        jsLog.debug("d");

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message == "d");
    }

    [Fact]
    public void debug_skips_when_level_disabled()
    {
        var logger = new CapturingLogger();
        logger.Disabled.Add(LogLevel.Debug);
        var jsLog = new JsLogger(logger);

        jsLog.debug("d");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void info_logs_when_enabled()
    {
        var logger = new CapturingLogger();
        var jsLog = new JsLogger(logger);

        jsLog.info("i");

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message == "i");
    }

    [Fact]
    public void info_skips_when_level_disabled()
    {
        var logger = new CapturingLogger();
        logger.Disabled.Add(LogLevel.Information);
        var jsLog = new JsLogger(logger);

        jsLog.info("i");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void warn_always_logs()
    {
        var logger = new CapturingLogger();
        var jsLog = new JsLogger(logger);

        jsLog.warn("w");

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message == "w");
    }

    [Fact]
    public void error_always_logs()
    {
        var logger = new CapturingLogger();
        var jsLog = new JsLogger(logger);

        jsLog.error("e");

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message == "e");
    }

    [Fact]
    public void null_logger_falls_back_to_NullLogger_without_throwing()
    {
        var jsLog = new JsLogger(null!);

        var ex = Record.Exception(() =>
        {
            jsLog.debug("d");
            jsLog.info("i");
            jsLog.warn("w");
            jsLog.error("e");
        });

        Assert.Null(ex);
    }
}
