using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Commands;

/// <summary>Clears the console screen.</summary>
public sealed class ClearCommand : IInteractiveCommand
{
    public string Name => "clear";
    public IReadOnlyList<string> Aliases => new[] { "cls" };
    public string Description => "Clear screen";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Console.Clear();
        return Task.FromResult(CommandResult.Continue());
    }
}
