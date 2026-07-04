using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Agent;

internal sealed class DeleteAgentCommand : IMainMenuCommand
{
    public string Name => "agent delete";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Delete an agent (prompts for ID)";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<AgentManagementService>();
            await service.DeleteAgentAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
