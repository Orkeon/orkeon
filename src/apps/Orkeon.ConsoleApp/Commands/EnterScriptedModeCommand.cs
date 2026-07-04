using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Runners;

namespace Orkeon.ConsoleApp.Commands;

/// <summary>Cross-runner command: launches the <see cref="ScriptedCommandsRunner"/> sub-loop.</summary>
internal sealed class EnterScriptedModeCommand : IMainMenuCommand
{
    public string Name => "scripted";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Enter scripted-commands REPL (loads *.cmd.ts files)";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            var runner = context.Scope.GetRequiredService<ScriptedCommandsRunner>();
            await runner.RunAsync(cancellationToken).ConfigureAwait(false);
            return CommandResult.Continue("Returned from scripted-commands mode.");
        }
    }
}
