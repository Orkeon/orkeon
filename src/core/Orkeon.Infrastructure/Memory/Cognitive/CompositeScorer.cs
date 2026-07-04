using Orkeon.Domain.Memory;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Memory.Cognitive;

/// <summary>
/// Calculates composite scores for memory items by combining semantic similarity,
/// temporal recency, and importance dimensions.
/// </summary>
public sealed class CompositeScorer
{
    private readonly CognitiveMemoryOptions _options;

    /// <summary>Initializes a new instance of <see cref="CompositeScorer"/>.</summary>
    public CompositeScorer(IOptions<CognitiveMemoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Scores the given semantic search results using composite scoring.
    /// </summary>
    /// <param name="semanticResults">Results from vector similarity search.</param>
    /// <param name="options">Recall options controlling weights and filtering.</param>
    /// <returns>Ranked list of scored memories.</returns>
    public IReadOnlyList<ScoredMemory> Score(
        IEnumerable<ScoredMemoryItem> semanticResults,
        RecallOptions options)
    {
        ArgumentNullException.ThrowIfNull(semanticResults);
        ArgumentNullException.ThrowIfNull(options);
        var now = DateTime.UtcNow;
        var halfLifeHours = _options.RecencyHalfLifeHours;
        // Decay constant: ln(2) / halfLife so that at halfLife hours the recency score is 0.5
        var decayConstant = Math.Log(2) / halfLifeHours;

        var scored = new List<ScoredMemory>();

        foreach (var result in semanticResults)
        {
            var item = result.Item;
            var semanticScore = result.Score;

            // Recency: exponential decay based on age
            var ageHours = (now - item.Timestamp).TotalHours;
            var recencyScore = (float)Math.Exp(-decayConstant * Math.Max(0, ageHours));

            // Importance: directly from the memory item
            var importanceScore = item.Importance;

            // Apply tag filter if specified
            if (options.TagFilter is { Count: > 0 })
            {
                var itemTags = item.Tags;
                if (!options.TagFilter.Any(t => itemTags.Contains(t)))
                    continue;
            }

            // Composite score
            var composite = options.SemanticWeight * semanticScore
                          + options.RecencyWeight * recencyScore
                          + options.ImportanceWeight * importanceScore;

            if (composite < options.MinScore)
                continue;

            scored.Add(new ScoredMemory
            {
                Item = item,
                CompositeScore = composite,
                SemanticScore = semanticScore,
                RecencyScore = recencyScore,
                ImportanceScore = importanceScore
            });
        }

        return scored
            .OrderByDescending(s => s.CompositeScore)
            .Take(options.TopK)
            .ToList();
    }
}
