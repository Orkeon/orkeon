using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Crew;

internal sealed class AddAgentToCrewCommand : IMainMenuCommand
{
    public string Name => "crew add-agent";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Add an agent to an existing crew";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<CrewManagementService>();
            await service.AddAgentToCrewAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
