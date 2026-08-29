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

    internal CommandDispatchScope(string commandName, TimeSpan? timeout, Action onDispose, CancellationToken cancellationToken)
    {
        CommandName = commandName;
        CancellationToken = cancellationToken;
        Timeout = timeout;
        _onDispose = onDispose;
    }

    /// <summary>Name of the command currently executing.</summary>
    public string CommandName { get; }

    /// <summary>Cooperative cancellation token for synchronous requests (REPL Ctrl-C).</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Per-request timeout, or <see langword="null"/> for the channel default.</summary>
    public TimeSpan? Timeout { get; }

    /// <summary>Instances created by <c>post()</c> during this scope, in call order.</summary>
    public IReadOnlyList<CommandInstance> Captured => _captured;

    internal void Capture(CommandInstance instance) => _captured.Add(instance);

    /// <inheritdoc />
    public void Dispose() => _onDispose();
}
