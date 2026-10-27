using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Tests.Doubles;

/// <summary>A command that declares a group, the way a scripted command does.</summary>
public sealed class StubGroupedCommand(string name, string? group, string description = "") : IInteractiveCommand
{
    public string Name => name;
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => description;
    public string? Group => group;

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => Task.FromResult(CommandResult.Continue());
}
