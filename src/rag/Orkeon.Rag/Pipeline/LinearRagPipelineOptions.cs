namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Options of the <see cref="LinearRagPipeline"/>.
/// Bound from configuration section <c>Orkeon:Rag:Pipeline</c> by
/// <c>AddOrkeonRag</c>.
/// </summary>
public sealed record LinearRagPipelineOptions
{
    /// <summary>
    /// Number of candidates retrieved from the store before truncation to
    /// <see cref="Abstractions.Models.RagQuery.TopN"/>. Defaults to the
    /// 50-candidate stage of the 50 → 5 cascade (guide §7.3).
    /// </summary>
    public int CandidateK { get; init; } = 50;

    /// <summary>
    /// Grounded system prompt used for generation. <c>null</c> selects
    /// <see cref="LinearRagPipeline.DefaultSystemPrompt"/>.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Sampling temperature passed to the chat client.</summary>
    public float? Temperature { get; init; }

    /// <summary>Maximum output tokens passed to the chat client.</summary>
    public int? MaxOutputTokens { get; init; }
}
