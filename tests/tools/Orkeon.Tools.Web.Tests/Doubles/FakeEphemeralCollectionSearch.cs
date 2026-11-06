using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// An <see cref="IEphemeralCollectionSearch"/> without embeddings: a source scores by the words its
/// content holds, one chunk per word found.
/// </summary>
internal sealed class FakeEphemeralCollectionSearch : IEphemeralCollectionSearch
{
    /// <summary>Every request received, in order.</summary>
    public List<EphemeralSearchRequest> Requests { get; } = [];

    /// <summary>The score a source gets for each word its content holds.</summary>
    public Dictionary<string, double> Scores { get; } = new(StringComparer.Ordinal);

    /// <summary>Thrown by the next searches when set.</summary>
    public Exception? Failure { get; set; }

    public Task<EphemeralSearchResult> SearchAsync(EphemeralSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        if (Failure is not null)
            throw Failure;

        var results = new List<ScoredChunk>();
        foreach (var source in request.Sources)
        {
            var content = source.Options[InlineTextLoader.ContentOptionKey];
            var metadata = source.Options
                .Where(option => option.Key.StartsWith(InlineTextLoader.MetadataOptionPrefix, StringComparison.Ordinal))
                .ToImmutableDictionary(option => option.Key[InlineTextLoader.MetadataOptionPrefix.Length..], option => option.Value);
            foreach (var (word, score) in Scores.Where(pair => content.Contains(pair.Key, StringComparison.Ordinal)))
            {
                results.Add(new ScoredChunk
                {
                    Chunk = new Chunk
                    {
                        Id = $"{source.Location}/{word}",
                        DocumentId = source.Location,
                        SourceId = source.SourceId ?? source.Location,
                        Content = content,
                        Metadata = metadata,
                    },
                    Score = score,
                });
            }
        }

        const string Collection = "firefox-test";
        return Task.FromResult(new EphemeralSearchResult
        {
            Collection = Collection,
            Results = results,
            Ingestion = new IngestionReport { Collection = Collection },
        });
    }
}
