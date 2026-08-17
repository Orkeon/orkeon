using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Args;
using Orkeon.Cli.Commands.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Registry;

/// <summary>
/// Companion command to <c>help</c> from <c>DefaultCommandRegistry</c>: prints the
/// detailed signature (description + typed args) of a scripted command.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the <see cref="ScriptCommandRegistry"/> so that <c>HelpCommand</c>
/// (in <c>Orkeon.Cli</c>) stays untouched — the plan Q4 arbitrated this layout to avoid
/// any cross-project modification. Usage:
/// </para>
/// <code>
///   help-cmd deploy
/// </code>
/// </remarks>
public sealed class ScriptHelpCommand : IInteractiveCommand
{
    private readonly Func<IReadOnlyList<ScriptCommand>> _scriptCommandsAccessor;

    public ScriptHelpCommand(Func<IReadOnlyList<ScriptCommand>> scriptCommandsAccessor)
    {
        _scriptCommandsAccessor = scriptCommandsAccessor ?? throw new ArgumentNullException(nameof(scriptCommandsAccessor));
    }

    /// <inheritdoc />
    public string Name => "help-cmd";

    /// <inheritdoc />
    public IReadOnlyList<string> Aliases => Array.Empty<string>();

    /// <inheritdoc />
    public string Description => "Show detailed help for a scripted command (usage: help-cmd <name>).";

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Args.Count == 0)
        {
            context.Console.WriteLine("usage: help-cmd <command-name>");
            return Task.FromResult(CommandResult.Continue());
        }

        var target = context.Args[0];
        var commands = _scriptCommandsAccessor();
        var match = commands.FirstOrDefault(c =>
            string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase) ||
            c.Aliases.Any(a => string.Equals(a, target, StringComparison.OrdinalIgnoreCase)));

        if (match is null)
        {
            context.Console.WriteLine($"No scripted command found for '{target}'. Type 'help' to list available commands.");
            return Task.FromResult(CommandResult.Continue());
        }

        var help = ArgsHelpFormatter.Format(match.Name, match.Aliases, match.Description, match.Descriptor.ArgsSchema);
        context.Console.WriteLine(help);
        return Task.FromResult(CommandResult.Continue());
    }
}
