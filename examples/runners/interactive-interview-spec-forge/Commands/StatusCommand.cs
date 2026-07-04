using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Tabular overview of the corpus: per transcript, presence of
/// summary / cr / cr.signed / indexed (latest attempt of the round).
/// </summary>
/// <remarks>
/// // EXCEPTION-BOOTSTRAP — read-only filesystem probing across the
/// experiment root.
/// </remarks>
public sealed class StatusCommand : IInteractiveCommand
{
    private readonly TranscriptsCatalog _catalog;

    public StatusCommand(TranscriptsCatalog catalog) => _catalog = catalog;

    public string Name => "status";
    public IReadOnlyList<string> Aliases => new[] { "st" };
    public string Description => "Show corpus progress: per-transcript summary/cr/signed/indexed state";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var entries = _catalog.Discover();
        if (entries.Count == 0)
        {
            context.Console.WriteLine($"  (no transcript found under {_catalog.TranscriptsRoot})");
            return Task.FromResult(CommandResult.Continue());
        }

        context.Console.WriteLine("");
        context.Console.WriteLine($"  Corpus status — {entries.Count} transcript(s):");
        context.Console.WriteLine("");
        context.Console.WriteLine("    slot  basename                                              summary  cr   signed  indexed");
        context.Console.WriteLine("    ────  ────────────────────────────────────────────────────  ───────  ───  ──────  ───────");

        foreach (var e in entries)
        {
            var roundDir = _catalog.ResolveRoundDir(e);
            var latestAttempt = ResolveLatestAttempt(roundDir);
            var hasSummary = latestAttempt is not null
                && File.Exists(Path.Combine(latestAttempt, "outputs", $"{e.Basename}--summary.json"));
            var hasCr = latestAttempt is not null
                && File.Exists(Path.Combine(latestAttempt, "outputs", $"{e.Basename}--cr.json"));
            var hasSigned = latestAttempt is not null
                && File.Exists(Path.Combine(latestAttempt, "outputs", $"{e.Basename}--cr.signed.json"));
            var hasIndexed = latestAttempt is not null
                && Directory.Exists(Path.Combine(latestAttempt, "outputs", "topics"))
                && Directory.Exists(Path.Combine(latestAttempt, "outputs", "tasks"))
                && Directory.Exists(Path.Combine(latestAttempt, "outputs", "glossary"));

            context.Console.WriteLine(
                $"    {e.Slot}   {e.Basename.PadRight(54)}  {Mark(hasSummary)}      {Mark(hasCr)}    {Mark(hasSigned)}     {Mark(hasIndexed)}");
        }
        context.Console.WriteLine("");
        return Task.FromResult(CommandResult.Continue());
    }

    private static string Mark(bool present) => present ? " ok " : "  - ";

    private static string? ResolveLatestAttempt(string roundDir)
    {
        if (!Directory.Exists(roundDir)) return null;
        int max = 0; string? winner = null;
        foreach (var sub in Directory.EnumerateDirectories(roundDir, "attempt-*"))
        {
            var name = Path.GetFileName(sub);
            if (name.Length >= 10
                && name.StartsWith("attempt-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name[8..], out var n)
                && n > max)
            {
                max = n; winner = sub;
            }
        }
        return winner;
    }
}
