namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// Ambient context for one command invocation, pushed by the runner around a handler /
/// <c>dispatch</c> call so the facade's <c>request</c>/<c>post</c> know the originating
/// command name, the cooperative cancellation token, and where to record the instances a
/// <c>dispatch</c> created (for the async <c>completed</c> drain — design §4.3).
/// </summary>
public sealed class CommandDispatchScope : IDisposable
{
    private readonly Action _onDispose;
    private readonly List<CommandInstance> _captured = new();
    private readonly List<CommandInstance> _turn = new();

    internal CommandDispatchScope(string commandName, TimeSpan? timeout, Action onDispose, CancellationToken cancellationToken, bool isPrompt = false, CommandDispatchScope? promptOwner = null, int depth = 0, string? parentCommand = null)
    {
        Depth = depth;
        ParentCommand = parentCommand;
        PromptOwner = isPrompt ? this : promptOwner;
        CommandName = commandName;
        CancellationToken = cancellationToken;
        Timeout = timeout;
        _onDispose = onDispose;
    }

    /// <summary>Chain depth of this scope.</summary>
    public int Depth { get; }

    /// <summary>
    /// Name of the command owning the instance whose <c>completed</c> runs in this scope, or <see langword="null"/>
    /// outside a <c>completed</c> drain. A launch asking to continue the chain keeps <see cref="Depth"/> only when it
    /// equals <see cref="CommandName"/>.
    /// </summary>
    internal string? ParentCommand { get; }

    /// <summary>Name of the command currently executing.</summary>
    public string CommandName { get; }

    /// <summary>Cooperative cancellation token for synchronous requests (REPL Ctrl-C).</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Per-request timeout, or <see langword="null"/> for the channel default.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>Instances created by <c>post()</c> during this scope, in call order.</summary>
    public IReadOnlyList<CommandInstance> Captured => _captured;

    /// <summary>The prompt scope this scope belongs to (itself when opened with a prompt line), or null.</summary>
    internal CommandDispatchScope? PromptOwner { get; }

    /// <summary>Instances registered under this prompt scope, nested scopes included.</summary>
    internal IReadOnlyList<CommandInstance> Turn { get { lock (_turn) return _turn.ToArray(); } }

    internal void Capture(CommandInstance instance)
    {
        _captured.Add(instance);
        if (PromptOwner is { } owner)
            lock (owner._turn) owner._turn.Add(instance);
    }

    /// <inheritdoc />
    public void Dispose() => _onDispose();
}
