using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Orkeon.Hosting;

/// <summary>
/// The operator channel: the warnings a run goes on after — an LLM not configured, an MCP server
/// that did not connect, an OAuth account without a token store — are logged in the category
/// <see cref="Category"/>, and that category reaches the operator's standard error whatever the
/// host's log level: <c>-v</c>, a <c>Logging:LogLevel:Default</c> of <c>Error</c> or a logger
/// cleared by a TUI do not silence it (§ 5.6 of the console inventory, option 1).
/// <para>
/// Its guarantee is a provider of its own, <see cref="OperatorLoggerProvider"/>: it takes that
/// category alone, at <see cref="LogLevel.Warning"/> and above, and writes each message as one
/// line on <see cref="IRunnerConsole.ErrorOutput"/>, synchronously — the line the runners wrote on
/// stderr before, byte for byte and in the same order. Every other provider is kept off the
/// category: the console logger writes to the standard output, which is the run's.
/// </para>
/// </summary>
internal static partial class RunnerOperatorLog
{
    /// <summary>The operator category.</summary>
    internal const string Category = "Orkeon.Operator";

    /// <summary>
    /// Adds the operator channel to <paramref name="services"/>: the provider, its forced filter,
    /// and <see cref="SystemRunnerConsole"/> as the <see cref="IRunnerConsole"/> when the caller
    /// registered none. Called after any <c>ClearProviders()</c> of the caller's logging.
    /// </summary>
    internal static void AddOperatorChannel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IRunnerConsole>(SystemRunnerConsole.Instance);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, OperatorLoggerProvider>());
        services.AddLogging(b =>
        {
            // Provider-specific rules beat every rule without a provider, a configuration's
            // Default included: the operator provider takes its category at Warning, nothing else.
            b.AddFilter<OperatorLoggerProvider>(category: null, LogLevel.None);
            b.AddFilter<OperatorLoggerProvider>(Category, LogLevel.Warning);
            // The other providers do not repeat the line: the runner's own category logs it.
            b.AddFilter(Category, LogLevel.None);
            b.AddFilter<ConsoleLoggerProvider>(Category, LogLevel.None);
        });
    }

    /// <summary>The operator logger of <paramref name="factory"/>.</summary>
    internal static ILogger Create(ILoggerFactory factory) => factory.CreateLogger(Category);

    /// <summary>Says <paramref name="line"/> to the operator.</summary>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{Line}")]
    internal static partial void Warn(ILogger logger, string line);
}

/// <summary>
/// Writes each entry of the operator category as one line on <see cref="IRunnerConsole.ErrorOutput"/>,
/// read at every write so a later redirection of the console is honoured.
/// </summary>
/// <param name="console">The runner's console.</param>
internal sealed class OperatorLoggerProvider(IRunnerConsole console) : ILoggerProvider
{
    private static readonly Lock s_gate = new();

    public ILogger CreateLogger(string categoryName) => new OperatorLogger(console);

    public void Dispose()
    {
    }

    private sealed class OperatorLogger(IRunnerConsole console) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
                return;

            var line = formatter(state, exception);
            lock (s_gate)
                console.ErrorOutput.WriteLine(line);
        }
    }
}
