namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>The instance a <see cref="CommandInstanceRegistry"/> event is about.</summary>
/// <param name="instance">The instance registered or settled.</param>
public sealed class CommandInstanceEventArgs(CommandInstance instance) : EventArgs
{
    /// <summary>The instance registered or settled.</summary>
    public CommandInstance Instance { get; } = instance ?? throw new ArgumentNullException(nameof(instance));
}

/// <summary>The instance whose <c>completed()</c> callback failed, and why (<see cref="CommandInstanceRegistry.CompletionFailed"/>).</summary>
/// <param name="instance">The instance whose completion was being drained.</param>
/// <param name="error">What the callback failed with.</param>
public sealed class CommandCompletionFailedEventArgs(CommandInstance instance, string error) : EventArgs
{
    /// <summary>The instance whose completion was being drained.</summary>
    public CommandInstance Instance { get; } = instance ?? throw new ArgumentNullException(nameof(instance));

    /// <summary>What the callback failed with.</summary>
    public string Error { get; } = error ?? string.Empty;
}
