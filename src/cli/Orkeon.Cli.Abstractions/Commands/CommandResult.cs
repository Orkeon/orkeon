namespace Orkeon.Cli.Abstractions.Commands;

/// <summary>
/// Result returned by an <see cref="IInteractiveCommand"/>. Tells the runner
/// whether to continue the loop and provides an optional message to display.
/// </summary>
public sealed record CommandResult
{
    /// <summary>If true, the runner terminates its loop after this command.</summary>
    public bool ShouldExit { get; init; }

    /// <summary>Optional message displayed by the runner after the command executes.</summary>
    public string? Message { get; init; }

    /// <summary>Continue the loop without displaying any message.</summary>
    public static CommandResult Continue() => new() { ShouldExit = false };

    /// <summary>Continue the loop and display the given message.</summary>
    public static CommandResult Continue(string message) => new() { ShouldExit = false, Message = message };

    /// <summary>Terminate the loop without displaying any message.</summary>
    public static CommandResult Exit() => new() { ShouldExit = true };

    /// <summary>Terminate the loop and display the given farewell message.</summary>
    public static CommandResult Exit(string farewell) => new() { ShouldExit = true, Message = farewell };
}
