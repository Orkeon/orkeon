namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// One transcript screen per thread: main plus one per task ticket, created at the thread's first
/// line. A hidden screen keeps appending, so showing it later needs no replay.
/// </summary>
internal sealed class ThreadScreens
{
    private readonly int _capacity;
    private readonly Action<TranscriptView> _configure;
    private bool? _wordWrap;
    private readonly Dictionary<string, TranscriptView> _tasks = new(StringComparer.Ordinal);

    public ThreadScreens(int capacity, Action<TranscriptView> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _capacity = capacity;
        _configure = configure;
        Main = Create();
        _wordWrap = Main.WordWrap;
        Displayed = Main;
    }

    public TranscriptView Main { get; }

    public TranscriptView Displayed { get; private set; }

    /// <summary>The ticket of the displayed screen; null for main.</summary>
    public string? DisplayedThread { get; private set; }

    /// <summary>The pane-level word-wrap state, shared by every screen.</summary>
    public bool WordWrap => _wordWrap ?? Main.WordWrap;

    public IEnumerable<string> Tickets => _tasks.Keys;

    /// <summary>The screen of <paramref name="thread"/>, opened on first use; null is main.</summary>
    public TranscriptView For(string? thread)
    {
        if (thread is null) return Main;
        if (!_tasks.TryGetValue(thread, out var screen))
        {
            screen = Create();
            _tasks[thread] = screen;
        }
        return screen;
    }

    /// <summary>Makes the screen of <paramref name="thread"/> the displayed one, opening it; true when the displayed screen changed.</summary>
    public bool Show(string? thread)
    {
        var screen = For(thread);
        DisplayedThread = thread;
        if (ReferenceEquals(screen, Displayed)) return false;
        Displayed = screen;
        return true;
    }

    /// <summary>Flips the shared wrap state and applies it to every screen.</summary>
    public void ToggleWordWrap()
    {
        var next = !WordWrap;
        _wordWrap = next;
        Main.WordWrap = next;
        foreach (var screen in _tasks.Values) screen.WordWrap = next;
    }

    public void DisposeScreens()
    {
        Main.Dispose();
        foreach (var screen in _tasks.Values) screen.Dispose();
    }

    private TranscriptView Create()
    {
        var screen = new TranscriptView(_capacity);
        _configure(screen);
        if (_wordWrap is { } wrap) screen.WordWrap = wrap;
        return screen;
    }
}
