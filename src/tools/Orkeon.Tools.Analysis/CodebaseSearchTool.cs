using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class CodebaseSearchTool : ToolBase<CodebaseSearchRequest, CodebaseSearchResponse>
{
    private readonly IRaggableStore _store;
    private readonly IndexFreshnessService? _freshness;

    public CodebaseSearchTool(
        IRaggableStore store,
        ILogger<CodebaseSearchTool>? logger = null,
        IndexFreshnessService? freshness = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _freshness = freshness;
    }

    public override string Name => "codebase_search";
    public override string Description => "Where in the codebase is this concept handled? Searches the indexed symbols by meaning (vector) and by words (BM25) and returns at most top_k hits (10) ranked, each with its FQN, score and match origin; truncated says when more match. The FQN of a hit feeds symbol_detail, sub_graph and impact_analysis. For an exact text, grep; for a code shape, structural_search.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    // A small model fills every optional field and sends 0: refuse it rather than return an empty result.
    protected override string? ValidateTypedRequest(CodebaseSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TopK < 1)
            return $"top_k must be at least 1 (got {request.TopK}); omit it for the default of 10";
        return null;
    }

    protected override Task<CodebaseSearchResponse> ExecuteTypedAsync(CodebaseSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<CodebaseSearchResponse> ExecuteCoreAsync()
        {
            // Lazy freshness (PLAN B3): reindex the edited files BEFORE answering, so a
            // search right after the agent's own edits sees them. Zero cost when clean.
            var refreshed = _freshness is null
                ? 0
                : await _freshness.EnsureFreshAsync(cancellationToken).ConfigureAwait(false);

            var preFilter = BuildPreFilter(request);
            // One hit past the page: truncated says that more hits match, never that the page is full (#283).
            var topK = request.TopK;
            var query = new SemanticQuery
            {
                Text = request.Query,
                TopK = topK + 1,
                MinScore = request.MinScore,
                PreFilter = preFilter,
                Mode = request.Mode,
            };
            var result = await _store.SemanticSearchAsync(query, cancellationToken).ConfigureAwait(false);
            var hits = result.Hits;

            var page = hits.Take(topK).ToList();
            var shaped = request.IncludeSignature
                ? page
                : page.Select(h => h with { Signature = null });
            return new CodebaseSearchResponse
            {
                Hits = [.. shaped],
                TotalCandidates = page.Count,
                // The store says when the prefilter kept only the first of the nodes it matched (#280).
                Truncated = hits.Count > topK || result.PreFilterCapped,
                RefreshedFiles = refreshed,
            };
        }
    }

    private static NodeQuery? BuildPreFilter(CodebaseSearchRequest request)
    {
        if (request.FilterKinds.IsDefaultOrEmpty && request.FilterPackages.IsDefaultOrEmpty
            && request.FilterLanguages.IsDefaultOrEmpty && request.Levels.IsDefaultOrEmpty)
        {
            return null;
        }

        return new NodeQuery
        {
            Level = request.Levels.IsDefaultOrEmpty ? null : request.Levels[0],
            Kinds = request.FilterKinds.IsDefaultOrEmpty ? null : request.FilterKinds,
            Languages = request.FilterLanguages.IsDefaultOrEmpty ? null : request.FilterLanguages,
            Packages = request.FilterPackages.IsDefaultOrEmpty ? null : request.FilterPackages,
            // As many candidates as the store keeps; SemanticSearchResult.PreFilterCapped says when it cut them.
            Take = int.MaxValue,
        };
    }
}
