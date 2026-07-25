namespace Orkeon.Domain.Knowledge;

/// <summary>
/// Mutable option bag for the <c>AgentBuilder.WithKnowledge("collection", opts =&gt; …)</c>
/// overload. Mirrors the optional fields of <see cref="KnowledgeAttachment"/>; the builder
/// materializes it into a validated attachment via <see cref="KnowledgeAttachment.Create"/>.
/// </summary>
public sealed class KnowledgeAttachmentOptions
{
    /// <summary>Gets or sets the maximum number of chunks retained per query. Default: <see cref="KnowledgeAttachment.DefaultTopK"/>.</summary>
    public int TopK { get; set; } = KnowledgeAttachment.DefaultTopK;

    /// <summary>Gets or sets the minimum relevance score in [0, 1], or null for the profile default.</summary>
    public double? MinScore { get; set; }

    /// <summary>Gets or sets the query profile name (free-form), or null for the crew default.</summary>
    public string? Profile { get; set; }

    /// <summary>Gets or sets the cap on injected context tokens, or null for the global budget.</summary>
    public int? MaxContextTokens { get; set; }
}
