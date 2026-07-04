using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.ClaimVerification.Commands;

/// <summary>
/// Displays the raw markdown content of one claim by slot.
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — runner-level command operating on the static
/// claim corpus before the per-verify VFS is built.
/// </remarks>
public sealed class ShowCommand : IInteractiveCommand
{
    private readonly ClaimsCatalog _catalog;

    public ShowCommand(ClaimsCatalog catalog) => _catalog = catalog;

    public string Name => "show";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Show <slot> — print the markdown body of one claim";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var raw = context.Args.Count > 0 ? context.Args[0] : null;
        if (string.IsNullOrEmpty(raw))
            return CommandResult.Continue("Usage: show <slot>  (e.g. 'show 001')");

        var entry = _catalog.Resolve(raw);
        if (entry is null)
            return CommandResult.Continue($"Unknown slot '{raw}'. Try 'list' to see available slots.");

        var content = await File.ReadAllTextAsync(entry.FilePath, cancellationToken).ConfigureAwait(false);
        context.Console.WriteLine("");
        context.Console.WriteLine($"  ── {Path.GetFileName(entry.FilePath)} ──");
        context.Console.WriteLine(content.TrimEnd());
        context.Console.WriteLine("");

        return CommandResult.Continue();
    }
}
