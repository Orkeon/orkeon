using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Lists every transcript present in the corpus with slot, interview date,
/// person, subject, and source path.
/// </summary>
public sealed class ListCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;

    public ListCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "list";
    public IReadOnlyList<string> Aliases => new[] { "ls" };
    public string Description => "List all transcripts in the corpus";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var entries = _catalog.Discover();
        if (entries.Count == 0)
        {
            context.Console.WriteLine($"  (no transcript found under {_catalog.TranscriptsRoot})");
            return Task.FromResult(CommandResult.Continue());
        }

        context.Console.WriteLine("");
        context.Console.WriteLine($"  Transcripts under {_catalog.TranscriptsRoot}:");
        context.Console.WriteLine("");

        var personWidth = entries.Max(e => e.Person.Length);
        var subjectWidth = entries.Max(e => e.SubjectSlug.Length);
        foreach (var e in entries)
        {
            context.Console.WriteLine(
                $"    {e.Slot}  {e.InterviewDate:yyyy-MM-dd}  {e.Person.PadRight(personWidth)}  {e.SubjectSlug.PadRight(subjectWidth)}");
        }
        context.Console.WriteLine("");

        return Task.FromResult(CommandResult.Continue());
    }
}
