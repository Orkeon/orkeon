using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Agent;

internal sealed class CreateAgentCommand : IMainMenuCommand
{
    public string Name => "agent create";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Create a new agent (interactive prompts)";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<AgentManagementService>();
            await service.CreateAgentAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
