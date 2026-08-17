using Jint;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Commands.Scripting.Bindings;
using Orkeon.Cli.Commands.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Tests.Bindings;

/// <summary>
/// Verifies that <c>console.log/info/warn/error/debug</c> calls inside JS land on the
/// CLR logger surface — the DX promise of spec §5 "no surprise for TS devs".
/// </summary>
public sealed class ConsoleShimTests
{
    private sealed record LogRecord(LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger
    {
        public List<LogRecord> Records { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Null.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState s, Exception? ex, Func<TState, Exception?, string> formatter)
            => Records.Add(new LogRecord(level, formatter(s, ex)));
        private sealed class Null : IDisposable
        {
            public static readonly Null Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public void ApplyForLoad_routes_console_log_to_supplied_logger()
    {
        using var engine = new Engine();
        var logger = new RecordingLogger();
        ConsoleShimBinding.ApplyForLoad(engine, logger);

        engine.Evaluate("console.log('hello from load')");

        Assert.Contains(logger.Records, r =>
            r.Level == LogLevel.Information && r.Message.Contains("hello from load"));
    }

    [Fact]
    public void ApplyForInvocation_routes_console_info_warn_error_debug_to_ctx_log()
    {
        using var engine = new Engine();
        var logger = new RecordingLogger();
        ConsoleShimBinding.ApplyForInvocation(engine, new JsLogShim(logger));

        engine.Evaluate("""
            console.debug('d');
            console.info('i');
            console.warn('w');
            console.error('e');
        """);

        Assert.Contains(logger.Records, r => r.Level == LogLevel.Debug       && r.Message.Contains('d'));
        Assert.Contains(logger.Records, r => r.Level == LogLevel.Information && r.Message.Contains('i'));
        Assert.Contains(logger.Records, r => r.Level == LogLevel.Warning     && r.Message.Contains('w'));
        Assert.Contains(logger.Records, r => r.Level == LogLevel.Error       && r.Message.Contains('e'));
    }

    [Fact]
    public void Console_log_accepts_object_data_argument()
    {
        using var engine = new Engine();
        var logger = new RecordingLogger();
        ConsoleShimBinding.ApplyForInvocation(engine, new JsLogShim(logger));

        engine.Evaluate("console.info('event', { kind: 'click', count: 3 })");

        var info = logger.Records.Single(r => r.Level == LogLevel.Information);
        Assert.Contains("event", info.Message);
        Assert.Contains("Click", info.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", info.Message);
    }
}
