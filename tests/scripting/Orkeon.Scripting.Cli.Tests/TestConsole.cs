using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The <see cref="IRunnerConsole"/> a test hands a verb: what the verb writes is read back from
/// <see cref="Stdout"/> and <see cref="Stderr"/>, what it reads comes from the given input, and
/// <see cref="Interrupt"/> plays a Ctrl+C. Nothing process-global is touched.
/// </summary>
internal sealed class TestConsole : IRunnerConsole, IDisposable
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly StringReader _in;
    private readonly InterruptHandlers _interrupts = new();

    /// <summary>
    /// <paramref name="stdin"/>, when given, is the verb's standard input - how a test scripts
    /// the inbound half of the <c>--events</c> protocol; without it the input is empty.
    /// </summary>
    public TestConsole(string? stdin = null) => _in = new StringReader(stdin ?? string.Empty);

    public string Stdout => _out.ToString();
    public string Stderr => _error.ToString();

    public TextWriter Output => _out;
    public TextWriter ErrorOutput => _error;
    public TextReader Input => _in;

    /// <summary>Whether the input reads as redirected; a buffer is, by default.</summary>
    public bool IsInputRedirected { get; set; } = true;

    /// <summary>How many interrupt handlers are registered right now.</summary>
    public int InterruptHandlerCount => _interrupts.Count;

    public IDisposable OnInterrupt(Func<bool> handler) => _interrupts.Add(handler);

    /// <summary>Calls every registered interrupt handler; true when one consumed the key press.</summary>
    public bool Interrupt() => _interrupts.Raise();

    public void Dispose()
    {
        _out.Dispose();
        _error.Dispose();
        _in.Dispose();
    }
}

/// <summary>The interrupt handlers a test console holds, raised on demand to play a Ctrl+C.</summary>
internal sealed class InterruptHandlers
{
    private readonly List<Func<bool>> _handlers = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
                return _handlers.Count;
        }
    }

    public IDisposable Add(Func<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
            _handlers.Add(handler);
        return new Registration(this, handler);
    }

    public bool Raise()
    {
        Func<bool>[] handlers;
        lock (_lock)
            handlers = [.. _handlers];
        var consumed = false;
        foreach (var handler in handlers)
            consumed |= handler();
        return consumed;
    }

    private sealed class Registration(InterruptHandlers owner, Func<bool> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._lock)
                owner._handlers.Remove(handler);
        }
    }
}
