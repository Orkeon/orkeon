using System.Globalization;
using System.Text;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag;

/// <summary>
/// Generic retrieval tool exposed to agents (<c>rag_search</c>). Routes queries to
/// the RAG subsystem's <see cref="IRagPipeline"/> or, when
/// <c>collection = "raggable-tree"</c>, to the <see cref="IRaggableStore"/> semantic
/// code search. Replaces the legacy Infrastructure <c>RagTool</c>
/// (RAG-02/C5) with the same agent-facing name, schema, and output format
/// (answer + <c>Sources:</c> block with scores).
/// </summary>
public class RagSearchTool : IBaseTool
{
    /// <summary>Collection queried when the agent names none.</summary>
    public const string DefaultCollection = "default";

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
                "Optional collection selector (e.g. 'raggable-tree' for the code index)",
                Required: false)
        });

    /// <summary>Initializes a new instance of <see cref="RagSearchTool"/>.</summary>
    public RagSearchTool(IRagPipeline ragPipeline) : this(ragPipeline, raggableStore: null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="RagSearchTool"/> with an optional RaggableTree backend.</summary>
    public RagSearchTool(IRagPipeline ragPipeline, IRaggableStore? raggableStore)
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

            var topK = request.Parameters.TryGetValue("top_k", out var k) && k is not null
                ? Convert.ToInt32(k, CultureInfo.InvariantCulture)
                : 3;

            var collection = request.Parameters.TryGetValue("collection", out var c)
                ? c?.ToString()
                : null;

            if (IsRaggableTreeCollection(collection) && _raggableStore is not null)
            {
                var hits = await SearchRaggableTreeAsync(question!, topK, cancellationToken).ConfigureAwait(false);
                return new ToolCallResponse(true, FormatRaggableHits(hits), null);
            }

            var answer = await _ragPipeline.QueryAsync(
                new RagQuery
                {
                    Text = question!,
                    Collection = string.IsNullOrWhiteSpace(collection) ? DefaultCollection : collection!,
                    TopN = topK,
                },
                cancellationToken).ConfigureAwait(false);

            return new ToolCallResponse(true, FormatAnswer(answer), null);
        }
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

    /// <summary>
    /// Formats a <see cref="RagAnswer"/> the way the legacy tool did — the answer text,
    /// then a <c>Sources:</c> block listing each citation with its score and a content
    /// preview — so agent prompts keep working unchanged.
    /// </summary>
    private static string FormatAnswer(RagAnswer answer)
    {
        var response = new StringBuilder();
        response.AppendLine(answer.Text);

        if (answer.Citations.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("Sources:");
            foreach (var citation in answer.Citations)
            {
                var content = citation.Snippet ?? string.Empty;
                var preview = content.Length > 100 ? content[..100] + "..." : content;
                response.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"- [{citation.SourceId}] (score: {citation.Score:F2}): {preview}"));
            }
        }

        return response.ToString();
    }

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
