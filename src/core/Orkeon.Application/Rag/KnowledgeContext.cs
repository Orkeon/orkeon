namespace Orkeon.Application.Rag;

/// <summary>
/// Represents the knowledge context available for a specific task or query.
/// </summary>
public class KnowledgeContext
{
    private readonly List<KnowledgeItem> _relevantKnowledge = [];
    private KnowledgeContextMetadata _metadata = KnowledgeContextMetadata.Empty;

    /// <summary>
    /// Gets the relevant knowledge items.
    /// </summary>
    public IReadOnlyList<KnowledgeItem> RelevantKnowledge => _relevantKnowledge.AsReadOnly();

    /// <summary>
    /// Gets the context metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata => _metadata.ToDictionary();

    /// <summary>
    /// Adds relevant knowledge items to the context.
    /// </summary>
    public void AddRelevantKnowledge(IEnumerable<KnowledgeItem> items)
    {
        _relevantKnowledge.AddRange(items);
    }

    /// <summary>
    /// Adds a single knowledge item to the context.
    /// </summary>
    public void AddRelevantKnowledge(KnowledgeItem item)
    {
        _relevantKnowledge.Add(item);
    }

    /// <summary>
    /// Sets metadata for the context.
    /// </summary>
    public void SetMetadata(string key, object value)
    {
        _metadata = _metadata.Set(key, value);
    }

    /// <summary>
    /// Gets the most relevant knowledge items based on similarity score.
    /// </summary>
    public IEnumerable<KnowledgeItem> GetTopRelevant(int topK)
    {
        return _relevantKnowledge
            .Where(k => k.SimilarityScore.HasValue)
            .OrderByDescending(k => k.SimilarityScore!.Value)
            .Take(topK);
    }

    /// <summary>
    /// Combines the content of all relevant knowledge items.
    /// </summary>
    public string GetCombinedContent(string separator = "\n\n")
    {
        return string.Join(separator, _relevantKnowledge.Select(k => k.Content));
    }
}
