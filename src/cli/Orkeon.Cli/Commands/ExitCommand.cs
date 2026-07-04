using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Commands;

/// <summary>Terminates the current runner's loop.</summary>
public sealed class ExitCommand : IInteractiveCommand
{
    public string Name => "exit";
    public IReadOnlyList<string> Aliases => new[] { "quit", "q" };
    public string Description => "Exit current runner";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => Task.FromResult(CommandResult.Exit("Goodbye!"));
}
