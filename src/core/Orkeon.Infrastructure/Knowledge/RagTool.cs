using System.Globalization;
using System.Text;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Infrastructure.Knowledge;

/// <summary>
/// Generic retrieval tool exposed to agents. Routes queries to either the RAG pipeline
/// or, when <c>collection = "raggable-tree"</c>, to the <see cref="IRaggableStore"/>
/// semantic search. Non-RaggableTree collections retain the original pipeline behavior.
/// </summary>
public class RagTool : IBaseTool, IRagTool
{
    /// <summary>Collection identifier that routes queries to the <see cref="IRaggableStore"/> code index.</summary>
    public const string RaggableTreeCollection = "raggable-tree";

    private readonly IRagPipeline _ragPipeline;
    private readonly IRaggableStore? _raggableStore;

    /// <inheritdoc />
    public string Name => "rag_search";

    /// <inheritdoc />
    public string Description =>
        "Search knowledge bases and retrieve answers grounded in documents. " +
        "Use this when you need factual information from the agent's knowledge sources.";

    /// <inheritdoc />
    public ToolSchema Schema => new(
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
                "Optional backend selector (e.g. 'raggable-tree' for the code index)",
                Required: false)
        });

    /// <summary>Initializes a new instance of <see cref="RagTool"/>.</summary>
    public RagTool(IRagPipeline ragPipeline) : this(ragPipeline, raggableStore: null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="RagTool"/> with an optional RaggableTree backend.</summary>
    public RagTool(IRagPipeline ragPipeline, IRaggableStore? raggableStore)
    {
        ArgumentNullException.ThrowIfNull(ragPipeline);
        _ragPipeline = ragPipeline;
        _raggableStore = raggableStore;
    }

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(
        ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ToolCallResponse> CallCoreAsync()
        {
            var question = request.Parameters.TryGetValue("question", out var q)
                ? q?.ToString()
                : null;

            if (string.IsNullOrWhiteSpace(question))
                return new ToolCallResponse(false, null, "question parameter is required");

            var topK = request.Parameters.TryGetValue("top_k", out var k)
                ? Convert.ToInt32(k, CultureInfo.InvariantCulture)
                : 3;

            var collection = request.Parameters.TryGetValue("collection", out var c)
                ? c?.ToString()
                : null;

            if (IsRaggableTreeCollection(collection) && _raggableStore is not null)
            {
                var hits = await SearchRaggableTreeAsync(question!, topK, cancellationToken).ConfigureAwait(false);
                return new ToolCallResponse(true, FormatHits(hits), null);
            }

            var result = await _ragPipeline.ExecuteAsync(
                question!,
                new RagOptions { Retrieval = new RetrievalOptions { TopK = topK } },
                cancellationToken).ConfigureAwait(false);

            return new ToolCallResponse(true, FormatPipelineResult(result), null);
        }
    }

    private static string FormatPipelineResult(RagResult result)
    {
        var response = new StringBuilder();
        response.AppendLine(result.Answer);

        if (result.Sources.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("Sources:");
            foreach (var source in result.Sources)
            {
                var preview = source.Content.Length > 100
                    ? source.Content[..100] + "..."
                    : source.Content;
                response.AppendLine(
                    Inv.Format($"- [{source.SourceId}] (score: {source.RelevanceScore:F2}): {preview}"));
            }
        }

        return response.ToString();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RagSearchHit>> SearchAsync(RagToolQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SearchCoreAsync(query, ct);
    }

    private async Task<IReadOnlyList<RagSearchHit>> SearchCoreAsync(RagToolQuery query, CancellationToken ct)
    {
        if (IsRaggableTreeCollection(query.Collection) && _raggableStore is not null)
        {
            return await SearchRaggableTreeAsync(query.Text, query.TopK, ct).ConfigureAwait(false);
        }

        var pipelineResult = await _ragPipeline.ExecuteAsync(
            query.Text,
            new RagOptions { Retrieval = new RetrievalOptions { TopK = query.TopK } },
            ct).ConfigureAwait(false);

        return pipelineResult.Sources
            .Select(s => new RagSearchHit
            {
                SourceId = s.SourceId,
                Content = s.Content,
                Score = s.RelevanceScore,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        string input, CancellationToken cancellationToken = default)
    {
        var request = new ToolCallRequest(
            Name,
            new Dictionary<string, object?> { ["question"] = input });

        var response = await CallAsync(request, cancellationToken).ConfigureAwait(false);

        return new ToolResult
        {
            Success = response.Success,
            Output = response.Result?.ToString(),
            Error = response.Error
        };
    }

    /// <inheritdoc />
    public bool ValidateInput(string input) => !string.IsNullOrWhiteSpace(input);

    private static bool IsRaggableTreeCollection(string? collection)
        => string.Equals(collection, RaggableTreeCollection, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<RagSearchHit>> SearchRaggableTreeAsync(string text, int topK, CancellationToken ct)
    {
        var semantic = new SemanticQuery
        {
            Text = text,
            TopK = topK,
        };
        var hits = await _raggableStore!.SemanticSearchAsync(semantic, ct).ConfigureAwait(false);
        return hits.Select(h => new RagSearchHit
        {
            SourceId = h.Fqn,
            Content = h.SummaryShort ?? h.Signature ?? string.Empty,
            Score = (float)h.Score,
        }).ToList();
    }

    private static string FormatHits(IReadOnlyList<RagSearchHit> hits)
    {
        if (hits.Count == 0) return "No results.";
        var sb = new StringBuilder();
        foreach (var hit in hits)
        {
            var preview = hit.Content.Length > 100 ? hit.Content[..100] + "..." : hit.Content;
            sb.AppendLine(Inv.Format($"- [{hit.SourceId}] (score: {hit.Score:F2}): {preview}"));
        }
        return sb.ToString();
    }
}
