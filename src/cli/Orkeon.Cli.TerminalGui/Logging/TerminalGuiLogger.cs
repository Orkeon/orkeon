using Microsoft.Extensions.Logging;

namespace Orkeon.Cli.TerminalGui.Logging;

internal sealed class TerminalGuiLogger : ILogger
{
    private readonly string _category;
    private readonly TerminalGuiLoggerProvider _provider;

    public TerminalGuiLogger(string category, TerminalGuiLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        ArgumentNullException.ThrowIfNull(formatter);
        var message = formatter(state, exception);
        _provider.Append(new LogEntry(DateTimeOffset.Now, logLevel, _category, message, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
            // No-op: the null scope holds no resources; logging scopes are not used here.
        }
    }
}
