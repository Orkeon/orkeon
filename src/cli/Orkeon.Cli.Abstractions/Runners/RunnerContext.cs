using Orkeon.Cli.Abstractions.Registry;

namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// Tracks the registry of the currently executing runner via an
/// <see cref="AsyncLocal{T}"/> stack. Allows shared commands like
/// <c>HelpCommand</c> to discover the active runner's commands without
/// explicit DI coupling, and supports nested runners (a runner launched
/// from another runner inherits its own context until disposed).
/// </summary>
public static class RunnerContext
{
    private static readonly AsyncLocal<Stack<IInteractiveCommandRegistry>?> _stack = new();

    /// <summary>The registry of the most recently pushed (= currently running) runner, or <see langword="null"/>.</summary>
    public static IInteractiveCommandRegistry? Current
        => _stack.Value is { Count: > 0 } stack ? stack.Peek() : null;

    /// <summary>Push a registry. Disposing the returned token pops it back.</summary>
    public static IDisposable Push(IInteractiveCommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var stack = _stack.Value ??= new Stack<IInteractiveCommandRegistry>();
        stack.Push(registry);
        return new PopOnDispose(stack, registry);
    }

    private sealed class PopOnDispose : IDisposable
    {
        private readonly Stack<IInteractiveCommandRegistry> _registryStack;
        private readonly IInteractiveCommandRegistry _expected;
        private bool _disposed;

        public PopOnDispose(Stack<IInteractiveCommandRegistry> stack, IInteractiveCommandRegistry expected)
        {
            _registryStack = stack;
            _expected = expected;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_registryStack.Count > 0 && ReferenceEquals(_registryStack.Peek(), _expected))
            {
                _registryStack.Pop();
            }
        }
    }
}
