namespace Orkeon.Domain.Knowledge;

/// <summary>
/// Declarative attachment of a knowledge (RAG) collection to an agent (RAG-03/C4).
/// Carries the retrieval parameters used when the agent's prompts are assembled:
/// which collection to query, how many chunks to keep, the minimum relevance score,
/// the query profile, and the context-injection budget. Pure configuration — this
/// value object triggers no retrieval by itself; the prompt wiring consumes it at
/// execution-context assembly time (plan RAG §8.2–8.3).
/// </summary>
public sealed record KnowledgeAttachment
{
    /// <summary>Default number of chunks retained per query when <see cref="TopK"/> is not specified.</summary>
    public const int DefaultTopK = 5;

    /// <summary>Gets the name of the knowledge collection to query (required, never blank).</summary>
    public required string Collection { get; init; }

    /// <summary>Gets the maximum number of chunks retained per query. Must be positive. Default: <see cref="DefaultTopK"/>.</summary>
    public int TopK { get; init; } = DefaultTopK;

    /// <summary>Gets the minimum relevance score in [0, 1], or null to keep the provider/profile default.</summary>
    public double? MinScore { get; init; }

    /// <summary>
    /// Gets the query profile name (free-form string for now, e.g. <c>"fast"</c> | <c>"balanced"</c> |
    /// <c>"quality"</c>), or null to use the crew-level default profile.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// Gets the maximum number of tokens of retrieved context injected into the prompt,
    /// or null to use the global RAG context budget. Must be positive when set.
    /// </summary>
    public int? MaxContextTokens { get; init; }

    /// <summary>
    /// Creates a validated <see cref="KnowledgeAttachment"/>.
    /// </summary>
    /// <param name="collection">Name of the knowledge collection (required, never blank).</param>
    /// <param name="topK">Maximum number of chunks retained per query (default: <see cref="DefaultTopK"/>).</param>
    /// <param name="minScore">Optional minimum relevance score in [0, 1].</param>
    /// <param name="profile">Optional query profile name (free-form).</param>
    /// <param name="maxContextTokens">Optional cap on injected context tokens.</param>
    /// <exception cref="ArgumentException">When any value violates an invariant.</exception>
    public static KnowledgeAttachment Create(
        string collection,
        int topK = DefaultTopK,
        double? minScore = null,
        string? profile = null,
        int? maxContextTokens = null)
    {
        var attachment = new KnowledgeAttachment
        {
            Collection = collection?.Trim() ?? string.Empty,
            TopK = topK,
            MinScore = minScore,
            Profile = string.IsNullOrWhiteSpace(profile) ? null : profile.Trim(),
            MaxContextTokens = maxContextTokens,
        };
        attachment.Validate();
        return attachment;
    }

    /// <summary>
    /// Validates the attachment invariants: non-blank collection, positive <see cref="TopK"/>,
    /// <see cref="MinScore"/> within [0, 1], positive <see cref="MaxContextTokens"/> when set.
    /// </summary>
    /// <exception cref="ArgumentException">When any value violates an invariant.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Collection))
            throw new ArgumentException("Knowledge attachment requires a non-empty collection name.", nameof(Collection));

        if (TopK <= 0)
            throw new ArgumentException($"Knowledge attachment TopK must be positive (got {TopK}).", nameof(TopK));

        if (MinScore is < 0.0 or > 1.0)
            throw new ArgumentException($"Knowledge attachment MinScore must be within [0, 1] (got {MinScore}).", nameof(MinScore));

        if (MaxContextTokens is <= 0)
            throw new ArgumentException($"Knowledge attachment MaxContextTokens must be positive (got {MaxContextTokens}).", nameof(MaxContextTokens));
    }
}
