using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Task;

internal sealed class DeleteTaskCommand : IMainMenuCommand
{
    public string Name => "task delete";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Delete a task";

    public System.Threading.Tasks.Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async System.Threading.Tasks.Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<TaskManagementService>();
            await service.DeleteTaskAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
