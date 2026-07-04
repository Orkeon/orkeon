using System.Globalization;

namespace Orkeon.Application.Rag;

/// <summary>
/// Represents an item of knowledge with its metadata and embedding.
/// </summary>
public record KnowledgeItem(
    string Id,
    string Content,
    string Source,
    Dictionary<string, object>? Metadata = null,
    IReadOnlyList<float>? Embedding = null,
    double? SimilarityScore = null,
    DateTime? CreatedAt = null)
{
    /// <summary>
    /// Creates a new knowledge item with auto-generated ID.
    /// </summary>
    public static KnowledgeItem Create(
        string content,
        string source,
        Dictionary<string, object>? metadata = null)
    {
        return new KnowledgeItem(
            Id: Guid.NewGuid().ToString(),
            Content: content,
            Source: source,
            Metadata: metadata,
            CreatedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// To String.
    /// </summary>
    public override string ToString()
    {
        var similarityStr = SimilarityScore?.ToString(CultureInfo.InvariantCulture) ?? "null";
        return $"KnowledgeItem {{ Id = {Id}, Content = {Content}, Source = {Source}, Metadata = {Metadata}, Embedding = {Embedding}, SimilarityScore = {similarityStr}, CreatedAt = {CreatedAt} }}";
    }
}
