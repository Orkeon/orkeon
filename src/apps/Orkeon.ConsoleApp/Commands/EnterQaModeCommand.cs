using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Runners;

namespace Orkeon.ConsoleApp.Commands;

/// <summary>Cross-runner command: launches the <see cref="QaRunner"/> sub-loop.</summary>
internal sealed class EnterQaModeCommand : IMainMenuCommand
{
    public string Name => "qa";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Enter Q&A mode (sub-runner)";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var qaRunner = context.Scope.GetRequiredService<QaRunner>();
            await qaRunner.RunAsync(cancellationToken).ConfigureAwait(false);
            return CommandResult.Continue("Returned from Q&A mode.");
        }
    }
}
