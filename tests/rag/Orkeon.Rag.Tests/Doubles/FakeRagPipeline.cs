using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IRagPipeline"/>: returns the answer
/// scripted for the query text in <see cref="AnswersByQuestion"/> (else
/// <see cref="DefaultAnswer"/>) and records every query received.
/// </summary>
public sealed class FakeRagPipeline : IRagPipeline
{
    /// <summary>Scripted answers, keyed by exact question text.</summary>
    public Dictionary<string, RagAnswer> AnswersByQuestion { get; } = new(StringComparer.Ordinal);

    /// <summary>Answer returned when the question is not scripted.</summary>
    public RagAnswer DefaultAnswer { get; set; } = new() { Text = "fake answer" };

    /// <summary>Queries received, in order.</summary>
    public List<RagQuery> Queries { get; } = [];

    public Task<RagAnswer> QueryAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(
            AnswersByQuestion.TryGetValue(query.Text, out var scripted) ? scripted : DefaultAnswer);
    }
}
