using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Reranking;

/// <summary>
/// <c>decision</c> (alias <c>typesafe</c>): reranks with the host's decision model in one request —
/// the query and every passage as the state, one yes-or-no question per passage, "is this passage
/// relevant to the query". The passages come back by the probability of a yes, highest first, that
/// probability as their score.
/// </summary>
public sealed class DecisionModelReranker : IReranker
{
    /// <summary>The name a pipeline's <c>rerank.kind</c> uses.</summary>
    public const string RerankerName = "decision";

    /// <summary>The alias naming the provider.</summary>
    public const string TypeSafeAlias = "typesafe";

    /// <summary>Value stamped on <see cref="ScoredChunk.ScoreOrigin"/>.</summary>
    public const string ScoreOrigin = "decision-rerank";

    /// <summary>The characters of a passage shown to the model; the rest is cut.</summary>
    public const int MaxPassageChars = 4000;

    private readonly IDecisionModel _model;

    /// <summary>Initializes a new instance of <see cref="DecisionModelReranker"/>.</summary>
    /// <param name="model">The host's decision model.</param>
    public DecisionModelReranker(IDecisionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    /// <inheritdoc />
    public string Name => RerankerName;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 || topN <= 0)
            return [];

        var passages = new JsonObject();
        var questions = ImmutableDictionary.CreateBuilder<string, DecisionQuestion>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var id = Id(i);
            var content = candidates[i].Chunk.Content;
            passages[id] = content.Length > MaxPassageChars ? content[..MaxPassageChars] : content;
            questions[id] = new NoulQuestion
            {
                Criteria = new NoulCriteria { True = JsonValue.Create($"Passage {id} is relevant to the query and helps answer it.") },
            };
        }

        var response = await _model.DecideAsync(new DecisionRequest
        {
            State = new JsonObject { ["query"] = query, ["passages"] = passages },
            Questions = questions.ToImmutable(),
        }, cancellationToken).ConfigureAwait(false);

        return [.. candidates
            .Select((candidate, i) => candidate with
            {
                Score = response.Get<NoulAnswer>(Id(i)).Noul,
                ScoreOrigin = ScoreOrigin,
            })
            .OrderByDescending(c => c.Score)
            .Take(topN)];
    }

    private static string Id(int index) => "p" + index.ToString(CultureInfo.InvariantCulture);
}
