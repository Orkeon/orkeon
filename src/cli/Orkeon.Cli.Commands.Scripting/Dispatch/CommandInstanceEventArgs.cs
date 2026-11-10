namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>The instance a <see cref="CommandInstanceRegistry"/> event is about.</summary>
public sealed class CommandInstanceEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="instance">The instance registered or settled.</param>
    public CommandInstanceEventArgs(CommandInstance instance)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    /// <summary>The instance registered or settled.</summary>
    public CommandInstance Instance { get; }
}

/// <summary>The instance whose <c>completed()</c> callback failed, and why (<see cref="CommandInstanceRegistry.CompletionFailed"/>).</summary>
public sealed class CommandCompletionFailedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="instance">The instance whose completion was being drained.</param>
    /// <param name="error">What the callback failed with.</param>
    public CommandCompletionFailedEventArgs(CommandInstance instance, string error)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Error = error ?? string.Empty;
    }

    /// <summary>The instance whose completion was being drained.</summary>
    public CommandInstance Instance { get; }

    /// <summary>What the callback failed with.</summary>
    public string Error { get; }
}
