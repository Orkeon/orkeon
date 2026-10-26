namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>The instance a <see cref="CommandInstanceRegistry"/> event is about.</summary>
/// <param name="instance">The instance registered or settled.</param>
public sealed class CommandInstanceEventArgs(CommandInstance instance) : EventArgs
{
    /// <summary>The instance registered or settled.</summary>
    public CommandInstance Instance { get; } = instance ?? throw new ArgumentNullException(nameof(instance));
}
