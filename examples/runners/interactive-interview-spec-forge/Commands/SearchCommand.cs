using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Examples.Interactive.InterviewSpecForge.Corpus;

namespace Orkeon.Examples.Interactive.InterviewSpecForge.Commands;

/// <summary>
/// Semantic search over the entire experiment corpus.
/// Searches transcriptions, summaries, CRs, topics, tasks, and the glossary.
/// Returns ranked citations with source file paths.
/// </summary>
public sealed class SearchCommand : IInteractiveCommand
{
    private readonly CorpusSearchIndex _index;
    private readonly ILogger<SearchCommand> _logger;

    public SearchCommand(CorpusSearchIndex index, ILogger<SearchCommand>? logger = null)
    {
        _index = index;
        _logger = logger ?? NullLogger<SearchCommand>.Instance;
    }

    public string Name => "search";
    public IReadOnlyList<string> Aliases => ["?"];
    public string Description => "search <requête> — recherche sémantique dans tout le corpus (citations)";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var query = string.Join(" ", context.Args).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            context.Console.WriteLine("  Usage   : search <requête>");
            context.Console.WriteLine("  Exemple : search statuts d'une commande");
            context.Console.WriteLine("  Alias   : ?  <requête>");
            return CommandResult.Continue();
        }

        if (!_index.IsBuilt)
            context.Console.WriteLine("  Indexation du corpus en cours… (première utilisation, ~quelques secondes)");

        IReadOnlyList<CorpusHit> hits;
        try
        {
            hits = await _index.SearchAsync(query, topK: 10, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Continue("  Recherche annulée.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorpusSearchIndex.SearchAsync failed");
            return CommandResult.Continue($"  Erreur lors de la recherche : {ex.Message}");
        }

        context.Console.WriteLine("");

        if (hits.Count == 0)
        {
            context.Console.WriteLine($"  Aucun résultat pour « {query} ».");
            context.Console.WriteLine("");
            return CommandResult.Continue();
        }

        context.Console.WriteLine($"  {hits.Count} résultat(s) pour « {query} »");
        context.Console.WriteLine("");

        for (int i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            var lineInfo = h.LineHint > 0 ? $", ~ligne {h.LineHint}" : "";
            context.Console.WriteLine($"  [{i + 1}] {h.RelativePath}  (score: {h.Score:F2}{lineInfo})");
            context.Console.WriteLine($"      {h.Excerpt}");
            context.Console.WriteLine("");
        }

        return CommandResult.Continue();
    }
}
