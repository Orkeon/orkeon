using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;
using Orkeon.Tools.Abstractions.Base;
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
public class RagEvalTool : ToolBase<RagEvalRequest, RagEvalResponse>
{
    /// <summary>Metric cutoff applied when the caller gives no <c>k</c>.</summary>
    private const int DefaultMetricCutoff = 5;

    private const string DatasetRequired = "dataset parameter is required";

    private readonly IRagEvalHarness _harness;
    private readonly Func<string?> _defaultCollection;

    /// <inheritdoc />
    public override string Name => "rag_eval";

    /// <inheritdoc />
    public override string Description =>
        "Evaluate a RAG knowledge collection against a golden dataset (YAML): recall@k, precision@k, MRR, " +
        "groundedness and answer-relevance (judge mode labelled: llm or heuristic). " +
        "Writes markdown/JSON reports and returns the aggregate summary.";

    /// <inheritdoc />
    public override ToolSchema Schema => new(
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
                "Collection to evaluate (default: the dataset's collection; for a dataset without corpus, the configured default collection; else 'rag-eval-{dataset name}')",
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
                Default: DefaultMetricCutoff),
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
    public RagEvalTool(IRagEvalHarness harness) : this(harness, (string?)null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="RagEvalTool"/> with the host's default collection.</summary>
    /// <param name="harness">The evaluation harness.</param>
    /// <param name="defaultCollection">
    /// The host's default collection (<c>Orkeon:Rag:Collection</c>), handed to the harness
    /// as <see cref="RagEvalRunRequest.DefaultCollection"/> when the call names none.
    /// </param>
    public RagEvalTool(IRagEvalHarness harness, string? defaultCollection)
        : this(harness, () => defaultCollection)
    {
    }

    /// <summary>
    /// Deferred form used by <c>AddOrkeonRagTools</c>: the default collection is read at the
    /// first call, so building the tool resolves nothing of the RAG subsystem.
    /// </summary>
    internal RagEvalTool(IRagEvalHarness harness, Func<string?> defaultCollectionSource)
    {
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(defaultCollectionSource);
        _harness = harness;
        _defaultCollection = defaultCollectionSource;
    }

    /// <inheritdoc />
    protected override async Task<RagEvalResponse> ExecuteTypedAsync(
        RagEvalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var compare = ExtractProfiles(request.Compare);
        ImmutableList<string> singleProfile =
            string.IsNullOrWhiteSpace(request.Profile) ? [] : ImmutableList.Create(request.Profile);

        var result = await _harness.RunAsync(
            new RagEvalRunRequest
            {
                DatasetPath = request.Dataset,
                Profiles = compare.Count > 0 ? compare : singleProfile,
                Collection = string.IsNullOrWhiteSpace(request.Collection) ? null : request.Collection,
                K = request.K,
                UseLlmJudge = request.UseLlmJudge,
                ReindexCorpus = request.Reindex,
                DefaultCollection = _defaultCollection(),
            },
            cancellationToken).ConfigureAwait(false);

        return new RagEvalResponse
        {
            Metrics =
            [
                .. result.Reports.Select(r => new RagEvalProfileMetrics
                {
                    Profile = r.Profile,
                    Judge = RagEvalReportWriter.JudgeLabel(r),
                    K = r.K,
                    RecallAtK = r.Aggregate.RecallAtK,
                    PrecisionAtK = r.Aggregate.PrecisionAtK,
                    Mrr = r.Aggregate.Mrr,
                    Groundedness = r.Aggregate.Groundedness,
                    AnswerRelevance = r.Aggregate.AnswerRelevance,
                }),
            ],
            Cases =
            [
                .. result.Reports.SelectMany(r => r.Cases.Select(c => new RagEvalCaseMetrics
                {
                    Profile = r.Profile,
                    CaseId = c.CaseId,
                    RecallAtK = c.RecallAtK,
                    ReciprocalRank = c.ReciprocalRank,
                })),
            ],
            Reports = result.ComparisonFile is null
                ? [.. result.WrittenFiles]
                : [.. result.WrittenFiles, result.ComparisonFile],
        };
    }

    /// <inheritdoc />
    protected override ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.ContainsKey("dataset")
            ? base.ValidateParameters(parameters)
            : ValidationResult.Failed(DatasetRequired);
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(RagEvalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.IsNullOrWhiteSpace(request.Dataset) ? DatasetRequired : null;
    }

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
    internal static ImmutableList<string> ExtractProfiles(object? raw) => raw switch
    {
        null => [],
        string csv => SplitProfileList(csv),
        JsonElement { ValueKind: JsonValueKind.Array } array => ProfilesFromJsonArray(array),
        JsonElement { ValueKind: JsonValueKind.String } str => SplitProfileList(str.GetString()),
        IEnumerable enumerable => ProfilesFromEnumerable(enumerable),
        _ => SplitProfileList(raw.ToString()),
    };

    /// <summary>Splits a comma-separated profile list; blanks yield nothing.</summary>
    private static ImmutableList<string> SplitProfileList(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static ImmutableList<string> ProfilesFromJsonArray(JsonElement array)
    {
        var profiles = ImmutableList.CreateBuilder<string>();
        foreach (var item in array.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                profiles.Add(value.Trim());
        }

        return profiles.ToImmutable();
    }

    private static ImmutableList<string> ProfilesFromEnumerable(IEnumerable enumerable)
    {
        var profiles = ImmutableList.CreateBuilder<string>();
        foreach (var item in enumerable)
        {
            var value = item?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                profiles.Add(value.Trim());
        }

        return profiles.ToImmutable();
    }
}
