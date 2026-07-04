using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands.Task;

internal sealed class ListTasksCommand : IMainMenuCommand
{
    public string Name => "task list";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "List all tasks";

    public System.Threading.Tasks.Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async System.Threading.Tasks.Task<CommandResult> ExecuteCoreAsync()
        {
            var service = context.Scope.GetRequiredService<TaskManagementService>();
            await service.ListTasksAsync().ConfigureAwait(false);
            return CommandResult.Continue();
        }
    }
}
