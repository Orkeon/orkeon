using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class CodebaseSearchTool : ToolBase<CodebaseSearchRequest, CodebaseSearchResponse>
{
    private readonly IRaggableStore _store;

    public CodebaseSearchTool(IRaggableStore store, ILogger<CodebaseSearchTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "codebase_search";
    public override string Description => "Semantic (vector) search over the codebase. Returns ranked hits with FQN and score.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<CodebaseSearchResponse> ExecuteTypedAsync(CodebaseSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<CodebaseSearchResponse> ExecuteCoreAsync()
        {
            var query = new SemanticQuery
            {
                Text = request.Query,
                TopK = request.TopK,
                MinScore = request.MinScore,
                PreFilter = BuildPreFilter(request),
            };
            var hits = await _store.SemanticSearchAsync(query, cancellationToken).ConfigureAwait(false);

            var shaped = request.IncludeSignature
                ? hits
                : hits.Select(h => h with { Signature = null });
            return new CodebaseSearchResponse
            {
                Hits = [.. shaped],
                TotalCandidates = hits.Count,
                Truncated = hits.Count >= request.TopK,
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
            Take = 1000,
        };
    }
}
