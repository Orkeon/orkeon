using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Knowledge;

/// <summary>
/// Interface for knowledge sources.
/// </summary>
public interface IKnowledgeSource
{
    /// <summary>
    /// Gets the source identifier.
    /// </summary>
    KnowledgeSourceId Id { get; }

    /// <summary>
    /// Gets the source name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the source type.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Retrieves content from the knowledge source.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The knowledge content.</returns>
    Task<KnowledgeContent> GetContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the knowledge source.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Matching knowledge content.</returns>
    Task<IEnumerable<KnowledgeContent>> SearchAsync(string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents content from a knowledge source.
/// </summary>
public sealed record KnowledgeContent
{
    /// <summary>Gets the content identifier.</summary>
    public KnowledgeContentId Id { get; init; } = KnowledgeContentId.Create();
    /// <summary>Gets the content title.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>Gets the content text.</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>Gets the source reference.</summary>
    public string Source { get; init; } = string.Empty;
    /// <summary>Gets additional metadata.</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    /// <summary>Gets when the content was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Gets the relevance score (0.0 to 1.0).</summary>
    public double Relevance { get; init; } = 1.0;
}
