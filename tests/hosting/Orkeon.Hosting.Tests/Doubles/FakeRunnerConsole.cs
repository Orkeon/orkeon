namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IRunnerConsole"/> over in-memory buffers: what a runner writes is read
/// back from <see cref="Output"/> and <see cref="ErrorOutput"/>, what it reads comes from
/// <see cref="Input"/>, and <see cref="Interrupt"/> plays a Ctrl+C.
/// </summary>
public sealed class FakeRunnerConsole : IRunnerConsole
{
    private readonly List<Func<bool>> _handlers = [];
    private readonly Lock _lock = new();

    /// <summary>The standard output buffer.</summary>
    public StringWriter Output { get; } = new();

    /// <summary>The standard error buffer.</summary>
    public StringWriter ErrorOutput { get; } = new();

    /// <summary>The standard input; empty unless a test sets it.</summary>
    public TextReader Input { get; set; } = new StringReader("");

    /// <summary>Whether the input reads as redirected; a buffer is, by default.</summary>
    public bool IsInputRedirected { get; set; } = true;

    TextWriter IRunnerConsole.Output => Output;

    TextWriter IRunnerConsole.ErrorOutput => ErrorOutput;

    TextReader IRunnerConsole.Input => Input;

    /// <inheritdoc />
    public IDisposable OnInterrupt(Func<bool> handler)
    {
        lock (_lock) { _handlers.Add(handler); }
        return new Registration(this, handler);
    }

    /// <summary>Calls every registered interrupt handler; true when one consumed the key press.</summary>
    public bool Interrupt()
    {
        Func<bool>[] handlers;
        lock (_lock) { handlers = [.. _handlers]; }
        var consumed = false;
        foreach (var handler in handlers)
            consumed |= handler();
        return consumed;
    }

    private sealed class Registration(FakeRunnerConsole owner, Func<bool> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._lock) { owner._handlers.Remove(handler); }
        }
    }
}
