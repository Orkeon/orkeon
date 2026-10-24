using System.Text.Json.Serialization;

namespace Orkeon.Tools.Rag;

/// <summary>Typed request of <see cref="RagEvalTool"/>.</summary>
public record RagEvalRequest
{
    /// <summary>Gets the virtual path of the golden dataset.</summary>
    [JsonPropertyName("dataset")]
    public string Dataset { get; init; } = "";

    /// <summary>Gets the collection selector.</summary>
    [JsonPropertyName("collection")]
    public string? Collection { get; init; }

    /// <summary>Gets the profile.</summary>
    [JsonPropertyName("profile")]
    public string? Profile { get; init; }

    /// <summary>Gets the profiles to compare (csv string or array).</summary>
    [JsonPropertyName("compare")]
    public object? Compare { get; init; }

    /// <summary>Gets the metric cutoff.</summary>
    [JsonPropertyName("k")]
    public int K { get; init; } = 5;

    /// <summary>Gets a value indicating whether the LLM judge is used.</summary>
    [JsonPropertyName("use_llm_judge")]
    public bool UseLlmJudge { get; init; }

    /// <summary>Gets a value indicating whether the corpus is fully reindexed.</summary>
    [JsonPropertyName("reindex")]
    public bool Reindex { get; init; }
}

/// <summary>Typed response of <see cref="RagEvalTool"/>.</summary>
public record RagEvalResponse
{
    /// <summary>Gets the aggregate metrics, one entry per evaluated profile.</summary>
    [JsonPropertyName("metrics")]
    public IReadOnlyList<RagEvalProfileMetrics> Metrics { get; init; } = [];

    /// <summary>Gets the per-case results, across the evaluated profiles.</summary>
    [JsonPropertyName("cases")]
    public IReadOnlyList<RagEvalCaseMetrics> Cases { get; init; } = [];

    /// <summary>Gets the report files written.</summary>
    [JsonPropertyName("reports")]
    public IReadOnlyList<string> Reports { get; init; } = [];
}

/// <summary>Aggregate metrics of one evaluated profile.</summary>
public record RagEvalProfileMetrics
{
    /// <summary>Gets the profile name.</summary>
    [JsonPropertyName("profile")]
    public string Profile { get; init; } = "";

    /// <summary>Gets the judge mode label (llm or heuristic).</summary>
    [JsonPropertyName("judge")]
    public string Judge { get; init; } = "";

    /// <summary>Gets the metric cutoff.</summary>
    [JsonPropertyName("k")]
    public int K { get; init; }

    /// <summary>Gets the mean recall@k.</summary>
    [JsonPropertyName("recall_at_k")]
    public double? RecallAtK { get; init; }

    /// <summary>Gets the mean precision@k.</summary>
    [JsonPropertyName("precision_at_k")]
    public double? PrecisionAtK { get; init; }

    /// <summary>Gets the mean reciprocal rank.</summary>
    [JsonPropertyName("mrr")]
    public double? Mrr { get; init; }

    /// <summary>Gets the mean groundedness.</summary>
    [JsonPropertyName("groundedness")]
    public double? Groundedness { get; init; }

    /// <summary>Gets the mean answer-relevance.</summary>
    [JsonPropertyName("answer_relevance")]
    public double? AnswerRelevance { get; init; }
}

/// <summary>Result of one case under one profile.</summary>
public record RagEvalCaseMetrics
{
    /// <summary>Gets the profile name.</summary>
    [JsonPropertyName("profile")]
    public string Profile { get; init; } = "";

    /// <summary>Gets the case id.</summary>
    [JsonPropertyName("case_id")]
    public string CaseId { get; init; } = "";

    /// <summary>Gets the recall@k of the case.</summary>
    [JsonPropertyName("recall_at_k")]
    public double? RecallAtK { get; init; }

    /// <summary>Gets the reciprocal rank of the case.</summary>
    [JsonPropertyName("reciprocal_rank")]
    public double? ReciprocalRank { get; init; }
}
