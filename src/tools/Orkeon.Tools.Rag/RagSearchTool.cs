using System.Globalization;
using System.Text;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Tools.Abstractions.Base;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag;

/// <summary>
/// Generic retrieval tool exposed to agents (<c>rag_search</c>). Routes queries to
/// the RAG subsystem's <see cref="IRagPipeline"/> or, when
/// <c>collection = "raggable-tree"</c>, to the <see cref="IRaggableStore"/> semantic
/// code search. Replaces the legacy Infrastructure <c>RagTool</c>
/// (RAG-02/C5) with the same agent-facing name; the result is
/// <c>{answer, citations}</c>.
/// </summary>
public class RagSearchTool : ToolBase<RagSearchRequest, RagSearchResponse>
{
    /// <summary>
    /// Collection queried when the agent names none and the host configures no
    /// <c>Orkeon:Rag:Collection</c>.
    /// </summary>
    public const string DefaultCollection = "default";

    /// <summary>Collection identifier that routes queries to the <see cref="IRaggableStore"/> code index.</summary>
    public const string RaggableTreeCollection = "raggable-tree";

    private readonly IRagPipeline _ragPipeline;
    private readonly IRaggableStore? _raggableStore;
    private readonly Func<string?> _defaultCollection;

    /// <inheritdoc />
    public override string Name => "rag_search";

    /// <inheritdoc />
    public override string Description =>
        "Search knowledge bases and retrieve answers grounded in documents. " +
        "Use this when you need factual information from the agent's knowledge sources.";

    /// <summary>Querying the index changes nothing: a read for the permission gate (#126).</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    public override ToolSchema Schema => new(
        Name: Name,
        Description: Description,
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["question"] = new ParameterSchema(
                "string",
                "The question to search for in knowledge bases",
                Required: true),
            ["top_k"] = new ParameterSchema(
                "integer",
                "Number of relevant chunks to retrieve (default: 3)",
                Required: false,
                Default: 3),
            ["collection"] = new ParameterSchema(
                "string",
                "Optional collection selector (default: the configured default collection; 'raggable-tree' for the code index)",
                Required: false)
        });

    /// <summary>Initializes a new instance of <see cref="RagSearchTool"/>.</summary>
    public RagSearchTool(IRagPipeline ragPipeline) : this(ragPipeline, raggableStore: null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="RagSearchTool"/> with an optional RaggableTree backend.</summary>
    public RagSearchTool(IRagPipeline ragPipeline, IRaggableStore? raggableStore)
        : this(ragPipeline, raggableStore, (string?)null)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RagSearchTool"/> with an optional RaggableTree
    /// backend and the host's default collection.
    /// </summary>
    /// <param name="ragPipeline">The RAG pipeline queried.</param>
    /// <param name="raggableStore">Optional code index (<c>collection = "raggable-tree"</c>).</param>
    /// <param name="defaultCollection">
    /// Collection queried when the agent names none — the host's <c>Orkeon:Rag:Collection</c>;
    /// <c>null</c> falls back to <see cref="DefaultCollection"/>.
    /// </param>
    public RagSearchTool(IRagPipeline ragPipeline, IRaggableStore? raggableStore, string? defaultCollection)
        : this(ragPipeline, raggableStore, () => defaultCollection)
    {
    }

    /// <summary>
    /// Deferred form used by <c>AddOrkeonRagTools</c>: the default collection is read at the
    /// first call, so building the tool resolves nothing of the RAG subsystem.
    /// </summary>
    internal RagSearchTool(IRagPipeline ragPipeline, IRaggableStore? raggableStore, Func<string?> defaultCollectionSource)
    {
        ArgumentNullException.ThrowIfNull(ragPipeline);
        ArgumentNullException.ThrowIfNull(defaultCollectionSource);
        _ragPipeline = ragPipeline;
        _raggableStore = raggableStore;
        _defaultCollection = defaultCollectionSource;
    }

    /// <inheritdoc />
    protected override Task<RagSearchResponse> ExecuteTypedAsync(
        RagSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<RagSearchResponse> ExecuteCoreAsync()
        {
            if (IsRaggableTreeCollection(request.Collection) && _raggableStore is not null)
            {
                var hits = await SearchRaggableTreeAsync(request.Question, request.TopK, cancellationToken).ConfigureAwait(false);
                return new RagSearchResponse { Answer = FormatRaggableHits(hits) };
            }

            var answer = await _ragPipeline.QueryAsync(
                new RagQuery
                {
                    Text = request.Question,
                    Collection = string.IsNullOrWhiteSpace(request.Collection) ? ResolveDefaultCollection() : request.Collection,
                    TopN = request.TopK,
                },
                cancellationToken).ConfigureAwait(false);

            return new RagSearchResponse { Answer = answer.Text, Citations = answer.Citations };
        }
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(RagSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.IsNullOrWhiteSpace(request.Question) ? "question parameter is required" : null;
    }

    /// <summary>The configured default collection, else <see cref="DefaultCollection"/>.</summary>
    private string ResolveDefaultCollection()
        => _defaultCollection() is { Length: > 0 } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : DefaultCollection;

    private static bool IsRaggableTreeCollection(string? collection)
        => string.Equals(collection, RaggableTreeCollection, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<(string SourceId, string Content, double Score)>> SearchRaggableTreeAsync(
        string text, int topK, CancellationToken ct)
    {
        var semantic = new SemanticQuery
        {
            Text = text,
            TopK = topK,
        };
        var hits = await _raggableStore!.SemanticSearchAsync(semantic, ct).ConfigureAwait(false);
        return hits
            .Select(h => (h.Fqn, h.SummaryShort ?? h.Signature ?? string.Empty, h.Score))
            .ToList();
    }

    private static string FormatRaggableHits(IReadOnlyList<(string SourceId, string Content, double Score)> hits)
    {
        if (hits.Count == 0) return "No results.";
        var sb = new StringBuilder();
        foreach (var (sourceId, content, score) in hits)
        {
            var preview = content.Length > 100 ? content[..100] + "..." : content;
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"- [{sourceId}] (score: {score:F2}): {preview}"));
        }
        return sb.ToString();
    }
}
