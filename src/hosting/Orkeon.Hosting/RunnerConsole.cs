using Orkeon.Compliance.Vfs;

namespace Orkeon.Hosting;

/// <summary>
/// The terminal a runner talks to: the streams a run writes its result and its diagnostics to,
/// the reader an interactive loop asks its questions on, and the interrupt key. Owned by Hosting
/// so a runner never reaches <c>System.Console</c> itself: the executable hands its own console
/// (<see cref="SystemRunnerConsole"/>), a test hands its buffers.
/// </summary>
public interface IRunnerConsole
{
    /// <summary>The standard output: the run's product.</summary>
    TextWriter Output { get; }

    /// <summary>The standard error: diagnostics, refusals and operator warnings.</summary>
    TextWriter ErrorOutput { get; }

    /// <summary>The standard input: the questions of an interactive loop.</summary>
    TextReader Input { get; }

    /// <summary>
    /// Calls <paramref name="handler"/> on each interrupt key press (Ctrl+C) until the returned
    /// registration is disposed. The handler returns <see langword="true"/> when it consumed the
    /// key press, which then does not end the process.
    /// </summary>
    /// <param name="handler">What an interrupt does; true when the process must keep running.</param>
    /// <returns>The registration; disposing it stops the calls.</returns>
    IDisposable OnInterrupt(Func<bool> handler);
}

/// <summary>
/// <see cref="IRunnerConsole"/> over the process console. Each stream is read from the console
/// at every access, so a redirection the process installs later (a TUI's pane) is honoured.
/// </summary>
[SuppressConsoleCompliance("CONSOLE-ADAPTER: the Hosting implementation of IRunnerConsole over the process console; every runner reaches the terminal through it.")]
public sealed class SystemRunnerConsole : IRunnerConsole
{
    private SystemRunnerConsole()
    {
    }

    /// <summary>The process console.</summary>
    public static SystemRunnerConsole Instance { get; } = new();

    /// <inheritdoc />
    public TextWriter Output => Console.Out;

    /// <inheritdoc />
    public TextWriter ErrorOutput => Console.Error;

    /// <inheritdoc />
    public TextReader Input => Console.In;

    /// <inheritdoc />
    public IDisposable OnInterrupt(Func<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            if (handler())
                e.Cancel = true;
        };
        Console.CancelKeyPress += onCancel;
        return new Registration(onCancel);
    }

    private sealed class Registration(ConsoleCancelEventHandler onCancel) : IDisposable
    {
        private ConsoleCancelEventHandler? _onCancel = onCancel;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _onCancel, null) is { } registered)
                Console.CancelKeyPress -= registered;
        }
    }
}
