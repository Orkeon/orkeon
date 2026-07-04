using Microsoft.Extensions.Logging;

namespace Orkeon.Cli.Abstractions.Logging;

/// <summary>
/// Process-wide ambient <see cref="ILoggerProvider"/> registry. Lets the outer-process
/// composition root (typically a TUI host that owns a custom logger pane) advertise its
/// provider to inner <c>IHost</c>s that would otherwise default to
/// <c>AddSimpleConsole</c> and pollute stdout.
/// </summary>
/// <remarks>
/// <para>
/// The outer composition root sets <see cref="Current"/> right after building its
/// logger. Inner hosts (e.g. a one-shot crew runner spawned from inside a REPL command)
/// check it during <c>ConfigureLogging</c> and substitute it for their console provider.
/// This avoids a hard project reference between examples runners and the TUI project.
/// </para>
/// <para>
/// The set provider is wrapped in a non-owning lease so child hosts that dispose their
/// LoggerFactory do NOT dispose the outer host's provider. The outer host owns the
/// real lifetime; <see cref="Reset"/> clears the ambient at TUI shutdown.
/// </para>
/// </remarks>
public static class AmbientLoggerProvider
{
    private static ILoggerProvider? _current;

    /// <summary>
    /// Currently advertised ambient provider, or <c>null</c> when none is active.
    /// Always returns a non-owning lease — disposing it has no effect on the underlying provider.
    /// </summary>
    public static ILoggerProvider? Current => _current is null ? null : new LeasedLoggerProvider(_current);

    /// <summary>Sets the ambient provider. Call once when the outer logger is ready.</summary>
    public static void Set(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _current = provider;
    }

    /// <summary>Clears the ambient provider. Call at outer host shutdown.</summary>
    public static void Reset() => _current = null;

    private sealed class LeasedLoggerProvider : ILoggerProvider
    {
        private readonly ILoggerProvider _inner;
        public LeasedLoggerProvider(ILoggerProvider inner) => _inner = inner;
        public ILogger CreateLogger(string categoryName) => _inner.CreateLogger(categoryName);
        public void Dispose() { /* no-op — outer host owns the lifetime */ }
    }
}
