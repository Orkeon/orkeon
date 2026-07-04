using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Crew;

internal sealed class CreateCrewCommand : IMainMenuCommand
{
    public string Name => "crew create";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Create a new crew (interactive prompts)";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<CrewManagementService>();
            await service.CreateCrewAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
