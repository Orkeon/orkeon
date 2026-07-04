using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Agent;

internal sealed class ListAgentsCommand : IMainMenuCommand
{
    public string Name => "agent list";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "List all agents";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<AgentManagementService>();
            await service.ListAgentsAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
