using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Rag;

/// <summary>
/// Evaluation tool exposed to agents (<c>rag_eval</c>, RAG-04/C1): runs a golden
/// dataset against a collection through the RAG evaluation harness — a crew can
/// self-evaluate its knowledge base (plan §9.3). The corpus referenced by the
/// dataset is ingested first (incremental), each requested profile produces a
/// report (recall@k, precision@k, MRR, groundedness, answer-relevance with the
/// judge mode labelled), and the markdown/JSON reports are written through the
/// VFS.
/// </summary>
public class RagEvalTool : IBaseTool
{
    private readonly IRagEvalHarness _harness;

    /// <inheritdoc />
    public string Name => "rag_eval";

    /// <inheritdoc />
    public string Description =>
        "Evaluate a RAG knowledge collection against a golden dataset (YAML): recall@k, precision@k, MRR, " +
        "groundedness and answer-relevance (judge mode labelled: llm or heuristic). " +
        "Writes markdown/JSON reports and returns the aggregate summary.";

    /// <inheritdoc />
    public ToolSchema Schema => new(
        Name: Name,
        Description: Description,
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["dataset"] = new ParameterSchema(
                "string",
                "Virtual path of the golden dataset YAML (e.g. '/workspace/examples/rag/eval/golden.yaml')",
                Required: true),
            ["collection"] = new ParameterSchema(
                "string",
                "Collection to evaluate (default: the dataset's collection, else 'rag-eval-{dataset name}')",
                Required: false),
            ["profile"] = new ParameterSchema(
                "string",
                "Profile to evaluate (default: 'default')",
                Required: false),
            ["compare"] = new ParameterSchema(
                "string",
                "Comma-separated profiles to compare (e.g. 'fast,balanced,quality'); overrides 'profile'",
                Required: false),
            ["k"] = new ParameterSchema(
                "integer",
                "Metric cutoff for recall@k / precision@k (default: 5)",
                Required: false,
                Default: 5),
            ["use_llm_judge"] = new ParameterSchema(
                "boolean",
                "Judge generation with the configured LLM (falls back to the deterministic heuristic, always labelled)",
                Required: false,
                Default: false),
            ["reindex"] = new ParameterSchema(
                "boolean",
                "Force a full corpus reindex before evaluating (required after an embedding model change)",
                Required: false,
                Default: false),
        });

    /// <summary>Initializes a new instance of <see cref="RagEvalTool"/>.</summary>
    public RagEvalTool(IRagEvalHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        _harness = harness;
    }

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(
        ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ToolCallResponse> CallCoreAsync()
        {
            var dataset = request.Parameters.TryGetValue("dataset", out var d) ? d?.ToString() : null;
            if (string.IsNullOrWhiteSpace(dataset))
                return new ToolCallResponse(false, null, "dataset parameter is required");

            var collection = request.Parameters.TryGetValue("collection", out var c) ? c?.ToString() : null;
            var profile = request.Parameters.TryGetValue("profile", out var p) ? p?.ToString() : null;
            var compare = ExtractProfiles(request.Parameters.TryGetValue("compare", out var cmp) ? cmp : null);

            var k = request.Parameters.TryGetValue("k", out var kRaw) && kRaw is not null
                ? Convert.ToInt32(kRaw, CultureInfo.InvariantCulture)
                : 5;

            var useLlmJudge = request.Parameters.TryGetValue("use_llm_judge", out var j) && ToBool(j);
            var reindex = request.Parameters.TryGetValue("reindex", out var r) && ToBool(r);

            // `compare` wins over `profile`; neither given means "the default pipeline".
            ImmutableList<string> singleProfile =
                string.IsNullOrWhiteSpace(profile) ? [] : ImmutableList.Create(profile!);

            var profiles = compare.Count > 0 ? compare : singleProfile;

            var result = await _harness.RunAsync(
                new RagEvalRunRequest
                {
                    DatasetPath = dataset!,
                    Profiles = profiles,
                    Collection = string.IsNullOrWhiteSpace(collection) ? null : collection,
                    K = k,
                    UseLlmJudge = useLlmJudge,
                    ReindexCorpus = reindex,
                },
                cancellationToken).ConfigureAwait(false);

            return new ToolCallResponse(true, FormatResult(result), null);
        }
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        // The legacy single-string entrypoint carries no dataset/profile structure  —
        // rag_eval is only meaningful through the structured protocol.
        => Task.FromResult(new ToolResult
        {
            Success = false,
            Error = "rag_eval requires structured parameters: { dataset[, collection, profile, compare, k, use_llm_judge] }",
        });

    /// <inheritdoc />
    public bool ValidateInput(string input) => false;

    /// <summary>
    /// Formats a harness result as the agent-facing text block: one aggregate line
    /// per profile (judge mode labelled), the comparison table for multi-profile
    /// runs, then the report files written. Public so the CLI
    /// (<c>orkeon rag eval</c>) prints the exact same summary.
    /// </summary>
    public static string FormatResult(RagEvalRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"RAG evaluation — dataset '{result.Dataset.Name}' ({result.Dataset.Cases.Count} case(s)), collection '{result.Collection}'"));

        if (result.Ingestion is { } ingestion)
        {
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Corpus: {ingestion.SourcesAdded} added, {ingestion.SourcesUnchanged} unchanged, {ingestion.SourcesReingested} re-ingested"));
        }

        foreach (var report in result.Reports)
        {
            var a = report.Aggregate;
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Profile '{report.Profile}' — judge: {RagEvalReportWriter.JudgeLabel(report)}"));
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"- recall@{report.K}: {Metric(a.RecallAtK)} | precision@{report.K}: {Metric(a.PrecisionAtK)} | MRR: {Metric(a.Mrr)} | groundedness: {Metric(a.Groundedness)} | answer-relevance: {Metric(a.AnswerRelevance)} | duration: {report.Duration.TotalSeconds:F2}s"));
        }

        if (result.Reports.Count > 1)
        {
            sb.AppendLine();
            sb.Append(RagEvalReportWriter.ToComparisonMarkdown(result.Reports));
        }

        if (result.WrittenFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Reports:");
            foreach (var file in result.WrittenFiles)
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {file}");
            if (result.ComparisonFile is not null)
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {result.ComparisonFile}");
        }

        return sb.ToString();
    }

    private static string Metric(double? value)
        => value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "n/a";

    /// <summary>
    /// Tolerant extraction of the <c>compare</c> parameter: a comma-separated
    /// string, any enumerable of values, or a JSON array.
    /// </summary>
    internal static ImmutableList<string> ExtractProfiles(object? raw)
    {
        var profiles = new List<string>();
        switch (raw)
        {
            case null:
                break;
            case string csv:
                profiles.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                foreach (var item in array.EnumerateArray())
                {
                    var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) profiles.Add(value!.Trim());
                }
                break;
            case JsonElement { ValueKind: JsonValueKind.String } str:
                var text = str.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    profiles.AddRange(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    var value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) profiles.Add(value!.Trim());
                }
                break;
            default:
                var fallback = raw.ToString();
                if (!string.IsNullOrWhiteSpace(fallback))
                    profiles.AddRange(fallback!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
        }

        return [.. profiles];
    }

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.String } s => bool.TryParse(s.GetString(), out var parsed) && parsed,
        string text => bool.TryParse(text, out var parsed) && parsed,
        _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
    };
}
