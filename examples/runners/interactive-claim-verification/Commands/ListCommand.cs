using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.ClaimVerification.Commands;

/// <summary>
/// Lists every claim file present in the corpus with slot, slug, and source path.
/// </summary>
public sealed class ListCommand : IInteractiveCommand
{
    private readonly ClaimsCatalog _catalog;

    public ListCommand(ClaimsCatalog catalog) => _catalog = catalog;

    public string Name => "list";
    public IReadOnlyList<string> Aliases => new[] { "ls" };
    public string Description => "List all claims in the corpus";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var entries = _catalog.Discover();
        if (entries.Count == 0)
        {
            context.Console.WriteLine($"  (no claim found under {_catalog.ClaimsRoot})");
            return Task.FromResult(CommandResult.Continue());
        }

        context.Console.WriteLine("");
        context.Console.WriteLine($"  Claims under {_catalog.ClaimsRoot}:");
        context.Console.WriteLine("");

        var slugWidth = entries.Max(e => e.Slug.Length);
        foreach (var e in entries)
        {
            context.Console.WriteLine($"    {e.Slot}  {e.Slug.PadRight(slugWidth)}  {Path.GetFileName(e.FilePath)}");
        }
        context.Console.WriteLine("");

        return Task.FromResult(CommandResult.Continue());
    }
}
