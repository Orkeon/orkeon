using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Crew;

internal sealed class ListCrewsCommand : IMainMenuCommand
{
    public string Name => "crew list";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "List all crews";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<CrewManagementService>();
            await service.ListCrewsAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
