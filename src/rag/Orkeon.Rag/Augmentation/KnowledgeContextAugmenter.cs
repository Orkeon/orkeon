using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Knowledge;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Augmentation;

/// <summary>
/// Default <see cref="IKnowledgeContextAugmenter"/> (RAG-03/C4, profile-aware since
/// GAP-02): for each attachment, resolves the pipeline of its profile
/// (<see cref="KnowledgeAttachment.Profile"/>, else the default profile the augmenter was
/// built with) through the <see cref="IRagProfileResolver"/>, runs its retrieval half
/// alone (<see cref="IRagRetrievalCapable.RetrieveAsync"/>, attachment TopK as TopN), and
/// assembles a bounded context block with globally numbered <c>[n]</c> excerpts, their
/// <see cref="Citation"/>s, and a short grounding instruction.
/// </summary>
/// <remarks>
/// <para>Retrieval only: the profile's transform, hybrid, rerank and assembly stages run,
/// its generation stage never does — a task would otherwise pay a nested grounded
/// generation before its own LLM call. A profile whose pipeline cannot retrieve alone
/// (<c>corrective</c>, <c>adaptive</c>: their graphs interleave evaluation with generation)
/// fails with a <see cref="NotSupportedException"/> rather than generating anyway, and an
/// unknown profile fails as the resolver does.</para>
/// <para><see cref="KnowledgeAttachment.MinScore"/> filters on the score the pipeline
/// reports — the vector similarity under <c>fast</c>, the reranker's score under a
/// reranking profile. Token budget heuristic: <see cref="KnowledgeAttachment.MaxContextTokens"/>
/// (default <see cref="DefaultMaxContextTokens"/>) is converted to a character
/// budget at <see cref="CharsPerToken"/> characters per token — the usual
/// "1 token ≈ 4 characters" English-text rule of thumb. The budget is applied
/// per attachment and counts excerpt content only (markers and source headers
/// are excluded); the excerpt that crosses the boundary is truncated with a
/// marker and later chunks of that attachment are dropped.</para>
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

    private readonly IRagProfileResolver _profileResolver;
    private readonly string _defaultProfile;
    private readonly ILogger<KnowledgeContextAugmenter> _logger;

    /// <summary>Initializes the augmenter.</summary>
    /// <param name="profileResolver">Resolves an attachment's profile to its pipeline.</param>
    /// <param name="defaultProfile">
    /// Profile used by an attachment that names none — the host's <c>Orkeon:Rag:Profile</c>
    /// in the default registration.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public KnowledgeContextAugmenter(
        IRagProfileResolver profileResolver,
        string defaultProfile,
        ILogger<KnowledgeContextAugmenter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(profileResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultProfile);

        _profileResolver = profileResolver;
        _defaultProfile = defaultProfile;
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

        var retained = new List<(KnowledgeAttachment Attachment, Citation Source, string Excerpt)>();
        var seenChunkIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attachment in attachments)
        {
            var profile = string.IsNullOrWhiteSpace(attachment.Profile) ? _defaultProfile : attachment.Profile;
            var retriever = ResolveRetriever(profile);

            var answer = await retriever.RetrieveAsync(
                new RagQuery
                {
                    Text = taskInput,
                    Collection = attachment.Collection,
                    TopN = attachment.TopK,
                },
                cancellationToken).ConfigureAwait(false);

            var kept = CollectWithinBudget(attachment, answer.Citations, seenChunkIds);
            retained.AddRange(kept);

            LogCollectionQueried(attachment.Collection, profile, answer.Citations.Count, kept.Count);
        }

        return retained.Count == 0 ? null : Assemble(retained);
    }

    private IRagRetrievalCapable ResolveRetriever(string profile)
    {
        var pipeline = _profileResolver.Resolve(profile); // unknown profile fails loudly
        return pipeline as IRagRetrievalCapable
            ?? throw new NotSupportedException(
                $"Knowledge attachment profile '{profile}' resolves to {pipeline.GetType().Name}, which " +
                "cannot retrieve without generating (it does not implement IRagRetrievalCapable). " +
                "Knowledge is injected from retrieval alone: give the attachment (or rag.defaults.profile, " +
                "or Orkeon:Rag:Profile) a staged profile — fast, balanced or quality.");
    }

    /// <summary>
    /// Applies MinScore filtering, TopK, cross-attachment deduplication and the
    /// per-attachment character budget (see class remarks for the heuristic).
    /// </summary>
    private static List<(KnowledgeAttachment, Citation, string)> CollectWithinBudget(
        KnowledgeAttachment attachment,
        IReadOnlyList<Citation> candidates,
        HashSet<string> seenChunkIds)
    {
        var budgetChars = (attachment.MaxContextTokens ?? DefaultMaxContextTokens) * CharsPerToken;
        var kept = new List<(KnowledgeAttachment, Citation, string)>();

        foreach (var citation in candidates
                     .Where(c => attachment.MinScore is not { } min || c.Score >= min)
                     .OrderByDescending(c => c.Score)
                     .Take(attachment.TopK))
        {
            if (!seenChunkIds.Add(citation.ChunkId))
                continue;

            if (budgetChars <= 0)
                break;

            var content = citation.Content ?? citation.Snippet ?? string.Empty;
            if (content.Length > budgetChars)
                content = content[..budgetChars] + TruncationSuffix;

            budgetChars -= content.Length;
            kept.Add((attachment, citation, content));
        }

        return kept;
    }

    /// <summary>Builds the numbered block text and its citations (global numbering, attachment order).</summary>
    private static KnowledgeContextBlock Assemble(
        List<(KnowledgeAttachment Attachment, Citation Source, string Excerpt)> retained)
    {
        var text = new StringBuilder();
        text.AppendLine(BlockHeader);
        text.AppendLine();
        text.AppendLine(GroundingInstruction);

        var citations = ImmutableList.CreateBuilder<Citation>();

        for (var i = 0; i < retained.Count; i++)
        {
            var (attachment, source, excerpt) = retained[i];
            var marker = i + 1;

            text.AppendLine();
            text.Append('[').Append(marker).Append("] (collection: ").Append(attachment.Collection)
                .Append(", source: ").Append(source.SourceId)
                .Append(", score: ").Append(source.Score.ToString("0.00", CultureInfo.InvariantCulture))
                .AppendLine(")");
            text.AppendLine(excerpt);

            var passage = source.Content ?? source.Snippet ?? string.Empty;
            citations.Add(source with
            {
                Marker = marker,
                Snippet = passage.Length <= CitationSnippetLength
                    ? passage
                    : passage[..CitationSnippetLength],
            });
        }

        return new KnowledgeContextBlock
        {
            Text = text.ToString().TrimEnd(),
            Citations = citations.ToImmutable(),
        };
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Knowledge collection '{Collection}' queried (profile '{Profile}'): {Candidates} candidate(s), {Kept} excerpt(s) retained.")]
    private partial void LogCollectionQueried(string collection, string profile, int candidates, int kept);
}
