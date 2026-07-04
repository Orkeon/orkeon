using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Displays the first ~40 lines of one transcript by slot, for quick preview.
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — runner-level command operating on the static
/// transcript corpus before the per-forge VFS is built.
/// </remarks>
public sealed class ShowCommand : IInteractiveCommand
{
    private const int PreviewLines = 40;

    private readonly TranscriptsCatalog _catalog;

    public ShowCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "show";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "show <slot> — print the first ~40 lines of one transcript";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var raw = context.Args is [var first, ..] ? first : null;
        if (string.IsNullOrEmpty(raw))
            return CommandResult.Continue("Usage: show <slot>  (e.g. 'show 001')");

        var entry = _catalog.Resolve(raw);
        if (entry is null)
            return CommandResult.Continue($"Unknown slot '{raw}'. Try 'list' to see available slots.");

        var lines = await File.ReadAllLinesAsync(entry.FilePath, cancellationToken).ConfigureAwait(false);

        context.Console.WriteLine("");
        context.Console.WriteLine($"  ── {Path.GetFileName(entry.FilePath)} ({lines.Length} lines) ──");
        var preview = Math.Min(PreviewLines, lines.Length);
        for (int i = 0; i < preview; i++)
            context.Console.WriteLine(lines[i]);
        if (lines.Length > preview)
            context.Console.WriteLine($"  …({lines.Length - preview} more lines)");
        context.Console.WriteLine("");

        return CommandResult.Continue();
    }
}
