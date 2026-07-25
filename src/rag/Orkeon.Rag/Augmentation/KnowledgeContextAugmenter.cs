using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Augmentation;

/// <summary>
/// Default <see cref="IKnowledgeContextAugmenter"/> (RAG-03/C4): embeds the task
/// input once, queries each attached collection through the
/// <see cref="IDocumentStore"/> (attachment TopK / MinScore), and assembles a
/// bounded context block with globally numbered <c>[n]</c> excerpts, their
/// <see cref="Citation"/>s, and a short grounding instruction.
/// </summary>
/// <remarks>
/// Token budget heuristic: <see cref="KnowledgeAttachment.MaxContextTokens"/>
/// (default <see cref="DefaultMaxContextTokens"/>) is converted to a character
/// budget at <see cref="CharsPerToken"/> characters per token — the usual
/// "1 token ≈ 4 characters" English-text rule of thumb. The budget is applied
/// per attachment and counts excerpt content only (markers and source headers
/// are excluded); the excerpt that crosses the boundary is truncated with a
/// marker and later chunks of that attachment are dropped.
/// </remarks>
public sealed partial class KnowledgeContextAugmenter : IKnowledgeContextAugmenter
{
    /// <summary>Header line opening the injected block.</summary>
    public const string BlockHeader = "## Knowledge Context";

    /// <summary>Short grounding instruction placed right under the header.</summary>
    public const string GroundingInstruction =
        "Answer from the excerpts below and cite the passages you use with their [n] markers. " +
        "If the excerpts do not cover the answer, say so explicitly.";

    /// <summary>Default context budget (tokens) when the attachment does not set <see cref="KnowledgeAttachment.MaxContextTokens"/> (plan RAG §8.1).</summary>
    public const int DefaultMaxContextTokens = 2000;

    /// <summary>Approximate characters per token (heuristic: 1 token ≈ 4 characters).</summary>
    public const int CharsPerToken = 4;

    /// <summary>Suffix appended to an excerpt cut by the context budget.</summary>
    public const string TruncationSuffix = "... [truncated]";

    private const int CitationSnippetLength = 200;

    private readonly IDocumentStore _store;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<KnowledgeContextAugmenter> _logger;

    /// <summary>Initializes the augmenter.</summary>
    /// <param name="store">Document store queried per attached collection.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed the task input.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public KnowledgeContextAugmenter(
        IDocumentStore store,
        IEmbeddingProvider embeddingProvider,
        ILogger<KnowledgeContextAugmenter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddingProvider);

        _store = store;
        _embeddingProvider = embeddingProvider;
        _logger = logger ?? NullLogger<KnowledgeContextAugmenter>.Instance;
    }

    /// <inheritdoc />
    public async Task<KnowledgeContextBlock?> BuildContextAsync(
        IReadOnlyList<KnowledgeAttachment> attachments,
        string taskInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        if (attachments.Count == 0 || string.IsNullOrWhiteSpace(taskInput))
            return null;

        var vector = await _embeddingProvider.GetEmbeddingAsync(taskInput, cancellationToken)
            .ConfigureAwait(false);
        ImmutableArray<float>? embedding = vector is { Length: > 0 } ? [.. vector] : null;

        var retained = new List<(KnowledgeAttachment Attachment, ScoredChunk Scored, string Excerpt)>();
        var seenChunkIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attachment in attachments)
        {
            var query = new RetrievalQuery
            {
                Text = taskInput,
                Embedding = embedding,
                TopK = attachment.TopK,
            };

            var candidates = await _store.SearchAsync(attachment.Collection, query, cancellationToken)
                .ConfigureAwait(false);

            var kept = CollectWithinBudget(attachment, candidates, seenChunkIds);
            retained.AddRange(kept);

            LogCollectionQueried(attachment.Collection, candidates.Count, kept.Count);
        }

        return retained.Count == 0 ? null : Assemble(retained);
    }

    /// <summary>
    /// Applies MinScore filtering, TopK, cross-attachment deduplication and the
    /// per-attachment character budget (see class remarks for the heuristic).
    /// </summary>
    private static List<(KnowledgeAttachment, ScoredChunk, string)> CollectWithinBudget(
        KnowledgeAttachment attachment,
        IReadOnlyList<ScoredChunk> candidates,
        HashSet<string> seenChunkIds)
    {
        var budgetChars = (attachment.MaxContextTokens ?? DefaultMaxContextTokens) * CharsPerToken;
        var kept = new List<(KnowledgeAttachment, ScoredChunk, string)>();

        foreach (var scored in candidates
                     .Where(c => attachment.MinScore is not { } min || c.Score >= min)
                     .OrderByDescending(c => c.Score)
                     .Take(attachment.TopK))
        {
            if (!seenChunkIds.Add(scored.Chunk.Id))
                continue;

            if (budgetChars <= 0)
                break;

            var content = scored.Chunk.Content;
            if (content.Length > budgetChars)
                content = content[..budgetChars] + TruncationSuffix;

            budgetChars -= content.Length;
            kept.Add((attachment, scored, content));
        }

        return kept;
    }

    /// <summary>Builds the numbered block text and its citations (global numbering, attachment order).</summary>
    private static KnowledgeContextBlock Assemble(
        List<(KnowledgeAttachment Attachment, ScoredChunk Scored, string Excerpt)> retained)
    {
        var text = new StringBuilder();
        text.AppendLine(BlockHeader);
        text.AppendLine();
        text.AppendLine(GroundingInstruction);

        var citations = ImmutableList.CreateBuilder<Citation>();

        for (var i = 0; i < retained.Count; i++)
        {
            var (attachment, scored, excerpt) = retained[i];
            var chunk = scored.Chunk;
            var marker = i + 1;

            text.AppendLine();
            text.Append('[').Append(marker).Append("] (collection: ").Append(attachment.Collection)
                .Append(", source: ").Append(chunk.SourceId)
                .Append(", score: ").Append(scored.Score.ToString("0.00", CultureInfo.InvariantCulture))
                .AppendLine(")");
            text.AppendLine(excerpt);

            citations.Add(new Citation
            {
                Marker = marker,
                ChunkId = chunk.Id,
                SourceId = chunk.SourceId,
                DocumentId = chunk.DocumentId,
                Snippet = chunk.Content.Length <= CitationSnippetLength
                    ? chunk.Content
                    : chunk.Content[..CitationSnippetLength],
                Score = scored.Score,
                StartOffset = chunk.StartOffset,
                EndOffset = chunk.EndOffset,
            });
        }

        return new KnowledgeContextBlock
        {
            Text = text.ToString().TrimEnd(),
            Citations = citations.ToImmutable(),
        };
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Knowledge collection '{Collection}' queried: {Candidates} candidate(s), {Kept} excerpt(s) retained.")]
    private partial void LogCollectionQueried(string collection, int candidates, int kept);
}
