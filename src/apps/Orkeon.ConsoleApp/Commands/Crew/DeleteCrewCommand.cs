using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Crew;

internal sealed class DeleteCrewCommand : IMainMenuCommand
{
    public string Name => "crew delete";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Delete a crew";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<CrewManagementService>();
            await service.DeleteCrewAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
