namespace Orkeon.Application.Configuration;

/// <summary>
/// Options for configuring the Orkeon Application layer.
/// </summary>
public class OrkeonApplicationOptions
{
    /// <summary>
    /// Gets or sets the strategy used to select the best agent for a task.
    /// <para>
    /// <see cref="AgentSelectionStrategyKind.Embedding"/> uses semantic similarity.
    /// The default <c>IEmbeddingService</c> adapts the <c>IEmbeddingProvider</c> port,
    /// whose resolution is semantic-first (local BGE via <c>AddOrkeonLocalEmbeddings()</c>,
    /// else the <c>Orkeon:Embeddings</c> remote configuration, else fail-fast at first use).
    /// </para>
    /// </summary>
    public AgentSelectionStrategyKind AgentSelectionStrategy { get; set; } = AgentSelectionStrategyKind.FirstFit;
}

/// <summary>
/// Strategy used to select the best agent for a task.
/// </summary>
public enum AgentSelectionStrategyKind
{
    /// <summary>
    /// Selects the first available agent (safe fallback, no semantic ranking).
    /// </summary>
    FirstFit,

    /// <summary>
    /// Selects the agent whose profile embedding is most similar to the task
    /// (semantic selection — requires a real embedding provider).
    /// </summary>
    Embedding,

    /// <summary>
    /// Selects the agent whose role/goal/backstory keywords best match the task
    /// (lexical Jaccard similarity, no embedding provider required).
    /// </summary>
    Skill
}
